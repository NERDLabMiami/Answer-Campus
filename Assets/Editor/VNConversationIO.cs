// VNConversationIO.cs
// Editor utility: clean JSON export + patch/match import for VNEngine conversations.

#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
// --- Minimal JSON writer that skips nulls and empty lists/dicts (editor-only) ---
static class MiniJson
{
    public static string Write(object obj, int indent = 0)
    {
        var sb = new System.Text.StringBuilder();
        WriteValue(sb, obj, indent, true);
        return sb.ToString();
    }

    static void WriteValue(System.Text.StringBuilder sb, object v, int indent, bool topLevel)
    {
        switch (v)
        {
            case null:
                sb.Append("null");
                return;
            case string s:
                sb.Append('"').Append(Escape(s)).Append('"');
                return;
            case bool b:
                sb.Append(b ? "true" : "false");
                return;
            case float f:
                sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            case double d:
                sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            case int i:
                sb.Append(i);
                return;
            case long l:
                sb.Append(l);
                return;
            case IEnumerable<string> sa:
                {
                    var list = sa.ToList();
                    if (list.Count == 0) { sb.Append("[]"); return; }
                    sb.Append('[');
                    for (int x = 0; x < list.Count; x++)
                    {
                        if (x > 0) sb.Append(", ");
                        sb.Append('"').Append(Escape(list[x] ?? "")).Append('"');
                    }
                    sb.Append(']');
                    return;
                }
            case IEnumerable<object> oa:
                {
                    var list = oa.ToList();
                    if (list.Count == 0) { sb.Append("[]"); return; }
                    sb.Append('[');
                    for (int x = 0; x < list.Count; x++)
                    {
                        if (x > 0) sb.Append(", ");
                        WriteValue(sb, list[x], indent + 2, false);
                    }
                    sb.Append(']');
                    return;
                }
            case Dictionary<string, object> dict:
                {
                    // Skip empties at top-level values
                    var nonNull = dict.Where(kv => !IsNullish(kv.Value)).ToList();
                    sb.Append('{');
                    if (nonNull.Count > 0)
                    {
                        int i2 = 0;
                        foreach (var kv in nonNull)
                        {
                            if (i2++ > 0) sb.Append(", ");
                            sb.Append('"').Append(Escape(kv.Key)).Append("\": ");
                            WriteValue(sb, kv.Value, indent + 2, false);
                        }
                    }
                    sb.Append('}');
                    return;
                }
            default:
                // numbers or other simple types
                if (v is float || v is double || v is int || v is long) { WriteValue(sb, v.ToString(), indent, topLevel); return; }
                // fallback: ToString as string
                sb.Append('"').Append(Escape(v.ToString())).Append('"');
                return;
        }
    }

    static string Escape(string s) => s
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t");

    static bool IsNullish(object v)
    {
        if (v == null) return true;
        if (v is string s) return false; // keep empty strings as-is (human-editable)
        if (v is IList<object> list) return list.Count == 0;
        if (v is IEnumerable<string> sa) return !sa.Any();
        if (v is Dictionary<string, object> d) return d.Count == 0;
        return false;
    }
    
}

namespace VNEngine.EditorTools
{
    // ---- Stable Node ID ------------------------------------------------------
    [DisallowMultipleComponent]
    public class NodeExportTag : MonoBehaviour
    {
        [SerializeField] private string _id;
        public string Id => _id;

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(_id))
                _id = GUID.Generate().ToString();
        }

        [ContextMenu("Regenerate Export ID (Danger)")]
        private void Regenerate() => _id = GUID.Generate().ToString();
    }

    // ==== LEGACY STRUCTS (back-compat import) =================================
    [Serializable] public class ExportedConversation
    {
        public string conversationName;
        public List<ExportedNode> nodes = new List<ExportedNode>();
        public string exportedAt;
        public string exporterVersion = "1.0";
    }
    [Serializable] public class ExportedNode
    {
        public string id;
        public string type;
        public string goName;
        public int order;
        public List<FieldEntry> fields = new List<FieldEntry>();
    }

    // FieldEntry shape is defined by VNEngine; mirror it exactly:
    [Serializable] public class FieldEntry
    {
        public string name;
        public string kind;        // Primitive, Enum, String, Asset, Array, Other
        public string type;        // AssemblyQualifiedName
        public string value;
        public string assetGuid;
        public string assetType;   // AssemblyQualifiedName for asset type
        public string[] stringArray;
    }

    // ==== CLEAN STRUCTS (new export/import) ===================================
    [Serializable] class ConversationExport
    {
        public string conversationName;
        public string exportedAt;
        public List<NodeRecord> nodes = new();
        public string exporterVersion = "clean-1.1";
    }

    [Serializable] class NodeRecord
    {
        public string id;
        public string type;
        public string goName;
        public int order;

        // One of these will be non-null, depending on `type`
        public DialoguePayload dialogue;
        public ChoicePayload choice;
        public ChangeActorImagePayload changeActorImage;
        public SetBackgroundPayload setBackground;
        public SetBackgroundTransparentPayload setBackgroundTransparent;
        public EnterActorPayload enterActor;
        public IfNodePayload ifNode;
    }

    // ---- Payloads ------------------------------------------------------------
    [Serializable] class IfPayload
    {
        [Serializable] public class ConditionItem
        {
            public string condition;        // enum name of VNEngine.Condition
            public string statName;         // Stat_Name[i]
            public bool   boolValue;        // Bool_Compare_Value[i]
            public float  floatValue;       // Float_Compare_Value[i]
            public string stringValue;      // String_Compare_Value[i]
            public string stringIs;         // enum name VNEngine.Result
            public string floatCompare;     // enum name Float_Stat_Comparator
            public string nullObjectName;   // Check_Null_Object[i]?.name (for human readability)
            public string activeObjectName; // Check_Active_Object[i]?.name
            public string logicAfter;       // enum name VNEngine.Boolean_Logic that follows this condition (empty on last)
        }

        public string expect;               // enum name VNEngine.Condition_Is
        public string action;               // enum name VNEngine.Requirement_Met_Action
        public string conversationName;     // Conversation_To_Switch_To?.name
        public string nodeName;            // Node_To_Switch_To?.name
        public bool   continueConversation; // Continue_Conversation
        public List<ConditionItem> conditions = new();
    }
// ---- Payloads ------------------------------------------------------------
// (keep your existing payload classes here)

    [Serializable] class IfNodePayload
    {
        public string conditionIs;              // Condition_Is enum name
        public string action;                   // Requirement_Met_Action enum name
        public bool continueConversation;

        public string conversationName;         // Conversation_To_Switch_To?.name (for Change_Conversation)
        public string nodeToSwitchToName;       // Node_To_Switch_To?.name (for Jump_to_Middle_of_Conversation)

        public List<IfCond> conditions = new(); // one entry per index 0..Number_Of_Conditions-1
    }

    [Serializable] class IfCond
    {
        public string kind;         // Condition enum name
        public string statName;     // Stat_Name[i]

        public bool   boolValue;    // Bool_Compare_Value[i]
        public float  floatValue;   // Float_Compare_Value[i]
        public string stringValue;  // String_Compare_Value[i]

        public string stringIs;     // Result enum name (Is / Is_Not)
        public string floatCompare; // Float_Stat_Comparator enum name (if used)

        public string nullObject;   // Check_Null_Object[i]?.name
        public string activeObject; // Check_Active_Object[i]?.name

        public string logicBefore;  // Boolean_Logic enum name connecting (i-1) -> i (for i>0)
    }

    [Serializable] class ChoicePayload
    {
        public bool localize;
        public List<ChoiceItem> choices = new();
    }
    [Serializable] class ChoiceItem
    {
        public string id;            // nodeGuid#index
        public string key;           // Button_Text[i]
        public string text;          // resolved or literal
        public string disabledText;  // Disabled_Text[i]
    }

    [Serializable] class DialoguePayload
    {
        public string actorKey;
        public string actorKeySource;   // Dialogue_Source enum name
        public string speakerTitle;     // textbox_title
        public string dialogueKey;      // localized_key (if used)
        public string dialogueKeySource;// dialogue_from enum name
        public string text;             // literal or resolved
        public bool bringToFront;
        public bool darkenOthers;
    }

    [Serializable] class ChangeActorImagePayload
    {
        public string actorName;
        public string imageGuid;
        public string imageFile; // file name (no extension)
        public bool fadeIn;
        public bool lightenActor;
        public bool bringToFront;
    }

    [Serializable] class SetBackgroundPayload
    {
        public string imageGuid;
        public string imageFile;
        public bool setForeground;
        public bool fadeIn;
        public bool fadeOut;
    }

    [Serializable] class SetBackgroundTransparentPayload
    {
        public bool fadeIn;
        public bool fadeOut;
    }

    [Serializable] class EnterActorPayload
    {
        public string actorName;
        public string actorNameSource; // Dialogue_Source enum name
        public string entranceType;    // Entrance_Type enum name
        public float fadeInTime;
        public string destination;     // Actor_Positions enum name
    }

    // ==== WINDOW ==============================================================
    public class VNConversationIOWindow : EditorWindow
    {
        [MenuItem("Pipeline/Writing/Conversation IO")]
        private static void Open() => GetWindow<VNConversationIOWindow>(true, "Conversation IO");

        [SerializeField] private GameObject conversationRoot;
        [SerializeField] private TextAsset jsonToImport;
        [SerializeField] private bool removeMissingOnImport = false;

        Vector2 _scroll;

   // 2) Update OnGUI() to show the new button when a set root is selected
void OnGUI()
{
    _scroll = EditorGUILayout.BeginScrollView(_scroll);
    EditorGUILayout.Space();
    EditorGUILayout.LabelField("VNEngine Conversation IO", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
        "Select either:\n• A single Conversation root (has ConversationManager + child Nodes), or\n• A parent whose direct children are ConversationManager objects.",
        MessageType.Info);

    conversationRoot =
        (GameObject)EditorGUILayout.ObjectField("Selected Root", conversationRoot, typeof(GameObject), true);
    removeMissingOnImport = EditorGUILayout.ToggleLeft("Remove scene nodes not present in JSON (destructive)", removeMissingOnImport);

    bool isSingle = IsValidConversationRoot(conversationRoot);
    bool isSet    = IsValidConversationSetRoot(conversationRoot);

    using (new EditorGUI.DisabledScope(!(isSingle || isSet)))
    {
        EditorGUILayout.Space();

        // Single-conversation tools (existing)
        using (new EditorGUI.DisabledScope(!isSingle))
        {
            if (GUILayout.Button("Export Unified CSV (1 sheet)…", GUILayout.Height(24)))
            {
                if (!IsValidConversationRoot(conversationRoot)) return;
                var path = EditorUtility.SaveFilePanel("Export Unified CSV", Application.dataPath, conversationRoot.name + "_nodes", "csv");
                if (!string.IsNullOrEmpty(path))
                    ExportUnifiedCsv(conversationRoot, path);
            }

            if (GUILayout.Button("Import Unified CSV (Patch)…", GUILayout.Height(24)))
            {
                if (!IsValidConversationRoot(conversationRoot)) return;
                var path = EditorUtility.OpenFilePanel("Import Unified CSV", Application.dataPath, "csv");
                if (!string.IsNullOrEmpty(path))
                {
                    Undo.RegisterFullObjectHierarchyUndo(conversationRoot, "Patch Unified CSV");
                    ImportUnifiedCsv(conversationRoot, path);
                }
            }
        }

        // NEW: Conversation set export
        using (new EditorGUI.DisabledScope(!isSet))
        {
            if (GUILayout.Button("Export All Child Conversations (CSV Folder)…", GUILayout.Height(24)))
            {
                var folder = EditorUtility.SaveFolderPanel("Export Conversations CSVs", Application.dataPath, conversationRoot.name + "_CSVs");
                if (!string.IsNullOrEmpty(folder))
                {
                    ExportAllChildConversationsCsv(conversationRoot, folder);
                }
            }
        }
    }

    EditorGUILayout.EndScrollView();
}

// 1) Add these helpers near IsValidConversationRoot(...)
        static bool IsValidConversationSetRoot(GameObject go)
        {
            if (!go) return false;
            // "Set" = direct children that are ConversationManager(s). We only look at direct children,
            // matching your structure description.
            return go.transform.Cast<Transform>().Any(t => t.parent == go.transform && t.GetComponent<VNEngine.ConversationManager>() != null);
        }

        static bool HasAnyNodeChild(Transform t)
        {
            // must have at least one direct child Node (or deeper)
            return t.GetComponentsInChildren<VNEngine.Node>(true).Length > 0;
        }

        static void ExportAllChildConversationsCsv(GameObject setRoot, string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;
            if (!System.IO.Directory.Exists(folderPath))
                System.IO.Directory.CreateDirectory(folderPath);

            int exported = 0, skipped = 0;
            foreach (Transform child in setRoot.transform)
            {
                var cm = child.GetComponent<VNEngine.ConversationManager>();
                if (!cm) continue;

                // Avoid exporting empty shells
                if (!HasAnyNodeChild(child))
                {
                    skipped++;
                    continue;
                }

                // Reuse the existing per-conversation exporter so we keep behavior consistent
                var safeName = child.name.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
                var outPath = Path.Combine(folderPath, safeName + "_nodes.csv");

                try
                {
                    ExportUnifiedCsv(child.gameObject, outPath);
                    exported++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to export CSV for {child.name}: {ex.Message}", child.gameObject);
                }
            }

            Debug.Log($"Conversation set export complete. Exported: {exported}, Skipped (no nodes): {skipped} → {folderPath}");
            EditorUtility.RevealInFinder(folderPath);
        }

        static bool IsValidConversationRoot(GameObject go)
{
    if (!go) return false;
    // Must be a real conversation root: has a ConversationManager
    // and at least one direct child Node.
    return go.GetComponent<VNEngine.ConversationManager>() != null
           && go.transform.Cast<Transform>().Any(t => t.GetComponent<VNEngine.Node>() != null);
}

        // ==== PAYLOAD BUILDERS ================================================
        static IfNodePayload BuildIfNodePayload(VNEngine.IfNode n)
        {
            var p = new IfNodePayload
            {
                conditionIs = GetEnumName(n, "Is_Condition_Met"),
                action = GetEnumName(n, "Action"),
                continueConversation = GetBool(n, "Continue_Conversation"),
                conversationName = n.Conversation_To_Switch_To ? n.Conversation_To_Switch_To.name : null,
                nodeToSwitchToName = n.Node_To_Switch_To ? n.Node_To_Switch_To.name : null
            };

            int count = GetInt(n, "Number_Of_Conditions");
            count = Mathf.Clamp(count, 0, VNEngine.IfNode.max_number_of_conditions);

            for (int i = 0; i < count; i++)
            {
                var c = new IfCond
                {
                    kind         = Enum.GetName(typeof(VNEngine.Condition), n.Conditions[i]),
                    statName     = n.Stat_Name[i],
                    boolValue    = n.Bool_Compare_Value[i],
                    floatValue   = n.Float_Compare_Value[i],
                    stringValue  = n.String_Compare_Value[i],
                    stringIs     = Enum.GetName(typeof(VNEngine.Result), n.String_Is[i]),
                    floatCompare = Enum.GetName(typeof(Float_Stat_Comparator), n.Float_Stat_Is[i]),
                    nullObject   = n.Check_Null_Object[i]   ? n.Check_Null_Object[i].name   : null,
                    activeObject = n.Check_Active_Object[i] ? n.Check_Active_Object[i].name : null,
                    logicBefore  = (i > 0) ? Enum.GetName(typeof(VNEngine.Boolean_Logic), n.Logic[i - 1]) : null
                };
                p.conditions.Add(c);
            }

            return p;
        }
        
        static DialoguePayload BuildDialoguePayload(VNEngine.DialogueNode dn)
        {
            string actorName = GetString(dn, "actor");
            string actorSource = GetEnumName(dn, "actor_name_from");
            string speaker = GetString(dn, "textbox_title");
            string key = GetString(dn, "localized_key");
            string keySource = GetEnumName(dn, "dialogue_from");
            string text = GetString(dn, "text");

            bool bringFront = GetBool(dn, "bring_speaker_to_front");
            bool darken = GetBool(dn, "darken_all_other_characters");

            return new DialoguePayload
            {
                actorKey = actorName,
                actorKeySource = actorSource,
                speakerTitle = speaker,
                dialogueKey = key,
                dialogueKeySource = keySource,
                text = text,
                bringToFront = bringFront,
                darkenOthers = darken
            };
        }

        static ChoicePayload BuildChoicePayload(VNEngine.ChoiceNode cn, string nodeGuid)
        {
            var payload = new ChoicePayload
            {
                localize = GetBool(cn, "Localize_Choice_Text")
            };

            int n = GetInt(cn, "Number_Of_Choices");
            var keys = GetStringArray(cn, "Button_Text");
            var disabled = GetStringArray(cn, "Disabled_Text");

            for (int i = 0; i < n; i++)
            {
                string k = (keys != null && i < keys.Length) ? keys[i] : "";
                string d = (disabled != null && i < disabled.Length) ? disabled[i] : "";
                string resolved = k;

                if (payload.localize && !string.IsNullOrEmpty(k) && VNEngine.VNSceneManager.scene_manager != null)
                    resolved = VNEngine.VNSceneManager.scene_manager.Get_Localized_Dialogue_Entry(k);

                payload.choices.Add(new ChoiceItem
                {
                    id = $"{nodeGuid}#{i}",
                    key = k,
                    text = resolved,
                    disabledText = d
                });
            }

            return payload;
        }

        static ChangeActorImagePayload BuildChangeActorImagePayload(VNEngine.ChangeActorImageNode n)
        {
            var (guid, file) = AssetGuidAndFile(n, "new_image");
            return new ChangeActorImagePayload
            {
                actorName = GetString(n, "actor_name"),
                imageGuid = guid,
                imageFile = file,
                fadeIn = GetBool(n, "fade_in_new_image"),
                lightenActor = GetBool(n, "lighten_actor"),
                bringToFront = GetBool(n, "bring_actor_to_front")
            };
        }

        static SetBackgroundPayload BuildSetBackgroundPayload(object n /*SetBackground*/)
        {
            var (guid, file) = AssetGuidAndFile(n, "sprite");
            return new SetBackgroundPayload
            {
                imageGuid = guid,
                imageFile = file,
                setForeground = GetBool(n, "set_foreground"),
                fadeIn = GetBool(n, "fade_in"),
                fadeOut = GetBool(n, "fade_out")
            };
        }

        static SetBackgroundTransparentPayload BuildSetBackgroundTransparentPayload(
            object n)
        {
            return new SetBackgroundTransparentPayload
            {
                fadeIn = GetBool(n, "fade_in"),
                fadeOut = GetBool(n, "fade_out")
            };
        }

        static EnterActorPayload BuildEnterActorPayload(VNEngine.EnterActorNode n)
        {
            return new EnterActorPayload
            {
                actorName = GetString(n, "actor_name"),
                actorNameSource = GetEnumName(n, "actor_name_from"),
                entranceType = GetEnumName(n, "entrance_type"),
                fadeInTime = GetFloat(n, "fade_in_time"),
                destination = GetEnumName(n, "destination")
            };
        }

        // ==== EXPORT ==========================================================
        static void EnsureIds(GameObject root)
        {
            foreach (Transform child in root.transform)
            {
                if (!child.GetComponent<VNEngine.Node>()) continue;
                if (!child.GetComponent<NodeExportTag>()) child.gameObject.AddComponent<NodeExportTag>();
            }
        }
        
 
    static void ApplyIfNodePayload(VNEngine.IfNode n, IfNodePayload p)
{
    // Enums
    SetEnum(n, "Is_Condition_Met", p.conditionIs);
    SetEnum(n, "Action",           p.action);

    Set(n, "Continue_Conversation", p.continueConversation);

    // Optional targets by name (best effort)
    if (!string.IsNullOrEmpty(p.conversationName))
    {
        var all = Resources.FindObjectsOfTypeAll<VNEngine.ConversationManager>();
        var cm = all.FirstOrDefault(x => x.name == p.conversationName);
        if (cm) n.Conversation_To_Switch_To = cm;
    }

    if (!string.IsNullOrEmpty(p.nodeToSwitchToName))
    {
        // Try to find a node with that name under any Conversation
        var allNodes = Resources.FindObjectsOfTypeAll<VNEngine.Node>();
        var target = allNodes.FirstOrDefault(x => x.name == p.nodeToSwitchToName);
        if (target) n.Node_To_Switch_To = target;
    }

    // Conditions
    int count = p.conditions?.Count ?? 0;
    count = Mathf.Clamp(count, 0, VNEngine.IfNode.max_number_of_conditions);
    Set(n, "Number_Of_Conditions", count);

    for (int i = 0; i < count; i++)
    {
        var c = p.conditions[i];

        // Enums/values
        if (!string.IsNullOrEmpty(c.kind))
            n.Conditions[i] = (VNEngine.Condition)Enum.Parse(typeof(VNEngine.Condition), c.kind);
        n.Stat_Name[i]        = c.statName ?? "";
        n.Bool_Compare_Value[i]   = c.boolValue;
        n.Float_Compare_Value[i]  = c.floatValue;
        n.String_Compare_Value[i] = c.stringValue ?? "";

        if (!string.IsNullOrEmpty(c.stringIs))
            n.String_Is[i] = (VNEngine.Result)Enum.Parse(typeof(VNEngine.Result), c.stringIs);
        if (!string.IsNullOrEmpty(c.floatCompare))
            n.Float_Stat_Is[i] = (Float_Stat_Comparator)Enum.Parse(typeof(Float_Stat_Comparator), c.floatCompare);

        // Best‑effort resolve object references by name (editor time)
        if (!string.IsNullOrEmpty(c.nullObject))
            n.Check_Null_Object[i] = FindSceneObjectByName(c.nullObject);
        if (!string.IsNullOrEmpty(c.activeObject))
            n.Check_Active_Object[i] = FindSceneObjectByName(c.activeObject);

        // Logic joining (i-1) -> i
        if (i > 0 && !string.IsNullOrEmpty(c.logicBefore))
            n.Logic[i - 1] = (VNEngine.Boolean_Logic)Enum.Parse(typeof(VNEngine.Boolean_Logic), c.logicBefore);
    }
    
}

// --- CSV helpers ---

    static string CsvEscape(string s)
{
    if (s == null) return "";
    bool needs = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
    if (!needs) return s;
    return "\"" + s.Replace("\"", "\"\"") + "\"";
}

    const char ChoiceSep = '|';

    static bool Has(string s) => !string.IsNullOrWhiteSpace(s);

    static string[] SplitChoices(string s)
{   
    if (!Has(s)) return Array.Empty<string>();
    return s.Split(new[] { ChoiceSep }, StringSplitOptions.None)
        .Select(x => x.Trim())
        .Where(x => x.Length > 0)
        .ToArray();
}


// Split/join with '|'
    static string JoinPipe(IEnumerable<string> arr) => arr == null ? "" : string.Join("|", arr.Select(x => x ?? ""));
    static string[] SplitPipe(string s) => string.IsNullOrEmpty(s) ? Array.Empty<string>() : s.Split('|');

// Minimal CSV line parser (handles quotes)
    static List<string> ParseCsvLine(string line)
    {
    var cells = new List<string>();
    if (string.IsNullOrEmpty(line)) return cells;
    int i = 0, n = line.Length;
    while (i < n)
    {
        if (line[i] == '"')
        {
            i++;
            var sb = new System.Text.StringBuilder();
            while (i < n)
            {
                if (line[i] == '"' && i + 1 < n && line[i + 1] == '"') { sb.Append('"'); i += 2; }
                else if (line[i] == '"') { i++; break; }
                else { sb.Append(line[i++]); }
            }
            if (i < n && line[i] == ',') i++;
            cells.Add(sb.ToString());
        }
        else
        {
            int start = i;
            while (i < n && line[i] != ',') i++;
            cells.Add(line.Substring(start, i - start));
            if (i < n && line[i] == ',') i++;
        }
    }
    return cells;
}
// ---------- CHOICES: one-row export helper ----------
    static (bool localize, string keysPipe, string textsPipe, string disabledPipe)
    ExportRow_ChoiceNode(VNEngine.ChoiceNode cn, string nodeGuid)
{
    // Build runtime-ish payload to get resolved text if localization is on,
    // but for the CSV we intentionally mirror keys <-> texts 1:1.
    var payload = BuildChoicePayload(cn, nodeGuid);

    var keys     = GetStringArray(cn, "Button_Text")   ?? Array.Empty<string>();
    var disabled = GetStringArray(cn, "Disabled_Text") ?? Array.Empty<string>();

    int n = Mathf.Max(0, GetInt(cn, "Number_Of_Choices"));

    // truncate or pad to exactly n so export reflects the active choices only
    if (keys.Length > n)      keys = keys.Take(n).ToArray();
    else if (keys.Length < n) Array.Resize(ref keys, n);

    if (disabled.Length > n)      disabled = disabled.Take(n).ToArray();
    else if (disabled.Length < n) Array.Resize(ref disabled, n);

    // mirror: keys == texts in CSV (import will keep them mirrored)
    var keysPipe  = JoinPipe(keys.Select(s => s ?? ""));
    var textsPipe = keysPipe;
    var disPipe   = JoinPipe(disabled.Select(s => s ?? ""));

    return (payload.localize, keysPipe, textsPipe, disPipe);}
    
    static bool TryParseBool(string s, out bool value)
{
    value = false;
    if (string.IsNullOrWhiteSpace(s)) return false;
    if (bool.TryParse(s, out value)) return true;
    if (s == "1") { value = true;  return true; }
    if (s == "0") { value = false; return true; }
    return false;
}

    static void ExportUnifiedCsv(GameObject root, string outPath)
{
    EnsureIds(root);

    // NEW: lock export to the same ConversationManager as 'root'
    var rootCM = root.GetComponent<VNEngine.ConversationManager>();

    var ordered = root
        .GetComponentsInChildren<VNEngine.Node>(true)     // any depth
        .Where(n => n && n.GetComponentInParent<VNEngine.ConversationManager>() == rootCM)
        .OrderBy(n => n.transform.GetSiblingIndex())
        .Select(n => n.transform)
        .ToList();

    var rows = new List<string>(ordered.Count + 1);
    rows.Add(string.Join(",", kUnifiedHeaders)); // header

    for (int i = 0; i < ordered.Count; i++)
    {
        var t = ordered[i];
        var node = t.GetComponent<VNEngine.Node>();
        if (!node) continue;

        var tag = t.GetComponent<NodeExportTag>();
        var id  = tag ? tag.Id : GUID.Generate().ToString();
        var typeName = node.GetType().Name;

        // create an empty row map
        var row = new Dictionary<string,string>(StringComparer.Ordinal);

        // shared columns
        Put(row, "id", id);
        Put(row, "order", i.ToString());
        Put(row, "type", typeName);
        Put(row, "goName", t.name);

        // per-type payload
        switch (typeName)
        {
            case "DialogueNode":
            {
                var p = BuildDialoguePayload((VNEngine.DialogueNode)node);
                Put(row, "actorKey",         p.actorKey);
//                Put(row, "actorKeySource",   p.actorKeySource);
                Put(row, "speakerTitle",     p.speakerTitle);
//                Put(row, "dialogueKey",      p.dialogueKey);
//                Put(row, "dialogueKeySource",p.dialogueKeySource);
                Put(row, "text",             p.text);
//                Put(row, "bringToFront",     p.bringToFront);
//                Put(row, "darkenOthers",     p.darkenOthers);
                break;
            }
            case "ChoiceNode":
            {
                var cn = (VNEngine.ChoiceNode)node;

                // Use the existing helper to build CSV-friendly values
                var (localize, keysPipe, textsPipe, disabledPipe) = ExportRow_ChoiceNode(cn, id);

                // Write the columns expected by the unified CSV header
                Put(row, "choice_keys",     keysPipe);
                Put(row, "choice_texts",    textsPipe);
                Put(row, "choice_disabled", disabledPipe);
                break;
            }
            case "ChangeActorImageNode":
            {
                var p = BuildChangeActorImagePayload((VNEngine.ChangeActorImageNode)node);
                Put(row, "change_actorName",     p.actorName);
                Put(row, "change_imageGuid",     p.imageGuid);
                Put(row, "change_imageFile",     p.imageFile);
//                Put(row, "change_fadeIn",        p.fadeIn);
//                Put(row, "change_lightenActor",  p.lightenActor);
///                Put(row, "change_bringToFront",  p.bringToFront);
                break;
            }
            case "SetBackground":
            {
                var p = BuildSetBackgroundPayload(node);
                Put(row, "bg_imageGuid",     p.imageGuid);
                Put(row, "bg_imageFile",     p.imageFile);
//                Put(row, "bg_setForeground", p.setForeground);
//                Put(row, "bg_fadeIn",        p.fadeIn);
//                Put(row, "bg_fadeOut",       p.fadeOut);
                break;
            }
            case "SetBackgroundTransparent":
            {
                var p = BuildSetBackgroundTransparentPayload(node);
                Put(row, "bgT_fadeIn",  p.fadeIn);
                Put(row, "bgT_fadeOut", p.fadeOut);
                break;
            }
            case "EnterActorNode":
            {
                var p = BuildEnterActorPayload((VNEngine.EnterActorNode)node);
                Put(row, "enter_actorName",        p.actorName);
//                Put(row, "enter_actorNameSource",  p.actorNameSource);
//                Put(row, "enter_entranceType",     p.entranceType);
//                Put(row, "enter_fadeInTime",       p.fadeInTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
//                Put(row, "enter_destination",      p.destination);
                break;
            }
            case "IfNode":
            {
                var p = BuildIfNodePayload((VNEngine.IfNode)node);
                Put(row, "if_conditionIs", p.conditionIs);
                Put(row, "if_action",      p.action);
                Put(row, "if_continue",    p.continueConversation);
                break;
            }
        }

        // finalize row into CSV
        rows.Add(RowToCsvLine(row, kUnifiedHeaders));
    }

    File.WriteAllLines(outPath, rows);
    Debug.Log($"Exported unified CSV with {rows.Count-1} node row(s) → {outPath}");
    EditorUtility.RevealInFinder(outPath);
}

    static void ImportUnifiedCsv(GameObject root, string path)
{
    if (!File.Exists(path)) { Debug.LogWarning("CSV not found: " + path); return; }
    var lines = File.ReadAllLines(path).ToList();
    if (lines.Count <= 1) { Debug.Log("CSV has no data rows."); return; }

    // Build id -> (Transform, Node)
    var map = new Dictionary<string,(Transform t, VNEngine.Node node)>();
    foreach (var tag in root.GetComponentsInChildren<NodeExportTag>(true))
    {
        var n = tag.GetComponent<VNEngine.Node>();
        if (n) map[tag.Id] = (tag.transform, n);
    }
    var header = ParseCsvLine(lines[0]);
    int Col(string name) => header.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
    string Cell(List<string> row, int idx) => (idx >= 0 && idx < row.Count) ? row[idx] : "";

    // required cols
    int c_id   = Col("id");
    int c_type = Col("type");
    int c_order= Col("order"); // optional

    // early out only if the REQUIRED columns are missing
    if (c_id < 0 || c_type < 0)
    {
        Debug.LogError("CSV is missing required columns: id/type");
        return;
    }

    // dialogue
    int d_actorKey = Col("actorKey");
    int d_speaker  = Col("speakerTitle");
    int d_text     = Col("text");

    // choices
    int ch_keys = Col("choice_keys");
    int ch_texts= Col("choice_texts");
    int ch_dis  = Col("choice_disabled");

    // change image
    int ca_name = Col("change_actorName");
    int ca_guid = Col("change_imageGuid");
    int ca_file = Col("change_imageFile");

    // backgrounds
    int bg_guid = Col("bg_imageGuid"), bg_file = Col("bg_imageFile");

    // bg transparent
    int bgt_fadeIn=Col("bgT_fadeIn"), bgt_fadeOut=Col("bgT_fadeOut");

    // enter actor
    int en_actor=Col("enter_actorName");

    // if node
    int if_is=Col("if_conditionIs"), if_act=Col("if_action"), if_cont=Col("if_continue");

    Undo.RegisterFullObjectHierarchyUndo(root, "Import VN CSV");

    for (int i = 1; i < lines.Count; i++)
    {
        var line = ParseCsvLine(lines[i]);
        if (line.Count == 0) continue;

        var id      = Cell(line, c_id);
        var rowType = Cell(line, c_type);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(rowType)) continue;
        if (!map.TryGetValue(id, out var tuple)) continue;

        var (t, node) = tuple;
        var typeName  = node.GetType().Name;

        // Only modify when types match
        if (!string.Equals(typeName, rowType, StringComparison.Ordinal))
            continue;

        // Sibling order (optional)
        var orderStr = Cell(line, c_order);
        if (!string.IsNullOrEmpty(orderStr) && int.TryParse(orderStr, out var ord))
        {
            t.SetSiblingIndex(Mathf.Clamp(ord, 0, root.transform.childCount - 1));
            EditorUtility.SetDirty(t);
        }

        switch (typeName)
        {
            case "DialogueNode":
            {
                var dn = (VNEngine.DialogueNode)node;
                var actor   = Cell(line, d_actorKey);
                var speaker = Cell(line, d_speaker);
                var text    = Cell(line, d_text);

                if (!string.IsNullOrEmpty(actor))   Set(dn,"actor",actor);
                if (!string.IsNullOrEmpty(speaker)) Set(dn,"textbox_title",speaker);
                if (!string.IsNullOrEmpty(text))    Set(dn,"text",text);

                EditorUtility.SetDirty(dn);
                break;
            }
            case "ChoiceNode":
            {
                var cn = (VNEngine.ChoiceNode)node;

                // read from the CURRENT ROW, not re-parsing a single cell
                string keysJoined    = Cell(line, ch_keys);
                string textsJoined   = Cell(line, ch_texts);
                string disabledJoin  = Cell(line, ch_dis);

                // treat "none"/"null" as empty too
                bool IsBlank(string s) =>
                    string.IsNullOrWhiteSpace(s) || string.Equals(s, "none", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "null", StringComparison.OrdinalIgnoreCase);

                // mirror one into the other if only one is present
                bool hasKeys = !IsBlank(keysJoined);
                bool hasText = !IsBlank(textsJoined);
                if (hasKeys && !hasText) textsJoined = keysJoined;
                else if (!hasKeys && hasText) keysJoined = textsJoined;

                if (hasKeys || hasText)
                {
                    var keys     = IsBlank(keysJoined)   ? Array.Empty<string>() : keysJoined.Split('|');
                    var texts    = IsBlank(textsJoined)  ? Array.Empty<string>() : textsJoined.Split('|');
                    var disabled = IsBlank(disabledJoin) ? Array.Empty<string>() : disabledJoin.Split('|');

                    // keep length consistent (use keys/texts length as driver)
                    int n = Math.Max(keys.Length, texts.Length);
                    if (n == 0) break;

                    Array.Resize(ref keys, n);
                    Array.Resize(ref texts, n);
                    Array.Resize(ref disabled, n);

                    Set(cn, "Number_Of_Choices", n);
                    SetArray(cn, "Button_Text",   keys);     // keys shown on buttons (or localization keys)
                    SetArray(cn, "Disabled_Text", disabled); // optional disabled text

                    EditorUtility.SetDirty(cn);
                }
                break;
            }

            case "ChangeActorImageNode":
            {
                var n = (VNEngine.ChangeActorImageNode)node;
                var name = Cell(line, ca_name);
                var guid = Cell(line, ca_guid);
                var file = Cell(line, ca_file);

                if (!string.IsNullOrEmpty(name)) Set(n, "actor_name", name);

                // only set if something valid actually resolves
                bool set =
                    TrySetAssetByGuidSafe(n, "new_image", guid) ||
                    TrySetAssetByNameSafe(n, "new_image", file);

                if (set) EditorUtility.SetDirty(n);
                break;
            }

            case "SetBackground":
            {
                var comp = node;
                var guid = Cell(line, bg_guid);
                var file = Cell(line, bg_file);

                bool set =
                    TrySetAssetByGuidSafe(comp, "sprite", guid) ||
                    TrySetAssetByNameSafe(comp, "sprite", file);

                if (set) EditorUtility.SetDirty(comp);
                break;
            }

            case "SetBackgroundTransparent":
            {
                var comp = node;
                if (TryParseBool(Cell(line, bgt_fadeIn),  out var fi)) Set(comp,"fade_in",  fi);
                if (TryParseBool(Cell(line, bgt_fadeOut), out var fo)) Set(comp,"fade_out", fo);
                EditorUtility.SetDirty(comp);
                break;
            }
            case "EnterActorNode":
            {
                var n = (VNEngine.EnterActorNode)node;
                var actor = Cell(line, en_actor);
                if (!string.IsNullOrEmpty(actor)) Set(n,"actor_name", actor);
                EditorUtility.SetDirty(n);
                break;
            }
            case "IfNode":
            {
                var n = (VNEngine.IfNode)node;
                var isStr  = Cell(line, if_is);
                var actStr = Cell(line, if_act);
                var contStr= Cell(line, if_cont);

                if (!string.IsNullOrEmpty(isStr))  SetEnum(n,"Is_Condition_Met",  isStr);
                if (!string.IsNullOrEmpty(actStr)) SetEnum(n,"Action",            actStr);
                if (TryParseBool(contStr, out var bc)) Set(n,"Continue_Conversation", bc);

                EditorUtility.SetDirty(n);
                break;
            }
        }
    }

    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
    Debug.Log("Unified CSV import complete.");
}
    // Case‑insensitive column lookup
    static int Col(IReadOnlyList<string> header, string name)
{
    if (header == null || string.IsNullOrEmpty(name)) return -1;
    for (int i = 0; i < header.Count; i++)
        if (string.Equals(header[i], name, StringComparison.OrdinalIgnoreCase))
            return i;
    return -1;
}
    static bool TrySetAssetByGuidSafe(UnityEngine.Object target, string fieldName, string guid)
{
    if (string.IsNullOrWhiteSpace(guid)) return false;
    if (string.Equals(guid, "none", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(guid, "null", StringComparison.OrdinalIgnoreCase)) return false;

#if UNITY_EDITOR
    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
    if (string.IsNullOrEmpty(path)) return false;
    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
    if (!asset) return false;
    Set(target, fieldName, asset);
    return true;
#else
    return false;
#endif
}

    static bool TrySetAssetByNameSafe(UnityEngine.Object target, string fieldName, string file)
{
    if (string.IsNullOrWhiteSpace(file)) return false;
    if (string.Equals(file, "none", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(file, "null", StringComparison.OrdinalIgnoreCase)) return false;

#if UNITY_EDITOR
    var guids = UnityEditor.AssetDatabase.FindAssets(System.IO.Path.GetFileNameWithoutExtension(file));
    foreach (var g in guids)
    {
        var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
        if (asset && string.Equals(System.IO.Path.GetFileName(p), file, StringComparison.OrdinalIgnoreCase))
        {
            Set(target, fieldName, asset);
            return true;
        }
    }
#endif
    return false;
}

static GameObject FindSceneObjectByName(string name)
{
    if (string.IsNullOrEmpty(name)) return null;
    var go = GameObject.Find(name);
    if (go) return go;
    var all = Resources.FindObjectsOfTypeAll<GameObject>();
    return all.FirstOrDefault(x => x.name == name);
}
// Utility: tries to find a scene object by name (edit-time). Returns null if not found.
        static void ApplyTypedPayload(VNEngine.Node node, NodeRecord rec)
        {
            switch (rec.type)
            {
                case "DialogueNode":
                    if (rec.dialogue != null)
                        ApplyDialoguePayload((VNEngine.DialogueNode)node, rec.dialogue);
                    break;

                case "ChoiceNode":
                    if (rec.choice != null)
                        ApplyChoicePayload((VNEngine.ChoiceNode)node, rec.choice);
                    break;

                case "ChangeActorImageNode":
                    if (rec.changeActorImage != null)
                        ApplyChangeActorImagePayload((VNEngine.ChangeActorImageNode)node, rec.changeActorImage);
                    break;

                case "SetBackground":
                    if (rec.setBackground != null)
                        ApplySetBackgroundPayload(node, rec.setBackground);
                    break;

                case "SetBackgroundTransparent":
                    if (rec.setBackgroundTransparent != null)
                        ApplySetBackgroundTransparentPayload(node, rec.setBackgroundTransparent);
                    break;

                case "EnterActorNode":
                    if (rec.enterActor != null)
                        ApplyEnterActorPayload((VNEngine.EnterActorNode)node, rec.enterActor);
                    break;
                
                case "IfNode":
                    if (rec.ifNode != null) ApplyIfNodePayload((VNEngine.IfNode)node, rec.ifNode);
                    break;                
            }
        }
        static void ApplyDialoguePayload(VNEngine.DialogueNode dn, DialoguePayload p)
        {
            Set(dn, "actor", p.actorKey);
            SetEnum(dn, "actor_name_from", p.actorKeySource);
            Set(dn, "textbox_title", p.speakerTitle);
            Set(dn, "localized_key", p.dialogueKey);
            SetEnum(dn, "dialogue_from", p.dialogueKeySource);
            Set(dn, "text", p.text);
            Set(dn, "bring_speaker_to_front", p.bringToFront);
            Set(dn, "darken_all_other_characters", p.darkenOthers);
        }
        static void ApplyChoicePayload(VNEngine.ChoiceNode cn, ChoicePayload p)
        {
            Set(cn, "Localize_Choice_Text", p.localize);
            int n = p.choices?.Count ?? 0;
            Set(cn, "Number_Of_Choices", n);

            var keys = GetStringArray(cn, "Button_Text") ?? new string[Mathf.Max(6, n)];
            var disabled = GetStringArray(cn, "Disabled_Text") ?? new string[Mathf.Max(6, n)];

            if (keys.Length < n) Array.Resize(ref keys, n);
            if (disabled.Length < n) Array.Resize(ref disabled, n);

            for (int i = 0; i < n; i++)
            {
                keys[i] = p.choices[i]?.key ?? "";
                disabled[i] = p.choices[i]?.disabledText ?? "";
            }

            SetArray(cn, "Button_Text", keys);
            SetArray(cn, "Disabled_Text", disabled);
        }
        static void ApplyChangeActorImagePayload(VNEngine.ChangeActorImageNode n, ChangeActorImagePayload p)
        {
            Set(n, "actor_name", p.actorName);
            if (!string.IsNullOrEmpty(p.imageGuid))
            {
                var path = AssetDatabase.GUIDToAssetPath(p.imageGuid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) SetObject(n, "new_image", sprite);
            }
        }
        static void ApplySetBackgroundPayload(object node /*SetBackground*/, SetBackgroundPayload p)
        {
            Set(node, "set_foreground", p.setForeground);
            Set(node, "fade_in", p.fadeIn);
            Set(node, "fade_out", p.fadeOut);

            if (!string.IsNullOrEmpty(p.imageGuid))
            {
                var path = AssetDatabase.GUIDToAssetPath(p.imageGuid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) SetObject(node, "sprite", sprite);
            }
        }
        static void ApplySetBackgroundTransparentPayload(object node /*SetBackgroundTransparent*/,
            SetBackgroundTransparentPayload p)
        {
            Set(node, "fade_in", p.fadeIn);
            Set(node, "fade_out", p.fadeOut);
        }

        static void ApplyEnterActorPayload(VNEngine.EnterActorNode n, EnterActorPayload p)
        {
            Set(n, "actor_name", p.actorName);
            SetEnum(n, "actor_name_from", p.actorNameSource);
            SetEnum(n, "entrance_type", p.entranceType);
            Set(n, "fade_in_time", p.fadeInTime);
            SetEnum(n, "destination", p.destination);
        }

        // ==== REFLECTION HELPERS (get) ========================================
        static bool TryGetField(Type t, string name, out FieldInfo fi)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            while (t != null && t != typeof(object))
            {
                fi = t.GetField(name, flags);
                if (fi != null) return true;
                t = t.BaseType;
            }

            fi = null;
            return false;
        }

        static string GetString(object o, string f) =>
            TryGetField(o.GetType(), f, out var fi) && fi.FieldType == typeof(string) ? (string)fi.GetValue(o) : "";

        static bool GetBool(object o, string f) =>
            TryGetField(o.GetType(), f, out var fi) && fi.FieldType == typeof(bool) ? (bool)fi.GetValue(o) : false;

        static int GetInt(object o, string f) => TryGetField(o.GetType(), f, out var fi) && fi.FieldType == typeof(int)
            ? (int)fi.GetValue(o)
            : 0;

        static float GetFloat(object o, string f) =>
            TryGetField(o.GetType(), f, out var fi) && fi.FieldType == typeof(float) ? (float)fi.GetValue(o) : 0f;

        static string[] GetStringArray(object o, string f)
            => TryGetField(o.GetType(), f, out var fi) && fi.FieldType == typeof(string[])
                ? (string[])fi.GetValue(o)
                : null;

        static string GetEnumName(object o, string f)
        {
            if (!TryGetField(o.GetType(), f, out var fi) || !fi.FieldType.IsEnum) return "";
            var val = fi.GetValue(o);
            return Enum.GetName(fi.FieldType, val) ?? "";
        }

        // ==== REFLECTION HELPERS (set) ========================================
        static void Set(object o, string f, string v)
        {
            if (TryGetField(o.GetType(), f, out var fi) && fi.FieldType == typeof(string)) fi.SetValue(o, v);
        }

        static void Set(object o, string f, bool v)
        {
            if (TryGetField(o.GetType(), f, out var fi) && fi.FieldType == typeof(bool)) fi.SetValue(o, v);
        }

        static void Set(object o, string f, int v)
        {
            if (TryGetField(o.GetType(), f, out var fi) && fi.FieldType == typeof(int)) fi.SetValue(o, v);
        }

        static void Set(object o, string f, float v)
        {
            if (TryGetField(o.GetType(), f, out var fi) && fi.FieldType == typeof(float)) fi.SetValue(o, v);
        }

        static void SetArray(object o, string f, Array arr)
        {
            if (TryGetField(o.GetType(), f, out var fi) && fi.FieldType.IsArray) fi.SetValue(o, arr);
        }

        static void SetEnum(object o, string f, string enumName)
        {
            if (string.IsNullOrEmpty(enumName)) return;
            if (TryGetField(o.GetType(), f, out var fi) && fi.FieldType.IsEnum)
            {
                try
                {
                    fi.SetValue(o, Enum.Parse(fi.FieldType, enumName));
                }
                catch
                {
                }
            }
        }

        static void SetObject(object o, string f, UnityEngine.Object obj)
        {
            if (TryGetField(o.GetType(), f, out var fi) && typeof(UnityEngine.Object).IsAssignableFrom(fi.FieldType))
                fi.SetValue(o, obj);
        }

   
        static (string guid, string file) AssetGuidAndFile(object target, string fieldName)
        {
            if (!TryGetField(target.GetType(), fieldName, out var fi)) return (null, null);
            if (!typeof(UnityEngine.Object).IsAssignableFrom(fi.FieldType)) return (null, null);

            var obj = fi.GetValue(target) as UnityEngine.Object;
            if (!obj) return (null, null);

            string path = AssetDatabase.GetAssetPath(obj);
            string guid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
            string file = string.IsNullOrEmpty(path) ? null : Path.GetFileNameWithoutExtension(path);
            return (guid, file);
        }
// Column order used by the unified CSV
        static readonly string[] kUnifiedHeaders = new[]{
            "id","order","type","goName",
            "actorKey","speakerTitle","text",
            "choice_keys","choice_texts","choice_disabled",   // <-- add these two
            "change_actorName","change_imageGuid","change_imageFile",
            "bg_imageGuid","bg_imageFile",
            "bgT_fadeIn","bgT_fadeOut",
            "enter_actorName",
            "if_conditionIs","if_action","if_continue"
        };
        
// safe put
        static void Put(Dictionary<string,string> row, string key, string value)
        {
            if (row == null) return;
            row[key] = value ?? "";
        }
        static void Put(Dictionary<string,string> row, string key, bool value) => row[key] = value ? "true" : "false";

// build a CSV line from a row map + headers
        static string RowToCsvLine(Dictionary<string,string> row, IReadOnlyList<string> headers)
        {
            var cells = new string[headers.Count];
            for (int i = 0; i < headers.Count; i++)
            {
                row.TryGetValue(headers[i], out var val);
                cells[i] = CsvEscape(val ?? "");
            }
            return string.Join(",", cells);
        }
        
    }
}
#endif
