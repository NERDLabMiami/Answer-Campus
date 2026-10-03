#!/usr/bin/env python3
"""
Dry-run for migrating legacy ChoiceNode components (raw Button_Events UnityEvent
wiring) to the current ShowChoiceNode (Choice.nextConversation field), scoped to
only the conversations living under each scene's "Conversations" GameObject.

Produces, per scene, a MIGRATE list (safe to mechanically convert) and a FLAGGED
list (needs manual review, with reasons) -- see Docs/VNEngine Audit/Choice
Migration/README.md for the eligibility rules and how to read the output.

This is read-only: it never touches the .unity files. The actual migration is
performed by Assets/Editor/ChoiceNodeMigrator.cs, run inside the Unity Editor;
this script's migrate_fileids.json / hierarchy paths are what you diff the
Editor run's own report against to confirm the two agree before trusting it.
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import audit_vn_conversations as audit

OUT_DIR = os.path.join(audit.REPO, "Docs/VNEngine Audit/Choice Migration")

CONVERSATIONS_ROOT_NAME = "Conversations"

# Packed hex-blob fields: ChoiceNode serializes these as a single scalar hex
# string rather than a YAML list. Confirmed empirically: enum-backed fields
# (Has_Requirements, Requirement_Not_Met_Actions) use 8 hex chars (4 bytes) per
# element; bool fields (Show_Choice_Was_Selected_Before, Choice_Been_Clicked_Before)
# use 2 hex chars (1 byte) per element. Array length varies per instance (stale
# sizing from an older max_number_of_buttons), but element width is constant.
PACKED_FIELD_WIDTH_BYTES = {
    'Has_Requirements': 4,
    'Requirement_Not_Met_Actions': 4,
    'Show_Choice_Was_Selected_Before': 1,
    'Choice_Been_Clicked_Before': 1,
}

NAME_OF_CHOICE_RE = re.compile(r'^\s*Name_Of_Choice:\s*(.*)$')
LOCALIZE_RE = re.compile(r'^\s*Localize_Choice_Text:\s*([01])')
NUMBER_OF_CHOICES_RE = re.compile(r'^\s*Number_Of_Choices:\s*(\d+)')
LIST_FIELD_START_RE = re.compile(r'^(\s*)(Button_Text|choice_button_images):\s*$')
LIST_ITEM_RE = re.compile(r'^(\s*)-\s*(.*)$')
PACKED_FIELD_RE = re.compile(r'^\s*(' + '|'.join(PACKED_FIELD_WIDTH_BYTES) + r'):\s*(\S*)')
BUTTON_EVENTS_START_RE = re.compile(r'^(\s*)Button_Events:\s*$')
PERSISTENT_CALL_ITEM_START_RE = re.compile(r'^(\s*)-\s*m_PersistentCalls:\s*$')


def unescape_yaml_scalar(text):
    """Unity double-quotes a plain-scalar YAML string when it contains non-ASCII
    characters, escaping them as \\uXXXX (plus the usual \\n/\\t/\\"/\\\\). Decode
    that back to a normal string; leave anything else (unquoted scalars) untouched."""
    text = text.strip()
    if len(text) >= 2 and text[0] == '"' and text[-1] == '"':
        inner = text[1:-1]
        try:
            return inner.encode('utf-8').decode('unicode_escape').encode('latin-1').decode('utf-8')
        except (UnicodeDecodeError, UnicodeEncodeError):
            return re.sub(r'\\u([0-9a-fA-F]{4})', lambda m: chr(int(m.group(1), 16)), inner)
    return text


def decode_packed_array(hex_str, width_bytes):
    width_chars = width_bytes * 2
    n = len(hex_str) // width_chars
    out = []
    for i in range(n):
        chunk = hex_str[i * width_chars:(i + 1) * width_chars]
        try:
            out.append(int(chunk, 16))
        except ValueError:
            out.append(0)
    return out


def find_conversations_root(scene):
    matches = [fid for fid, go in scene.gameobjects.items() if go['name'] == CONVERSATIONS_ROOT_NAME]
    if len(matches) != 1:
        raise RuntimeError(
            f"{scene.name}: expected exactly one GameObject named "
            f"'{CONVERSATIONS_ROOT_NAME}', found {len(matches)}"
        )
    return matches[0]


def is_descendant_of(scene, go_fid, ancestor_go_fid, transform_of_go, stripped_transform_prefab):
    """Walk the Transform/PrefabInstance parent chain up from go_fid's own
    transform, looking for ancestor_go_fid. Mirrors audit.owning_conversation's
    walk, but terminates on a GameObject-identity match instead of a
    ConversationManager classification match."""
    cur = transform_of_go.get(go_fid)
    visited = set()
    while cur is not None and cur not in visited and cur != 0:
        visited.add(cur)
        if cur in scene.transforms:
            t = scene.transforms[cur]
            if not t['stripped'] and t['go'] == ancestor_go_fid:
                return True
            cur = t['father']
            continue
        if cur in stripped_transform_prefab:
            pi_fid = stripped_transform_prefab[cur]
            cur = scene.prefab_instances[pi_fid]['parent']
            continue
        break
    return False


def hierarchy_path(scene, go_fid, transform_of_go, stripped_transform_prefab, stop_at_fid):
    """Build a readable Conversations/.../NodeName path by walking up from
    go_fid to (and including) stop_at_fid. Mirrors the path format built by
    ChoiceNodeMigrator.cs's HierarchyPath (same join key for diffing)."""
    names = []
    cur_go = go_fid
    visited = set()
    while cur_go is not None:
        names.append(scene.gameobjects.get(cur_go, {}).get('name') or f"<GO {cur_go}>")
        if cur_go == stop_at_fid or cur_go in visited:
            break
        visited.add(cur_go)

        # Find cur_go's own transform, then move to ITS parent transform, then find
        # that parent transform's owning GameObject -- that's one level up.
        own_t = transform_of_go.get(cur_go)
        if own_t is None:
            break
        if own_t in scene.transforms:
            father_t = scene.transforms[own_t]['father']
        elif own_t in stripped_transform_prefab:
            pi_fid = stripped_transform_prefab[own_t]
            father_t = scene.prefab_instances[pi_fid]['parent']
        else:
            break

        if father_t in scene.transforms:
            cur_go = scene.transforms[father_t]['go']
        else:
            # Parent is itself a prefab-instance node with no named GameObject at
            # this level in our parsed model -- stop rather than guess.
            cur_go = None
    return '/'.join(reversed(names))


def parse_choice_node_fields(body_lines):
    """Per-button-aligned extraction of the fields relevant to migration
    eligibility, from a single inline ChoiceNode MonoBehaviour's body lines."""
    fields = {
        'name_of_choice': '',
        'localize': False,
        'number_of_choices': None,
        'button_text': [],
        'has_image': [],
        'has_requirement': [],
        'buttons': [],  # list of {'calls': [{'method':..,'target_type':..,'target_fid':..}, ...]}
    }

    i = 0
    n = len(body_lines)
    while i < n:
        line = body_lines[i]

        m = NAME_OF_CHOICE_RE.match(line)
        if m:
            fields['name_of_choice'] = m.group(1).strip()
            i += 1
            continue

        m = LOCALIZE_RE.match(line)
        if m:
            fields['localize'] = (m.group(1) == '1')
            i += 1
            continue

        m = NUMBER_OF_CHOICES_RE.match(line)
        if m:
            fields['number_of_choices'] = int(m.group(1))
            i += 1
            continue

        m = PACKED_FIELD_RE.match(line)
        if m:
            field, hex_str = m.group(1), m.group(2)
            width = PACKED_FIELD_WIDTH_BYTES[field]
            decoded = decode_packed_array(hex_str, width)
            if field == 'Has_Requirements':
                fields['has_requirement'] = [v != 0 for v in decoded]
            i += 1
            continue

        m = LIST_FIELD_START_RE.match(line)
        if m:
            indent, field_name = m.group(1), m.group(2)
            items = []
            i += 1
            while i < n:
                item_line = body_lines[i]
                im = LIST_ITEM_RE.match(item_line)
                if im and len(im.group(1)) >= len(indent):
                    items.append(im.group(2).strip())
                    i += 1
                    continue
                # A long plain YAML scalar in a list item can wrap onto a further-indented
                # continuation line with no leading "-" (YAML line folding) -- join it onto
                # the previous item rather than treating it as the end of the list.
                stripped = item_line.strip()
                if items and stripped and len(item_line) - len(item_line.lstrip()) > len(indent):
                    items[-1] = (items[-1] + ' ' + stripped).strip()
                    i += 1
                    continue
                break
            if field_name == 'Button_Text':
                fields['button_text'] = [unescape_yaml_scalar(it) for it in items]
            elif field_name == 'choice_button_images':
                fields['has_image'] = [bool(audit.OBJ_REF_RE.match('objectReference: ' + it)
                                             and re.search(r'fileID:\s*(?!0\b)\d', it)) for it in items]
            continue

        m = BUTTON_EVENTS_START_RE.match(line)
        if m:
            indent = m.group(1)
            i += 1
            while i < n:
                item_line = body_lines[i]
                cm = PERSISTENT_CALL_ITEM_START_RE.match(item_line)
                if not cm or len(cm.group(1)) < len(indent):
                    break
                item_indent = cm.group(1)
                i += 1
                calls_text_lines = []
                while i < n:
                    sub_line = body_lines[i]
                    stripped = sub_line.strip()
                    if sub_line.startswith(item_indent) and not sub_line.startswith(item_indent + ' '):
                        break  # dedented back to item_indent: next sibling "- m_PersistentCalls:" or end of array
                    calls_text_lines.append(sub_line)
                    i += 1
                calls = audit.extract_button_calls_from_inline_body(''.join(calls_text_lines))
                fields['buttons'].append({'calls': calls})
            continue

        i += 1

    return fields


def classify_choice_node(scene, fields, conv_manager_fids):
    reasons = []
    choices = []

    if fields['name_of_choice']:
        reasons.append(f"non-empty banner text: '{fields['name_of_choice']}'")
    if fields['localize']:
        reasons.append("Localize_Choice_Text=true (no mapping)")

    # ChoiceNode.Running() only ever iterates `for (x = 0; x < Number_Of_Choices; x++)` --
    # any button index at or beyond that bound is stale/unreachable data (e.g. left over
    # from when the node had more choices) and must never influence migration, even if it
    # still has a wired listener.
    num_choices = fields['number_of_choices']
    buttons = fields['buttons'] if num_choices is None else fields['buttons'][:num_choices]
    for i, button in enumerate(buttons):
        calls = button['calls']
        if not calls:
            continue  # unwired button: skipped, matches runtime behavior
        if len(calls) > 1:
            reasons.append(f"button {i}: multiple listeners ({len(calls)})")
            continue
        call = calls[0]
        method = call['method']
        tfid = call['target_fid']
        is_conv_mgr = tfid in conv_manager_fids
        if method == 'Start_Conversation_Partway_Through':
            reasons.append(f"button {i}: Start_Conversation_Partway_Through jump (no ShowChoiceNode equivalent)")
            continue
        if method != 'Start_Conversation' or not is_conv_mgr:
            ttype = (call['target_type'] or '').split(',')[0].split('.')[-1]
            reasons.append(f"button {i}: other action ({ttype}.{method})")
            continue
        has_req = i < len(fields['has_requirement']) and fields['has_requirement'][i]
        has_img = i < len(fields['has_image']) and fields['has_image'][i]
        if has_req:
            reasons.append(f"button {i}: stat requirement")
        if has_img:
            reasons.append(f"button {i}: custom button image")
        if has_req or has_img:
            continue
        text = fields['button_text'][i] if i < len(fields['button_text']) else ''
        choices.append({'text': text, 'target_fid': tfid})

    if reasons:
        return 'FLAGGED', reasons, []
    return 'MIGRATE', [], choices


def analyze_scene_for_migration(scene_file):
    class_guid_table = audit.build_class_guid_table()
    prefab_guid_table = audit.build_prefab_guid_table(class_guid_table)
    scene = audit.parse_scene(os.path.join(audit.SCENES_DIR, scene_file), class_guid_table, prefab_guid_table)

    transform_of_go, stripped_transform_prefab, go_conv_manager = audit.build_indexes(scene)
    conv_manager_fids = {fid for fid, mb in scene.monobehaviours.items()
                          if mb['class'] == audit.CONVERSATION_MANAGER}
    conversations_root = find_conversations_root(scene)

    def conv_name(fid):
        go = scene.monobehaviours[fid]['go']
        return scene.gameobjects.get(go, {}).get('name') or f"<ConversationManager {fid}>"

    rows = []
    for fid, mb in scene.monobehaviours.items():
        if mb['class'] != audit.CHOICE_NODE:
            continue
        owner = audit.entity_owner(scene, 'mb', fid, transform_of_go, stripped_transform_prefab, go_conv_manager)
        if owner is None:
            continue
        owner_go = scene.monobehaviours[owner]['go']
        if not is_descendant_of(scene, owner_go, conversations_root, transform_of_go, stripped_transform_prefab):
            continue  # out of scope: not under Conversations

        node_go = mb['go']
        fields = parse_choice_node_fields(scene.body(fid))
        verdict, reasons, choices = classify_choice_node(scene, fields, conv_manager_fids)
        path = hierarchy_path(scene, node_go, transform_of_go, stripped_transform_prefab, conversations_root)

        rows.append({
            'fid': fid,
            'line': scene.docs[fid]['start'] + 1,
            'owner': conv_name(owner),
            'node_name': scene.gameobjects.get(node_go, {}).get('name') or f"<GO {node_go}>",
            'path': path,
            'verdict': verdict,
            'reasons': reasons,
            'choices': [{'text': c['text'], 'target_name': conv_name(c['target_fid'])} for c in choices],
        })

    rows.sort(key=lambda r: r['line'])
    return scene, rows


def render_scene_report(scene_file, rows):
    name = os.path.splitext(scene_file)[0]
    migrate = [r for r in rows if r['verdict'] == 'MIGRATE']
    flagged = [r for r in rows if r['verdict'] == 'FLAGGED']

    lines = []
    lines.append(f"# {name} — Choice Node Migration\n")
    lines.append(f"Scope: `ChoiceNode` instances under the `{CONVERSATIONS_ROOT_NAME}` GameObject only.\n")
    lines.append("Generated by `Tools/audit_choice_migration.py`\n")
    lines.append("## Summary\n")
    lines.append(f"- In-scope ChoiceNode instances: {len(rows)}")
    lines.append(f"- **MIGRATE (safe to auto-convert): {len(migrate)}**")
    lines.append(f"- **FLAGGED (needs manual review): {len(flagged)}**")
    lines.append("")

    lines.append("## MIGRATE\n")
    if migrate:
        lines.append("| Owning conversation | Node | Line # | Resulting choices |")
        lines.append("|---|---|---|---|")
        for r in migrate:
            choice_str = '; '.join(f"\"{c['text']}\" → {c['target_name']}" for c in r['choices']) or '_(no wired buttons)_'
            lines.append(f"| {r['owner']} | {r['node_name']} | {r['line']} | {choice_str} |")
    else:
        lines.append("_None in this scene._")
    lines.append("")

    lines.append("## FLAGGED\n")
    if flagged:
        lines.append("| Owning conversation | Node | Line # | Reasons |")
        lines.append("|---|---|---|---|")
        for r in flagged:
            lines.append(f"| {r['owner']} | {r['node_name']} | {r['line']} | {'; '.join(r['reasons'])} |")
    else:
        lines.append("_None in this scene._")
    lines.append("")

    return '\n'.join(lines)


def render_index(all_results):
    lines = []
    lines.append("# ChoiceNode → ShowChoiceNode Migration — Dry Run\n")
    lines.append(f"Generated by `Tools/audit_choice_migration.py`. Scoped to `ChoiceNode` instances under the "
                 f"`{CONVERSATIONS_ROOT_NAME}` GameObject in each of the 9 location scenes — anything outside "
                 f"it is not reported.\n")
    lines.append("## Eligibility rules (MIGRATE vs FLAGGED)\n")
    lines.append(
        "A `ChoiceNode` is **MIGRATE** only if ALL of the following hold; otherwise it's **FLAGGED** with every "
        "applicable reason listed:\n"
        "- `Name_Of_Choice` (banner/prompt text) is blank — `ShowChoiceNode` has no banner field, so real text "
        "would be silently dropped.\n"
        "- `Localize_Choice_Text` is false.\n"
        "- Every wired button (has at least one listener) has **exactly one** persistent call — more than one "
        "listener on a button is flagged rather than guessing which to keep.\n"
        "- That one call targets a `ConversationManager` and calls exactly `Start_Conversation` (not "
        "`Start_Conversation_Partway_Through`, which has no `ShowChoiceNode` equivalent).\n"
        "- That button's `Has_Requirements` is `No_Requirement` and `choice_button_images` is null — checked "
        "only on wired buttons, matching what the game actually reads at runtime.\n"
        "- An unwired button (no listener) is simply skipped, matching `ChoiceNode`'s own behavior.\n\n"
        "Not checked as a blocker: `ShowChoiceNode` always shuffles choice order at runtime (no fixed-order "
        "option), unlike `ChoiceNode`'s default fixed order — accepted as an intentional behavior change for "
        "every migrated node. `Hide_Dialogue_UI` is not ported; migrated nodes keep `ShowChoiceNode`'s default "
        "(`hideDialogueUI = true`).\n"
    )
    lines.append("## Aggregate counts\n")
    lines.append("| Scene | In scope | MIGRATE | FLAGGED |")
    lines.append("|---|---|---|---|")
    total_scope = total_migrate = total_flagged = 0
    for scene_file, rows in all_results:
        n = os.path.splitext(scene_file)[0]
        migrate = sum(1 for r in rows if r['verdict'] == 'MIGRATE')
        flagged = sum(1 for r in rows if r['verdict'] == 'FLAGGED')
        total_scope += len(rows)
        total_migrate += migrate
        total_flagged += flagged
        link = n.replace(' ', '%20') + '.md'
        lines.append(f"| [{n}]({link}) | {len(rows)} | {migrate} | {flagged} |")
    lines.append(f"| **Total** | **{total_scope}** | **{total_migrate}** | **{total_flagged}** |")
    lines.append("")
    return '\n'.join(lines)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    all_results = []
    sidecar = []

    for scene_file in audit.SCENES:
        print(f"Parsing {scene_file} ...", file=sys.stderr)
        scene, rows = analyze_scene_for_migration(scene_file)
        all_results.append((scene_file, rows))
        report = render_scene_report(scene_file, rows)
        out_name = os.path.splitext(scene_file)[0] + '.md'
        with open(os.path.join(OUT_DIR, out_name), 'w') as f:
            f.write(report)
        for r in rows:
            if r['verdict'] == 'MIGRATE':
                sidecar.append({
                    'scene': scene_file, 'node_name': r['node_name'],
                    'hierarchy_path': r['path'], 'owner': r['owner'],
                })
        migrate_n = sum(1 for r in rows if r['verdict'] == 'MIGRATE')
        flagged_n = sum(1 for r in rows if r['verdict'] == 'FLAGGED')
        print(f"  {len(rows)} in scope, {migrate_n} MIGRATE, {flagged_n} FLAGGED", file=sys.stderr)

    index = render_index(all_results)
    with open(os.path.join(OUT_DIR, 'README.md'), 'w') as f:
        f.write(index)
    with open(os.path.join(OUT_DIR, 'migrate_fileids.json'), 'w') as f:
        json.dump(sidecar, f, indent=2)
    print("Done.", file=sys.stderr)


if __name__ == '__main__':
    main()
