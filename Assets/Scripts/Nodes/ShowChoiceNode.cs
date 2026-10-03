using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace VNEngine
{
    [AddComponentMenu("Game Object/VN Engine/Branching/Show Choice Node")]
    public class ShowChoiceNode : Node
    {
        [System.Serializable]
        public class Choice
        {
            [TextArea] public string text;
            public ConversationManager nextConversation;      // null => continue current
            public List<FlexibleTraitRequirement> requirements = new List<FlexibleTraitRequirement>(); // ALL must pass
            public bool enableLogging;
            public string label;      // stable research ID, e.g. "gain_frame_A"
            public string category;   // optional grouping, e.g. "framing"
            public string variant;    // optional arm, e.g. "A" | "B"
        }

        public List<Choice> choices = new List<Choice>();
        private List<int> _presentedOrder = new(); // shownIndex -> origIndex

        public bool logOnShow = true;
        public bool logOnSelect = true;
        public bool includeTraitSnapshot = true; 
        public bool hideDialogueUI = true; // default Answer Campus behavior

        [SerializeField] private TraitRegistry traitRegistry;
        private readonly List<Button> _activeButtons = new();
        private void Reset()  // called when the component is first added
        {
            if (traitRegistry == null) traitRegistry = TraitRegistry.Load();
        }

#if UNITY_EDITOR
        private void OnValidate() // keeps it filled when edited/duplicated
        {
            if (!Application.isPlaying && traitRegistry == null)
                traitRegistry = TraitRegistry.Load();
        }
#endif
        public override void Run_Node()
        {
            if (hideDialogueUI)
                VNSceneManager.scene_manager.Show_UI(false);                         // hide dialogue while showing choices :contentReference[oaicite:3]{index=3}

            UIManager.ui_manager.choice_panel.SetActive(true);                        // open the panel :contentReference[oaicite:4]{index=4}
            ClearAllChoiceButtons();

            // 1) Filter to visible (requirements met)
            var visible = new List<int>();
            int uiMax = UIManager.ui_manager.choice_buttons.Length;                  // bound to prefab button capacity :contentReference[oaicite:5]{index=5}
            for (int i = 0; i < choices.Count && visible.Count < uiMax; i++)
            {
                if (MeetsRequirements(choices[i].requirements))
                    visible.Add(i);
            }

            // 2) Always randomize order (Fisher–Yates)
            for (int i = 0; i < visible.Count; i++)
            {
                int j = Random.Range(i, visible.Count);
                (visible[i], visible[j]) = (visible[j], visible[i]);
            }

            _presentedOrder = new List<int>(visible);
            // 3) Paint buttons
            _activeButtons.Clear();
            for (int slot = 0; slot < visible.Count && slot < uiMax; slot++)
            {
                int idx = visible[slot];
                var c = choices[idx];

                var btn = UIManager.ui_manager.choice_buttons[slot];
                btn.gameObject.SetActive(true);
                btn.interactable = true;
                btn.onClick.RemoveAllListeners();
                btn.GetComponentInChildren<Text>().text = DialogueNode.Insert_Stats_into_Text(c.text);

                btn.onClick.AddListener(() => OnChoice(idx));
                _activeButtons.Add(btn);
            }

            // Hide leftovers
            for (int i = visible.Count; i < uiMax; i++)
            {
                UIManager.ui_manager.choice_buttons[i].onClick.RemoveAllListeners();
                UIManager.ui_manager.choice_buttons[i].gameObject.SetActive(false);
            }

            // Animate & focus first active (gamepad only — mouse/keyboard shouldn't get a forced highlight)
            UIManager.ui_manager.AnimateChoiceButtons(_activeButtons);               // existing helper :contentReference[oaicite:6]{index=6}
            if (_activeButtons.Count > 0 && Gamepad.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
                EventSystem.current.SetSelectedGameObject(_activeButtons[0].gameObject);
            }

            // If this choice is timed, hand the countdown everything it needs to run
            // alongside the buttons (the sequencer never runs both nodes concurrently on its own).
            var timed = GetComponent<TimedChoiceNode>();
            if (timed != null)
            {
                var slotConversations = new List<ConversationManager>(visible.Count);
                for (int slot = 0; slot < visible.Count; slot++)
                    slotConversations.Add(choices[visible[slot]].nextConversation);

                timed.BeginCountdown(this, _activeButtons, slotConversations, visible);
            }

            if (logOnShow && Logging.Instance != null)
            {
                var cm = GetComponentInParent<ConversationManager>();
                var json = new Dictionary<string, object>
                {
                    { "conversation", cm ? cm.name : "" },
                    { "node", name },
                    { "nodeIndex", cm ? cm.cur_node : 0 },                   // current node index
                    { "hidden_count", Mathf.Max(0, choices.Count - _presentedOrder.Count) },
                    { "order", _presentedOrder },                            // shownIndex -> origIndex
                    { "options", BuildOptionsArray() }                       // descriptors incl. research tags
                };
                if (includeTraitSnapshot) json["traits_current"] = CurrentTraitMap();

                SafeLogJson("choice_presented", json);
            }
            
        }

        private void OnChoice(int idx)
        {
            GetComponent<TimedChoiceNode>()?.StopTimer();

            if (logOnSelect && Logging.Instance != null)
            {
                var cm = GetComponentInParent<ConversationManager>();
                var json = new Dictionary<string, object>
                {
                    { "conversation", cm ? cm.name : "" },
                    { "node", name },
                    { "nodeIndex", cm ? cm.cur_node : 0 },
                    { "order", _presentedOrder },
                    { "selected", OptionDescriptor(idx) }
                };
                if (includeTraitSnapshot) json["traits_current"] = CurrentTraitMap();

                SafeLogJson("choice_selected", json);
            }

            var c = choices[idx];

            // Jump? End current conversation then start target (same as ChoicesManager.Change_Conversation)
            if (c.nextConversation != null)
            {
                JumpToConversation(c.nextConversation);
                return;
            }

            // Continue in current conversation
            CleanupAndHide();
            base.Finish_Node();                                                      // advance to next node in current convo :contentReference[oaicite:10]{index=10}
        }

        // Called externally by TimedChoiceNode when its countdown ends with a surviving
        // button that matches default_choice - equivalent to the player clicking it.
        internal void SelectChoiceExternally(int origIndex) => OnChoice(origIndex);

        // Called externally by TimedChoiceNode when its countdown ends with no button
        // left standing (the "silent" default_choice case).
        internal void ForceJumpExternally(ConversationManager target) => JumpToConversation(target);

        private void JumpToConversation(ConversationManager target)
        {
            if (VNSceneManager.current_conversation != null)
                VNSceneManager.current_conversation.Finish_Conversation();        // finish current
            target.Start_Conversation();                                         // start target

            go_to_next_node = false;                                             // don’t auto-advance current node chain
            CleanupAndHide(restoreDialogueUI: false);                            // let the target conversation reveal the UI when it starts talking
            Finish_Node();
        }
// Build array of descriptors for the presented buttons
        private List<Dictionary<string, object>> BuildOptionsArray()
        {
            var list = new List<Dictionary<string, object>>();
            for (int shown = 0; shown < _presentedOrder.Count; shown++)
            {
                int orig = _presentedOrder[shown];
                var ch = choices[orig];
                list.Add(new Dictionary<string, object> {
                    { "id", $"{name}#{orig}" },
                    { "label", ch.label ?? "" },
                    { "category", ch.category ?? "" },
                    { "variant", ch.variant ?? "" },
                    { "text", DialogueNode.Insert_Stats_into_Text(ch.text ?? "") },
                    { "origIndex", orig },
                    { "shownIndex", shown }
                });
            }
            return list;
        }

        private Dictionary<string, object> OptionDescriptor(int origIndex)
        {
            // Map orig index to shown index for completeness
            int shownIndex = _presentedOrder.IndexOf(origIndex);
            var ch = choices[origIndex];
            return new Dictionary<string, object> {
                { "id", $"{name}#{origIndex}" },
                { "label", ch.label ?? "" },
                { "category", ch.category ?? "" },
                { "variant", ch.variant ?? "" },
                { "text", DialogueNode.Insert_Stats_into_Text(ch.text ?? "") },
                { "origIndex", origIndex },
                { "shownIndex", shownIndex }
            };
        }
        private Dictionary<string, object> CurrentTraitMap() {
            var map = new Dictionary<string, object>();
            foreach (var k in GateTraitsNode.AllTraitKeys())
                map[k] = StatsManager.Get_Numbered_Stat(k);
            return map;
        }private void SafeLogJson(string eventName, Dictionary<string, object> payload)
{
    try
    {
        if (Logging.Instance == null) return;
        string json = WriteJson(payload);
        Logging.Instance.BeginCustom(eventName);          // see #6
        Logging.Instance.ParamJson("payload", json);      // single packed blob
        // Optional: add a couple scalars for query-ability
        Logging.Instance.Param("conversation", GetComponentInParent<ConversationManager>()?.name ?? "");
        Logging.Instance.Param("node", name);
        Logging.Instance.SubmitCustom();
    }
    catch (System.Exception e)
    {
        Debug.LogWarning($"ShowChoiceNode log failed: {e.Message}");
    }
}

private static string WriteJson(object obj)
{
    // very small JSON writer to handle dictionaries/lists/scalars
    var sb = new System.Text.StringBuilder();
    void W(object o)
    {
        switch (o)
        {
            case null: sb.Append("null"); break;
            case string s: sb.Append('"').Append(s.Replace("\\","\\\\").Replace("\"","\\\"")
                                                  .Replace("\n","\\n").Replace("\r","\\r").Replace("\t","\\t")).Append('"'); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case int i: sb.Append(i); break;
            case float f: sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
            case double d: sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
            case IDictionary<string, object> dict:
                sb.Append('{'); bool first = true;
                foreach (var kv in dict) { if(!first) sb.Append(','); first=false; W(kv.Key); sb.Append(':'); W(kv.Value); }
                sb.Append('}'); break;
            case System.Collections.IEnumerable seq:
                sb.Append('['); first = true;
                foreach (var v in seq) { if(!first) sb.Append(','); first=false; W(v); }
                sb.Append(']'); break;
            default: sb.Append('"').Append(o.ToString()).Append('"'); break;
        }
    }
    W(obj);
    return sb.ToString();
}

        private void CleanupAndHide(bool restoreDialogueUI = true)
        {
            ClearAllChoiceButtons();
            UIManager.ui_manager.choice_panel.SetActive(false);                       // close panel :contentReference[oaicite:11]{index=11}
            if (hideDialogueUI && restoreDialogueUI)
                VNSceneManager.scene_manager.Show_UI(true);                           // restore dialogue UI :contentReference[oaicite:12]{index=12}
        }

        private static void ClearAllChoiceButtons()
        {
            var ui = UIManager.ui_manager;
            if (ui == null || ui.choice_buttons == null) return;
            for (int i = 0; i < ui.choice_buttons.Length; i++)
            {
                var b = ui.choice_buttons[i];
                if (b == null) continue;
                b.onClick.RemoveAllListeners();
                b.gameObject.SetActive(false);
            }
        }

        private static bool MeetsRequirements(List<FlexibleTraitRequirement> reqs)
        {
            if (reqs == null || reqs.Count == 0) return true;
            foreach (var r in reqs)
            {
                var key = r.ResolveKey();
                float current = StatsManager.Get_Numbered_Stat(key);
                if (!CompareNumber(current, r.compare, r.value)) return false;
            }
            return true;
        }

        private static bool CompareNumber(float current, NumberCompare op, float target)
        {
            switch (op)
            {
                case NumberCompare.GreaterThan:    return current >  target;
                case NumberCompare.GreaterOrEqual: return current >= target;
                case NumberCompare.Equal:          return Mathf.Approximately(current, target);
                case NumberCompare.LessOrEqual:    return current <= target;
                case NumberCompare.LessThan:       return current <  target;
                default: return false;
            }
        }

        public override void Button_Pressed() { /* no default submit */ }
    }
}
