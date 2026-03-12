using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

namespace VNEngine
{
    [AddComponentMenu("Game Object/VN Engine/Branching/Show Choice Node")]
    public class ShowChoiceNode : Node
    {
        [System.Serializable] private class FootballGameListWrapper { public FootballGame[] games; }
        [System.Serializable] private class FootballGame { public bool played; public bool won; }

        [System.Serializable]
        public class Choice
        {
            [TextArea] public string text;
            public ConversationManager nextConversation;      // null => continue current
            [Header("Enable Requirements")]
            public bool useTraits = false;
            public bool useEvents = false;
            public bool useGame = false;

            [Header("Trait Requirements (ALL must pass)")]
            public List<TraitRequirement> traitRequirements = new();

            [Header("Event Requirements (ALL must pass)")]
            public List<EventRequirement> eventRequirements = new();

            [Header("Game Requirements (ALL must pass)")]
            public FootballRequirement footballRequirement = new FootballRequirement { check = FootballCheckType.None };
        }

        public List<Choice> choices = new List<Choice>();

        [Header("Presentation")]
        public bool hideDialogueUI = true; // default Answer Campus behavior

        private readonly List<Button> _activeButtons = new();

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
                if (MeetsAllRequirements(choices[i]))
                    visible.Add(i);
            }

            // 2) Always randomize order (Fisher–Yates)
            for (int i = 0; i < visible.Count; i++)
            {
                int j = Random.Range(i, visible.Count);
                (visible[i], visible[j]) = (visible[j], visible[i]);
            }

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

            // Animate & focus first active
            UIManager.ui_manager.AnimateChoiceButtons(_activeButtons);               // existing helper :contentReference[oaicite:6]{index=6}
            //if (_activeButtons.Count > 0)
            //{
            //    EventSystem.current.SetSelectedGameObject(null);
            //    EventSystem.current.SetSelectedGameObject(_activeButtons[0].gameObject);
           // }
        }
private bool MeetsAllRequirements(Choice c)
{
    if (c == null) return false;

    // TRAITS
    if (c.useTraits && c.traitRequirements != null && c.traitRequirements.Count > 0)
    {
        for (int i = 0; i < c.traitRequirements.Count; i++)
        {
            var r = c.traitRequirements[i];

            // Safety: if trait requirement is uninitialized, fail closed or skip.
            // I recommend fail closed for pedagogy: errors become visible quickly.
            float current = StatsManager.Get_Numbered_Stat(r.trait.ToString());
            if (!CompareNumber(current, r.compare, r.value))
                return false;
        }
    }

    // EVENTS
    if (c.useEvents && c.eventRequirements != null && c.eventRequirements.Count > 0)
    {
        for (int i = 0; i < c.eventRequirements.Count; i++)
        {
            var r = c.eventRequirements[i];
            if (r == null || string.IsNullOrEmpty(r.key))
                return false; // fail closed: author turned on Events but didn’t specify key

            bool completed = GameEvents.IsCustomEventCompleted(r.key);

            if (r.check == EventCheckType.Completed && !completed) return false;
            if (r.check == EventCheckType.NotCompleted && completed) return false;
        }
    }

    // GAME (football only for now)
    if (c.useGame && c.footballRequirement != null && c.footballRequirement.check != FootballCheckType.None)
    {
        var record = GetFootballRecord();

        switch (c.footballRequirement.check)
        {
            case FootballCheckType.IsWinningRecord:
                if (!(record.wins > record.losses)) return false;
                break;

            case FootballCheckType.WinsAtLeast:
                if (!(record.wins >= Mathf.RoundToInt(c.footballRequirement.threshold))) return false;
                break;

            case FootballCheckType.WinRateAtLeast:
                if (!(record.played > 0 && record.winRate >= c.footballRequirement.threshold)) return false;
                break;
        }
    }

    return true;
}

private (int wins, int losses, int played, float winRate) GetFootballRecord()
{
    string json = StatsManager.Get_String_Stat("FootballSchedule");
    if (string.IsNullOrEmpty(json)) return (0, 0, 0, 0f);

    FootballGameListWrapper wrapper = null;
    try { wrapper = JsonUtility.FromJson<FootballGameListWrapper>(json); }
    catch { }

    if (wrapper?.games == null || wrapper.games.Length == 0)
        return (0, 0, 0, 0f);

    int wins = 0, losses = 0;
    for (int i = 0; i < wrapper.games.Length; i++)
    {
        var g = wrapper.games[i];
        if (!g.played) continue;
        if (g.won) wins++; else losses++;
    }

    int played = wins + losses;
    float winRate = played > 0 ? (float)wins / played : 0f;
    return (wins, losses, played, winRate);
}

        private void OnChoice(int idx)
        {
            var c = choices[idx];

            // Jump? End current conversation then start target (same as ChoicesManager.Change_Conversation)
            if (c.nextConversation != null)
            {
                if (VNSceneManager.current_conversation != null)
                    VNSceneManager.current_conversation.Finish_Conversation();        // finish current :contentReference[oaicite:7]{index=7}
                c.nextConversation.Start_Conversation();                              // start target :contentReference[oaicite:8]{index=8}

                go_to_next_node = false;                                             // don’t auto-advance current node chain :contentReference[oaicite:9]{index=9}
                CleanupAndHide();
                Finish_Node();
                return;
            }

            // Continue in current conversation
            CleanupAndHide();
            base.Finish_Node();                                                      // advance to next node in current convo :contentReference[oaicite:10]{index=10}
        }

        private void CleanupAndHide()
        {
            ClearAllChoiceButtons();
            UIManager.ui_manager.choice_panel.SetActive(false);                       // close panel :contentReference[oaicite:11]{index=11}
            if (hideDialogueUI)
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

        private static bool MeetsRequirements(List<TraitRequirement> reqs)
        {
            if (reqs == null || reqs.Count == 0) return true;
            for (int i = 0; i < reqs.Count; i++)
            {
                var r = reqs[i];
                float current = StatsManager.Get_Numbered_Stat(r.trait.ToString());
                if (!CompareNumber(current, r.compare, r.value))
                    return false;
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
