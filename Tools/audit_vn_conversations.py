#!/usr/bin/env python3
"""
Audits the VNEngine conversation scenes in Assets/Scenes/Locations for:
  1. ConversationManager "conversations" that are unreachable from any known
     entry point (dead node-tree paths).
  2. Legacy ChoiceNode (raw UnityEvent Button_Events) usages, as opposed to
     the newer ShowChoiceNode.nextConversation jump field.

See Docs/VNEngine Audit/README.md for methodology and caveats.
"""
import os
import re
import sys
from collections import defaultdict, deque

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCENES_DIR = os.path.join(REPO, "Assets/Scenes/Locations")
OUT_DIR = os.path.join(REPO, "Docs/VNEngine Audit")

SCENES = [
    "Apartment.unity", "Dining Hall.unity", "Green.unity", "Lecture Hall.unity",
    "Library.unity", "Outside Class.unity", "Post Game.unity", "Shuttle.unity",
    "Student Center.unity",
]

EXTRA_CLASS_FILES = [
    "Assets/Scripts/ConversationManager.cs",
    "Assets/Scripts/ClassroomRouterNode.cs",
    "Assets/Scripts/CharacterStageRouterNode.cs",
    "Assets/Scripts/CharacterFootballRouterNode.cs",
    "Assets/Scripts/VNSceneManager.cs",
    "Assets/Scripts/GroupStudyManager.cs",
    "Assets/Scripts/ClickToStartConversation.cs",
    "Assets/Scripts/Extras/PressAnyKeyToStartConversation.cs",
    "Assets/Scripts/Mini Games/Study/FivePositionsGameManager.cs",
]

CONVERSATION_MANAGER = "ConversationManager"
ROUTER_TYPES = {"CharacterStageRouterNode", "ClassroomRouterNode", "CharacterFootballRouterNode"}
CHOICE_NODE = "ChoiceNode"
SHOW_CHOICE_NODE = "ShowChoiceNode"

# Non-Node MonoBehaviours whose ConversationManager-typed fields are external
# entry points (player/system triggered), not internal node-to-node jumps.
EXTERNAL_TRIGGER_FIELDS = {
    "ClickToStartConversation": ["conversation_to_start"],
    "PressAnyKeyToStartConversation": ["conversation_to_start"],
    "VNSceneManager": ["starting_conversation"],
    "GroupStudyManager": ["conversationManager"],
    "FivePositionsGameManager": ["conversationManager", "pendingEndConversation"],
}

DOC_RE = re.compile(r'^--- !u!(\d+) &(-?\d+)( stripped)?\s*$')
CLASS_NAME_RE = re.compile(r'\bclass\s+(\w+)\s*:')
GUID_LINE_RE = re.compile(r'^guid:\s*([0-9a-fA-F]+)')
SCRIPT_GUID_RE = re.compile(r'm_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-fA-F]+)')
FIELD_FILEID_RE = re.compile(
    r'^(\s*)([A-Za-z_][A-Za-z0-9_]*):\s*\{fileID:\s*(-?\d+)(?:,\s*guid:\s*([0-9a-fA-F]+))?[^}]*\}\s*$'
)
NAME_RE = re.compile(r'^\s*m_Name:\s*(.*)$')
FATHER_RE = re.compile(r'^\s*m_Father:\s*\{fileID:\s*(-?\d+)\}')
GAMEOBJECT_REF_RE = re.compile(r'^\s*m_GameObject:\s*\{fileID:\s*(-?\d+)\}')
COMPONENT_RE = re.compile(r'^\s*-\s*component:\s*\{fileID:\s*(-?\d+)\}')
PREFABINSTANCE_REF_RE = re.compile(r'^\s*m_PrefabInstance:\s*\{fileID:\s*(-?\d+)\}')
SOURCE_PREFAB_RE = re.compile(r'm_SourcePrefab:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-fA-F]+)')
TRANSFORM_PARENT_RE = re.compile(r'^\s*m_TransformParent:\s*\{fileID:\s*(-?\d+)\}')
MOD_TARGET_RE = re.compile(r'^\s*-\s*target:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-fA-F]+)')
PROP_PATH_RE = re.compile(r'^\s*propertyPath:\s*(.*)$')
OBJ_REF_RE = re.compile(r'^\s*objectReference:\s*\{fileID:\s*(-?\d+)(?:,\s*guid:\s*([0-9a-fA-F]+))?')
VALUE_RE = re.compile(r'^\s*value:\s*(.*)$')


# ---------------------------------------------------------------------------
# GUID -> class name lookup tables
# ---------------------------------------------------------------------------

def build_class_guid_table():
    table = {}
    files = []
    nodes_dir = os.path.join(REPO, "Assets/Scripts/Nodes")
    for fn in sorted(os.listdir(nodes_dir)):
        if fn.endswith(".cs"):
            files.append(os.path.join(nodes_dir, fn))
    for rel in EXTRA_CLASS_FILES:
        files.append(os.path.join(REPO, rel))

    for cs_path in files:
        meta_path = cs_path + ".meta"
        if not (os.path.exists(cs_path) and os.path.exists(meta_path)):
            continue
        guid = None
        with open(meta_path) as f:
            for line in f:
                m = GUID_LINE_RE.match(line)
                if m:
                    guid = m.group(1)
                    break
        if not guid:
            continue
        with open(cs_path, errors="replace") as f:
            content = f.read()
        m = CLASS_NAME_RE.search(content)
        if m:
            table[guid] = m.group(1)
    return table


def build_prefab_guid_table(class_guid_table):
    table = {}
    cp_dir = os.path.join(REPO, "Assets/Resources/Conversation Pieces")
    for fn in sorted(os.listdir(cp_dir)):
        if not fn.endswith(".prefab"):
            continue
        meta_path = os.path.join(cp_dir, fn + ".meta")
        prefab_path = os.path.join(cp_dir, fn)
        pguid = None
        if os.path.exists(meta_path):
            with open(meta_path) as f:
                for line in f:
                    m = GUID_LINE_RE.match(line)
                    if m:
                        pguid = m.group(1)
                        break
        if not pguid:
            continue
        with open(prefab_path, errors="replace") as f:
            content = f.read()
        classes = []
        for m in SCRIPT_GUID_RE.finditer(content):
            g = m.group(1)
            if g in class_guid_table:
                classes.append(class_guid_table[g])
        table[pguid] = classes
    return table


# ---------------------------------------------------------------------------
# Scene parsing
# ---------------------------------------------------------------------------

class Scene:
    def __init__(self, name, lines):
        self.name = name
        self.lines = lines
        self.docs = {}          # fid -> dict(type, start, end, stripped)
        self.gameobjects = {}   # fid -> {'name':.., 'components':[fid,...]}
        self.transforms = {}    # fid -> {'go':fid|None,'father':fid|None,'stripped':bool,'prefab_instance':fid|None}
        self.monobehaviours = {}  # fid -> {'go':fid|None,'guid':str|None,'class':str|None,'stripped':bool}
        self.prefab_instances = {}  # fid -> {'source_guid':.., 'classes':[...], 'parent':fid|None, 'modifications':[...], 'name_override':str|None}

    def body(self, fid):
        d = self.docs[fid]
        return self.lines[d['start']:d['end']]


def split_docs(scene):
    lines = scene.lines
    cur = None
    for i, line in enumerate(lines):
        m = DOC_RE.match(line.rstrip('\n'))
        if m:
            if cur:
                cur['end'] = i
                scene.docs[cur['fid']] = cur
            cur = {'type': m.group(1), 'fid': int(m.group(2)), 'start': i,
                   'stripped': bool(m.group(3))}
    if cur:
        cur['end'] = len(lines)
        scene.docs[cur['fid']] = cur


def parse_gameobjects(scene):
    for fid, d in scene.docs.items():
        if d['type'] != '1':
            continue
        body = scene.body(fid)
        name = None
        comps = []
        for line in body:
            nm = NAME_RE.match(line)
            if nm and name is None:
                name = nm.group(1)
            cm = COMPONENT_RE.match(line)
            if cm:
                comps.append(int(cm.group(1)))
        scene.gameobjects[fid] = {'name': name, 'components': comps}


def parse_transforms(scene):
    for fid, d in scene.docs.items():
        if d['type'] != '4':
            continue
        body = scene.body(fid)
        entry = {'go': None, 'father': None, 'stripped': d['stripped'], 'prefab_instance': None}
        for line in body:
            if entry['go'] is None:
                gm = GAMEOBJECT_REF_RE.match(line)
                if gm:
                    entry['go'] = int(gm.group(1))
            fm = FATHER_RE.match(line)
            if fm:
                entry['father'] = int(fm.group(1))
            if d['stripped']:
                pim = PREFABINSTANCE_REF_RE.match(line)
                if pim:
                    entry['prefab_instance'] = int(pim.group(1))
        scene.transforms[fid] = entry


def parse_monobehaviours(scene, class_guid_table):
    for fid, d in scene.docs.items():
        if d['type'] != '114':
            continue
        body = scene.body(fid)
        go = None
        guid = None
        for line in body:
            if go is None:
                gm = GAMEOBJECT_REF_RE.match(line)
                if gm:
                    go = int(gm.group(1))
            if guid is None:
                sm = SCRIPT_GUID_RE.search(line)
                if sm:
                    guid = sm.group(1)
            if go is not None and guid is not None:
                break
        cls = class_guid_table.get(guid) if guid else None
        scene.monobehaviours[fid] = {'go': go, 'guid': guid, 'class': cls, 'stripped': d['stripped']}


def parse_prefab_instances(scene, prefab_guid_table):
    for fid, d in scene.docs.items():
        if d['type'] != '1001':
            continue
        body = scene.body(fid)
        source_guid = None
        parent = None
        name_override = None
        modifications = []  # (propertyPath, value_str, obj_fid, obj_guid)
        pending_path = None
        pending_value = None
        for line in body:
            if source_guid is None:
                sm = SOURCE_PREFAB_RE.search(line)
                if sm:
                    source_guid = sm.group(1)
            if parent is None:
                tm = TRANSFORM_PARENT_RE.match(line)
                if tm:
                    parent = int(tm.group(1))
            pm = PROP_PATH_RE.match(line)
            if pm:
                pending_path = pm.group(1).strip()
                if len(pending_path) >= 2 and pending_path[0] == pending_path[-1] and pending_path[0] in ("'", '"'):
                    pending_path = pending_path[1:-1]  # Unity sometimes quotes paths like 'foo.Array.data[1]'
                pending_value = None
                continue
            vm = VALUE_RE.match(line)
            if vm and pending_path is not None:
                pending_value = vm.group(1).strip()
                if pending_path == 'm_Name' and pending_value:
                    name_override = pending_value
                continue
            om = OBJ_REF_RE.match(line)
            if om and pending_path is not None:
                obj_fid = int(om.group(1))
                obj_guid = om.group(2)
                modifications.append((pending_path, pending_value, obj_fid, obj_guid))
                pending_path = None
                pending_value = None
        classes = prefab_guid_table.get(source_guid, []) if source_guid else []
        scene.prefab_instances[fid] = {
            'source_guid': source_guid, 'classes': classes, 'parent': parent,
            'modifications': drop_stale_array_overrides(modifications), 'name_override': name_override,
        }


ARRAY_SIZE_PATH_RE = re.compile(r'^(.+)\.Array\.size$')
ARRAY_DATA_PATH_RE = re.compile(r'^(.+)\.Array\.data\[(\d+)\]')


def drop_stale_array_overrides(modifications):
    """When a PrefabInstance array field is shrunk in the Inspector, Unity can leave
    per-element override entries for indices beyond the new (smaller) declared size
    still sitting in m_Modifications -- those are never applied at load time. Drop
    any data[N] entry whose field has a known size and N >= size, so e.g. a
    CharacterStageRouterNode.routes override with size=3 but data[0..7] present only
    yields the 3 routes actually live at runtime."""
    sizes = {}
    for path, value, obj_fid, obj_guid in modifications:
        m = ARRAY_SIZE_PATH_RE.match(path)
        if m:
            try:
                sizes[m.group(1)] = int(value)
            except (TypeError, ValueError):
                pass
    filtered = []
    for entry in modifications:
        path = entry[0]
        m = ARRAY_DATA_PATH_RE.match(path)
        if m:
            field, idx = m.group(1), int(m.group(2))
            if field in sizes and idx >= sizes[field]:
                continue
        filtered.append(entry)
    return filtered


def parse_scene(path, class_guid_table, prefab_guid_table):
    with open(path, errors='replace') as f:
        lines = f.readlines()
    scene = Scene(os.path.basename(path), lines)
    split_docs(scene)
    parse_gameobjects(scene)
    parse_transforms(scene)
    parse_monobehaviours(scene, class_guid_table)
    parse_prefab_instances(scene, prefab_guid_table)
    return scene


# ---------------------------------------------------------------------------
# Ownership (which conversation a node belongs to) + reachability graph
# ---------------------------------------------------------------------------

def display_name(scene, entity_kind, fid):
    if entity_kind == 'mb':
        go = scene.monobehaviours[fid]['go']
        if go is not None and go in scene.gameobjects:
            return scene.gameobjects[go]['name'] or f"<unnamed GO {go}>"
        return f"<MonoBehaviour {fid}>"
    else:
        pi = scene.prefab_instances[fid]
        if pi['name_override']:
            return pi['name_override']
        classes = pi['classes']
        return (classes[0] if classes else pi['source_guid']) or f"<PrefabInstance {fid}>"


def build_indexes(scene):
    """Build lookup structures needed for upward ownership walks."""
    transform_of_go = {}
    for go, data in scene.gameobjects.items():
        for c in data['components']:
            if c in scene.transforms:
                transform_of_go[go] = c
                break

    stripped_transform_prefab = {}
    for fid, t in scene.transforms.items():
        if t['stripped'] and t['prefab_instance'] is not None:
            stripped_transform_prefab[fid] = t['prefab_instance']

    go_conv_manager = {}
    for fid, mb in scene.monobehaviours.items():
        if mb['class'] == CONVERSATION_MANAGER and mb['go'] is not None:
            go_conv_manager[mb['go']] = fid

    return transform_of_go, stripped_transform_prefab, go_conv_manager


def owning_conversation(scene, start_fid, transform_of_go, stripped_transform_prefab, go_conv_manager):
    """Given a transform-or-prefabinstance-parent fid, walk up to find the owning ConversationManager fid."""
    cur = start_fid
    visited = set()
    while cur is not None and cur not in visited and cur != 0:
        visited.add(cur)
        if cur in scene.transforms:
            t = scene.transforms[cur]
            if not t['stripped'] and t['go'] is not None and t['go'] in go_conv_manager:
                return go_conv_manager[t['go']]
            cur = t['father']
            continue
        if cur in stripped_transform_prefab:
            pi_fid = stripped_transform_prefab[cur]
            cur = scene.prefab_instances[pi_fid]['parent']
            continue
        break
    return None


def entity_owner(scene, kind, fid, transform_of_go, stripped_transform_prefab, go_conv_manager):
    if kind == 'mb':
        go = scene.monobehaviours[fid]['go']
        start = transform_of_go.get(go)
    else:
        start = scene.prefab_instances[fid]['parent']
    if start is None:
        return None
    return owning_conversation(scene, start, transform_of_go, stripped_transform_prefab, go_conv_manager)


PERSISTENT_CALL_PATH_RE = re.compile(
    r'^(.*)\.m_PersistentCalls\.m_Calls\.Array\.data\[(\d+)\]\.(.+)$'
)
INLINE_PERSISTENT_CALL_RE = re.compile(
    r'm_Target:\s*\{fileID:\s*(-?\d+)[^}]*\}\s*\n\s*m_TargetAssemblyTypeName:\s*([^\n]*)\n\s*m_MethodName:\s*(\S*)'
)
CONV_JUMP_METHODS = {"Start_Conversation", "Start_Conversation_Partway_Through"}


def extract_button_calls_from_prefab_modifications(modifications):
    """Group any UnityEvent persistent-call overrides (Button_Events on a ChoiceNode,
    or a plain Button's m_OnClick, etc.) by (field-array prefix, call index)."""
    groups = defaultdict(dict)
    for path, value, obj_fid, obj_guid in modifications:
        m = PERSISTENT_CALL_PATH_RE.match(path)
        if not m:
            continue
        prefix, call, suffix = m.group(1), int(m.group(2)), m.group(3)
        groups[(prefix, call)][suffix] = (value, obj_fid, obj_guid)
    calls = []
    for (prefix, call), fields in groups.items():
        method = fields.get('m_MethodName', (None, None, None))[0]
        target_type = fields.get('m_TargetAssemblyTypeName', (None, None, None))[0]
        target_fid = fields.get('m_Target', (None, None, None))[1]
        calls.append({'prefix': prefix, 'call': call, 'method': method,
                       'target_type': target_type, 'target_fid': target_fid})
    return calls


def extract_button_calls_from_inline_body(body_text):
    calls = []
    for m in INLINE_PERSISTENT_CALL_RE.finditer(body_text):
        target_fid = int(m.group(1))
        target_type = m.group(2).strip()
        method = m.group(3).strip()
        calls.append({'button': None, 'call': None, 'method': method,
                      'target_type': target_type, 'target_fid': target_fid})
    return calls


def analyze_scene(scene):
    transform_of_go, stripped_transform_prefab, go_conv_manager = build_indexes(scene)

    conv_manager_fids = {fid for fid, mb in scene.monobehaviours.items()
                          if mb['class'] == CONVERSATION_MANAGER}

    def conv_name(fid):
        go = scene.monobehaviours[fid]['go']
        n = scene.gameobjects.get(go, {}).get('name') if go is not None else None
        return n or f"<ConversationManager {fid}>"

    def conv_line(fid):
        return scene.docs[fid]['start'] + 1

    edges = defaultdict(set)   # conv_fid -> set of conv_fid (reachable jumps), for reporting we also keep labels
    edge_labels = defaultdict(list)  # (src,dst) -> [label,...]
    roots = set()
    root_reasons = defaultdict(list)
    choice_node_calls = {}  # ('mb'|'pi', fid) -> [call,...], for the legacy ChoiceNode report

    # --- Generic field-level scan over every MonoBehaviour and PrefabInstance ---
    # (jump fields that hold a plain ConversationManager reference directly, e.g.
    # nextConversation, fallbackConversation, conversation_to_start, routes[].conversation)
    for fid, mb in scene.monobehaviours.items():
        cls = mb['class']
        body = scene.body(fid)

        if cls == CHOICE_NODE:
            continue  # ChoiceNode's own fields are legacy choice-text arrays, not jump fields
                      # (its Start_Conversation button wiring is handled in the persistent-call pass below)

        if cls is not None:
            owner = None
            is_external = cls in EXTERNAL_TRIGGER_FIELDS
            for line in body:
                fm = FIELD_FILEID_RE.match(line)
                if not fm:
                    continue
                key, tfid, guid = fm.group(2), int(fm.group(3)), fm.group(4)
                if guid:  # points to an external asset, not a scene object
                    continue
                if key == 'm_Target':  # UnityEvent internals, handled in the persistent-call pass below
                    continue
                if tfid not in conv_manager_fids:
                    continue
                if is_external:
                    roots.add(tfid)
                    root_reasons[tfid].append(f"{cls}.{key}")
                    continue
                if cls == CONVERSATION_MANAGER and key == 'start_conversation_when_done':
                    edges[fid].add(tfid)
                    edge_labels[(fid, tfid)].append('start_conversation_when_done')
                    continue
                if cls != CONVERSATION_MANAGER:
                    if owner is None:
                        owner = entity_owner(scene, 'mb', fid, transform_of_go, stripped_transform_prefab, go_conv_manager)
                    if owner is not None:
                        edges[owner].add(tfid)
                        edge_labels[(owner, tfid)].append(f"{cls}.{key}")

    for fid, pi in scene.prefab_instances.items():
        classes = pi['classes']
        if CHOICE_NODE in classes:
            continue  # handled in the persistent-call pass below

        owner = None
        for path, value, obj_fid, obj_guid in pi['modifications']:
            if obj_guid:  # external asset reference
                continue
            if obj_fid not in conv_manager_fids:
                continue
            leaf = path.rsplit('.', 1)[-1]
            leaf = re.sub(r'\[\d+\]$', '', leaf)
            if leaf in ('m_Target',):
                continue
            if owner is None:
                owner = entity_owner(scene, 'pi', fid, transform_of_go, stripped_transform_prefab, go_conv_manager)
            if owner is not None:
                label = classes[0] if classes else (pi['source_guid'] or '?')
                edges[owner].add(obj_fid)
                edge_labels[(owner, obj_fid)].append(f"{label}.{leaf}")

    # --- Persistent-call (UnityEvent) scan, run over EVERY component, not just ChoiceNode ---
    # Confirmed empirically: in these scenes, plain UI Buttons (NPC icons, map elements) wire
    # their onClick directly to ConversationManager.Start_Conversation, exactly like a legacy
    # ChoiceNode's Button_Events does. A call whose source sits inside some conversation's node
    # tree (e.g. a ChoiceNode) is an internal jump edge; a call from anything else (no owning
    # conversation found) is treated as an external entry point (root).
    for fid, mb in scene.monobehaviours.items():
        calls = extract_button_calls_from_inline_body(''.join(scene.body(fid)))
        if not calls:
            continue
        if mb['class'] == CHOICE_NODE:
            choice_node_calls[('mb', fid)] = calls
        owner = entity_owner(scene, 'mb', fid, transform_of_go, stripped_transform_prefab, go_conv_manager)
        for call in calls:
            tfid = call['target_fid']
            if call['method'] not in CONV_JUMP_METHODS or tfid not in conv_manager_fids:
                continue
            if owner is not None:
                edges[owner].add(tfid)
                edge_labels[(owner, tfid)].append(f"button -> {call['method']}")
            else:
                roots.add(tfid)
                root_reasons[tfid].append(f"external button (fileID {fid}) -> {call['method']}")

    for fid, pi in scene.prefab_instances.items():
        calls = extract_button_calls_from_prefab_modifications(pi['modifications'])
        if not calls:
            continue
        if CHOICE_NODE in pi['classes']:
            choice_node_calls[('pi', fid)] = calls
        owner = entity_owner(scene, 'pi', fid, transform_of_go, stripped_transform_prefab, go_conv_manager)
        for call in calls:
            tfid = call['target_fid']
            if call['method'] not in CONV_JUMP_METHODS or tfid is None or tfid not in conv_manager_fids:
                continue
            if owner is not None:
                edges[owner].add(tfid)
                edge_labels[(owner, tfid)].append(f"button -> {call['method']}")
            else:
                roots.add(tfid)
                root_reasons[tfid].append(f"external button (fileID {fid}) -> {call['method']}")

    # --- Roots: literal "Start" ConversationManagers ---
    for fid in conv_manager_fids:
        go = scene.monobehaviours[fid]['go']
        name = scene.gameobjects.get(go, {}).get('name') if go is not None else None
        if name == 'Start':
            roots.add(fid)
            root_reasons[fid].append('GameObject named "Start"')

    # --- Reachability ---
    reachable = set()
    q = deque(roots)
    reachable.update(roots)
    while q:
        cur = q.popleft()
        for nxt in edges.get(cur, ()):
            if nxt not in reachable:
                reachable.add(nxt)
                q.append(nxt)

    unreachable = sorted(conv_manager_fids - reachable, key=lambda f: conv_line(f))

    # --- Cross-file whole-conversation-prefab self-check ---
    whole_conv_prefab_guids = set()
    for fid, pi in scene.prefab_instances.items():
        if CONVERSATION_MANAGER in pi['classes']:
            whole_conv_prefab_guids.add(pi['source_guid'])

    # --- Legacy ChoiceNode report data ---
    legacy_nodes = []

    def resolve_conv_target(tfid):
        if tfid in conv_manager_fids:
            return conv_name(tfid)
        return None

    for fid, mb in scene.monobehaviours.items():
        if mb['class'] != CHOICE_NODE:
            continue
        owner = entity_owner(scene, 'mb', fid, transform_of_go, stripped_transform_prefab, go_conv_manager)
        owner_name = conv_name(owner) if owner is not None else '<unknown>'
        node_name = display_name(scene, 'mb', fid)
        calls = extract_button_calls_from_inline_body(''.join(scene.body(fid)))
        legacy_nodes.append({
            'owner': owner_name, 'node': node_name, 'line': scene.docs[fid]['start'] + 1,
            'calls': [{'method': c['method'], 'target_type': c['target_type'],
                       'target_name': resolve_conv_target(c['target_fid'])} for c in calls],
        })

    for fid, pi in scene.prefab_instances.items():
        if CHOICE_NODE not in pi['classes']:
            continue
        owner = entity_owner(scene, 'pi', fid, transform_of_go, stripped_transform_prefab, go_conv_manager)
        owner_name = conv_name(owner) if owner is not None else '<unknown>'
        node_name = display_name(scene, 'pi', fid)
        calls = extract_button_calls_from_prefab_modifications(pi['modifications'])
        legacy_nodes.append({
            'owner': owner_name, 'node': node_name, 'line': scene.docs[fid]['start'] + 1,
            'calls': [{'method': c['method'], 'target_type': c['target_type'],
                       'target_name': resolve_conv_target(c['target_fid'])} for c in calls],
        })
    legacy_nodes.sort(key=lambda n: n['line'])

    return {
        'conv_manager_fids': conv_manager_fids,
        'conv_name': conv_name,
        'conv_line': conv_line,
        'edges': edges,
        'edge_labels': edge_labels,
        'roots': roots,
        'root_reasons': root_reasons,
        'reachable': reachable,
        'unreachable': unreachable,
        'whole_conv_prefab_guids': whole_conv_prefab_guids,
        'legacy_nodes': legacy_nodes,
    }


# ---------------------------------------------------------------------------
# Report rendering
# ---------------------------------------------------------------------------

def render_scene_report(scene_path, scene, result):
    lines = []
    name = os.path.splitext(scene.name)[0]
    total = len(result['conv_manager_fids'])
    unreach = result['unreachable']
    legacy = result['legacy_nodes']
    total_calls = sum(len(n['calls']) for n in legacy)
    resolved_calls = sum(1 for n in legacy for c in n['calls'] if c['target_name'])
    other_calls = sum(1 for n in legacy for c in n['calls']
                       if c['method'] not in CONV_JUMP_METHODS and c['method'])

    lines.append(f"# {name} — Conversation Audit\n")
    lines.append(f"Scene file: `Assets/Scenes/Locations/{scene.name}` ({len(scene.lines)} lines)\n")
    lines.append("Generated by `Tools/audit_vn_conversations.py`\n")
    lines.append("## Summary\n")
    lines.append(f"- Total ConversationManager instances: {total}")
    lines.append(f"- Reachable from a known entry point: {total - len(unreach)}")
    lines.append(f"- **Unreachable (flagged for review): {len(unreach)}**")
    lines.append(f"- Legacy ChoiceNode instances: {len(legacy)} ({total_calls} wired buttons; "
                 f"{resolved_calls} resolve to a conversation jump; {other_calls} trigger a non-standard action)")
    if result['whole_conv_prefab_guids']:
        lines.append(f"- ⚠️ Cross-file prefab warning: scene instantiates a prefab that itself contains a "
                     f"ConversationManager (guid(s): {', '.join(result['whole_conv_prefab_guids'])}) — "
                     f"reachability analysis does not follow node trees into external prefabs; treat conversations "
                     f"reached only through it as unverified.")
    else:
        lines.append("- Cross-file prefab warning: none (no whole-conversation prefab instantiated in this scene)")
    lines.append("")

    lines.append("## Unreachable Conversations\n")
    lines.append("_Flagged by static analysis only — verify before deleting (see README caveats, e.g. "
                 "save/resume-by-name). \"Entry points\" = conversations named `Start`, or targeted by "
                 "ClickToStartConversation / PressAnyKeyToStartConversation / VNSceneManager / GroupStudyManager / "
                 "FivePositionsGameManager._\n")
    if unreach:
        lines.append("| Conversation name | fileID | Line # | Outgoing references (if any) |")
        lines.append("|---|---|---|---|")
        for fid in unreach:
            outs = sorted(
                f"{result['conv_name'](t)} ({label})"
                for t in result['edges'].get(fid, ())
                for label in result['edge_labels'][(fid, t)]
            )
            out_str = '; '.join(outs) if outs else '—'
            lines.append(f"| {result['conv_name'](fid)} | {fid} | {result['conv_line'](fid)} | {out_str} |")
    else:
        lines.append("_None — every conversation in this scene is reachable from a known entry point._")
    lines.append("")

    lines.append("## Legacy ChoiceNode Usages\n")
    lines.append("_`ChoiceNode` (raw `Button_Events` UnityEvent array) instances, as opposed to the newer "
                 "`ShowChoiceNode.nextConversation` field. Candidates for migration._\n")
    if legacy:
        lines.append("| Owning conversation | ChoiceNode | Line # | Button action | Resolved target |")
        lines.append("|---|---|---|---|---|")
        for n in legacy:
            if not n['calls']:
                lines.append(f"| {n['owner']} | {n['node']} | {n['line']} | _(no wired buttons found)_ | — |")
                continue
            for c in n['calls']:
                method = c['method'] or '(empty)'
                ttype = (c['target_type'] or '').split(',')[0].split('.')[-1]
                action = f"{ttype}.{method}" if ttype else method
                target = c['target_name'] or '—'
                lines.append(f"| {n['owner']} | {n['node']} | {n['line']} | {action} | {target} |")
    else:
        lines.append("_None found in this scene._")
    lines.append("")

    return '\n'.join(lines)


def render_index(all_results):
    lines = []
    lines.append("# VNEngine Conversation Audit — Scenes/Locations\n")
    lines.append("Generated by `Tools/audit_vn_conversations.py`. Audits the 9 VNEngine conversation scenes "
                 "under `Assets/Scenes/Locations/` (Home.unity excluded — no VNEngine content). "
                 "Archive/, Temporary/, and Prefabs/Templates/ are out of scope.\n")
    lines.append("## Methodology\n")
    lines.append(
        "- Each scene's `ConversationManager` instances form a per-scene directed graph: a scene-local "
        "fileID reference from one conversation's nodes (or from `ConversationManager.start_conversation_when_done`) "
        "to another `ConversationManager` is an edge; a reference from a non-node external trigger "
        "(`ClickToStartConversation`, `PressAnyKeyToStartConversation`, `VNSceneManager`, `GroupStudyManager`, "
        "`FivePositionsGameManager`) marks the target as a root.\n"
        "- Every scene's `ConversationManager` GameObject(s) literally named `Start` are also treated as roots "
        "(confirmed: these are scene-establishing intro conversations, usually chaining into a `Conversation Router` "
        "/ `Classroom Router` / `Football Character Router` node that then branches based on game state).\n"
        "- A conversation not reachable from the root set via this graph is flagged **unreachable**.\n"
        "- `ChoiceNode` instances (legacy: raw `Button_Events` UnityEvent array) are listed separately from "
        "`ShowChoiceNode` (current: clean `nextConversation` field per choice), with each wired button's target "
        "resolved where possible.\n"
    )
    lines.append("## Caveats\n")
    lines.append(
        "- **\"Unreachable\" means unreachable via static analysis, not provably dead.** "
        "`Assets/Scripts/SaveLoad/SaveFile.cs` resumes a conversation from a save file via `GameObject.Find(name)` "
        "by name — this can only resume a conversation a player already reached once, so it doesn't rescue a "
        "truly dead conversation, but a flagged conversation should still be opened in the Unity Editor and "
        "confirmed before deletion.\n"
        "- This audit assumes no whole-conversation prefab (a prefab asset itself containing a `ConversationManager`) "
        "is instantiated inside any of these 9 scenes, which keeps the graph analysis scoped per-scene-file. "
        "The script checks this on every run per scene (see each scene's \"Cross-file prefab warning\" line) rather "
        "than assuming it forever.\n"
        "- Type is always resolved via script/prefab GUID, never by GameObject or prefab name — several "
        "`Conversation Pieces` prefabs have misleading names (e.g. `Show Choices.prefab` is actually the legacy "
        "`ChoiceNode`, while `Simple Show Choices.prefab` is the current `ShowChoiceNode`).\n"
        "- A handful of `Button_Events` entries trigger something other than starting a conversation (e.g. "
        "`Finish_Conversation`, `SetActive`, `Save`, `LoadScene`, or an indirect `Run_Node`/`StartNewConversation` "
        "call whose real jump field is captured separately) — these are reported verbatim in the Legacy "
        "ChoiceNode tables but are not treated as reachability edges.\n"
        "- **Stale prefab-instance array overrides are discarded.** When a router's `routes` or "
        "`fallbackConversations` list is shrunk in the Inspector (fewer entries than before), Unity can leave the "
        "old per-element `m_Modifications` overrides for the now-out-of-range indices sitting in the scene YAML "
        "even though they're never applied at load time. The script reads each array's own `...Array.size` override "
        "and drops any `data[N]` entry with `N >= size` before building edges, so a route/fallback pointing past the "
        "live array length is correctly treated as not actually wired up. This is also worth a look in the Unity "
        "Editor on its own — it means some routers carry inspector-visible-looking entries that are silently "
        "dead weight in the serialized data.\n"
    )
    lines.append("## Aggregate counts\n")
    lines.append("| Scene | ConversationManagers | Unreachable | Legacy ChoiceNode instances |")
    lines.append("|---|---|---|---|")
    total_cm = total_unreach = total_legacy = 0
    for scene_name, result in all_results:
        n = os.path.splitext(scene_name)[0]
        cm = len(result['conv_manager_fids'])
        un = len(result['unreachable'])
        lg = len(result['legacy_nodes'])
        total_cm += cm
        total_unreach += un
        total_legacy += lg
        link = n.replace(' ', '%20') + '.md'
        lines.append(f"| [{n}]({link}) | {cm} | {un} | {lg} |")
    lines.append(f"| **Total** | **{total_cm}** | **{total_unreach}** | **{total_legacy}** |")
    lines.append("")
    return '\n'.join(lines)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    class_guid_table = build_class_guid_table()
    prefab_guid_table = build_prefab_guid_table(class_guid_table)

    all_results = []
    for scene_file in SCENES:
        path = os.path.join(SCENES_DIR, scene_file)
        print(f"Parsing {scene_file} ...", file=sys.stderr)
        scene = parse_scene(path, class_guid_table, prefab_guid_table)
        result = analyze_scene(scene)
        all_results.append((scene_file, result))
        report = render_scene_report(path, scene, result)
        out_name = os.path.splitext(scene_file)[0] + '.md'
        with open(os.path.join(OUT_DIR, out_name), 'w') as f:
            f.write(report)
        print(f"  {len(result['conv_manager_fids'])} conversations, "
              f"{len(result['unreachable'])} unreachable, "
              f"{len(result['legacy_nodes'])} legacy ChoiceNode(s)", file=sys.stderr)

    index = render_index(all_results)
    with open(os.path.join(OUT_DIR, 'README.md'), 'w') as f:
        f.write(index)
    print("Done.", file=sys.stderr)


if __name__ == '__main__':
    main()
