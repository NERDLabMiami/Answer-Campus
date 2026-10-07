#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VNEngine;

namespace AnswerCampus.Simulation
{
    // Drives whatever VNEngine conversation is currently active, the same way a real
    // player does: mashing "continue" through dialogue (DialogueNode.Button_Pressed()
    // fast-forwards the text reveal on the first press, then advances on the next) and
    // picking the first currently-visible ShowChoiceNode option via the internal
    // SelectChoiceExternally seam (Assets/Scripts/Nodes/ShowChoiceNode.cs:170, built for
    // TimedChoiceNode -- no production code changes needed since this file compiles into
    // the same Assembly-CSharp assembly).
    public struct ChoiceAlternative
    {
        public int Index;
        public string Text;
    }

    // One ShowChoiceNode encounter that had 2+ visible, non-quit-looking options -- a real
    // divergence point for path exploration. Encounters with only one real option aren't
    // recorded here (nothing to diverge to), but still consume an entry in AllChosenIndices,
    // since that list must be indexable by the same global encounter counter ResolveChoiceIndex
    // uses to consult ScriptedChoices.
    public class ChoiceEncounter
    {
        public int EncounterIndex;
        public string ConversationName;
        public int ChosenIndex;
        public string ChosenText;
        public List<ChoiceAlternative> Alternatives = new List<ChoiceAlternative>();
    }

    public static class ConversationDriver
    {
        public static bool LastDriveSucceeded { get; private set; }
        public static string LastStuckDescription { get; private set; }
        public static bool LastUsedQuitToHomeRecovery { get; private set; }

        // True when a drive targeting "Home" instead found the active scene had become
        // "Main" -- the real game's own ending content legitimately loads the main menu
        // after its closing cutscene (confirmed via a LoadSceneNode logging "Switching
        // level: Main after playing cutscene..."), which is a different scene than every
        // other redirect this driver handles. Without recognizing this as success, the
        // driver would keep waiting for "Home" that's never coming, time out, and fall
        // into the Quit-to-Home recovery below -- which restores stats to whatever they
        // were the last time the player left Home (an arbitrary earlier week), silently
        // corrupting what should have been a clean "reached the ending" result into
        // "resumed a stale mid-game state." Confirmed as the exact cause of exactly that
        // symptom in testing.
        public static bool LastReachedMainMenu { get; private set; }

        // Scripting/recording state for path exploration (AnswerCampus.Simulation.PathExplorer).
        // Scoped to one full playthrough at a time -- call BeginNewRun() before each RunBody().
        public static List<int> ScriptedChoices { get; private set; }
        public static List<ChoiceEncounter> Recording { get; private set; }
        public static List<int> AllChosenIndices { get; private set; }
        private static int _encounterCounter;

        // Tracks how many times each distinct choice point has been landed on within this
        // run, keyed by content (conversation name + the visible choice texts) rather than
        // object/GetEntityId() identity -- a repeatable prompt (e.g. Library's "Group
        // Study": "Yes!" loops back to the same prompt, "On second thought..." leaves)
        // typically gets its GameObject destroyed and recreated each time the session
        // restarts, which would give an identity-keyed lookup a fresh "first landing" every
        // single time and never detect the repeat at all. Keying by content survives that
        // re-instantiation. Without this, a repeatable choice would otherwise always
        // re-pick nonQuit[0] ("Yes!") forever -- cur_node keeps changing each lap, so the
        // idle-timeout stuck detector never trips either. Default policy below rotates
        // through the visible options on each revisit instead of always repeating the
        // first, so a two-option stay/leave loop exits on its second landing.
        private static Dictionary<string, int> _nodeVisitCounts = new Dictionary<string, int>();

        // Starts a fresh playthrough's choice bookkeeping. Pass null for a normal (baseline)
        // run that always follows the default first-visible-non-quit policy; pass a list to
        // force those exact choice indices at the first N ShowChoiceNode encounters (by the
        // same global order every run naturally hits them in, given deterministic replay up
        // to that point), falling back to the default policy for every encounter after that.
        public static void BeginNewRun(List<int> scriptedChoices)
        {
            ScriptedChoices = scriptedChoices;
            Recording = new List<ChoiceEncounter>();
            AllChosenIndices = new List<int>();
            _encounterCounter = 0;
            _nodeVisitCounts = new Dictionary<string, int>();
        }

        // Steps the active conversation until the active scene becomes targetScene, or
        // idleTimeoutSeconds of real elapsed time pass with neither a scene change nor a
        // visible choice panel nor a Button_Pressed()-driven dialogue advance (stuck).
        // Wall-clock, not frame count: this PlayMode test runs frames far faster than normal
        // gameplay (confirmed Time.deltaTime as low as ~0.0007s/frame, ~1400fps, since
        // nothing throttles it to vsync), so a real-time-driven node (a WaitNode holding
        // cur_node constant throughout its delay, or any CutsceneOverlayController fade) can
        // easily outlast a frame-count budget sized for normal gameplay speeds.
        public static IEnumerator DriveUntilSceneIs(string targetScene, float idleTimeoutSeconds)
        {
            LastDriveSucceeded = false;
            LastStuckDescription = null;
            LastUsedQuitToHomeRecovery = false;
            LastReachedMainMenu = false;
            float idleStartTime = Time.realtimeSinceStartup;
            int lastKnownCurNode = -1;
            bool triedQuitToHomeRecovery = false;

            while (true)
            {
                string activeScene = SceneManager.GetActiveScene().name;
                if (activeScene == targetScene)
                {
                    LastDriveSucceeded = true;
                    yield break;
                }
                if (targetScene == "Home" && activeScene == "Main")
                {
                    LastDriveSucceeded = true;
                    LastReachedMainMenu = true;
                    yield break;
                }

                var cm = VNSceneManager.current_conversation;
                // Fully-qualified: a second, unrelated `UIManager` class (no namespace)
                // exists in Assets/Scripts/MiniGameUIManager.cs and shadows the bare name.
                var ui = VNEngine.UIManager.ui_manager;
                bool madeProgress = false;

                // A text-entry prompt (e.g. name entry) blocks everything else until
                // filled in. Neither node type in this codebase that looks purpose-built
                // for this (KeyboardEntry.cs, InputfieldToStat.cs) is actually wired into
                // any scene/prefab, so whatever implements it couldn't be identified
                // statically -- handle any active input field generically instead.
                if (TryAutoFillActiveInputField())
                {
                    madeProgress = true;
                }
                else if (ui != null && ui.choice_panel != null && ui.choice_panel.activeSelf && cm != null)
                {
                    var node = cm.Get_Current_Node();
                    if (node is ShowChoiceNode showChoice)
                    {
                        int idx = ResolveChoiceIndex(showChoice, cm.name);
                        if (idx >= 0)
                        {
                            showChoice.SelectChoiceExternally(idx);
                            madeProgress = true;
                        }
                    }
                }
                else if (cm != null && cm.active)
                {
                    // Not a choice -- mash "continue" to fast-forward/advance dialogue and
                    // any other node that responds to Button_Pressed(). Harmless for node
                    // types that ignore it (e.g. ShowChoiceNode.Button_Pressed() is a no-op).
                    cm.Button_Pressed();
                    madeProgress = true;
                }

                if (cm != null && cm.cur_node != lastKnownCurNode)
                {
                    lastKnownCurNode = cm.cur_node;
                    madeProgress = true;
                }

                if (madeProgress)
                {
                    idleStartTime = Time.realtimeSinceStartup;
                }
                else if (Time.realtimeSinceStartup - idleStartTime >= idleTimeoutSeconds)
                {
                    string sceneName = SceneManager.GetActiveScene().name;
                    string convName = cm != null ? cm.name : "<none>";
                    int nodeIdx = cm != null ? cm.cur_node : -1;

                    // Last resort, same as a real stuck player: every non-Home/non-minigame
                    // scene has a universal "Quit to Home" button (MenuOptions.QuitToHome(),
                    // wired into the shared dialogue-canvas prefab) for exactly this situation
                    // -- a conversation that finished with no further chain and no "Set Week"+
                    // "Cutscene Back Home" pair back to Home. Try it once before giving up, so
                    // one dead-ended chain doesn't halt exploration of everything after it. This
                    // is recorded (not silently swallowed) since needing it is itself a finding:
                    // real content that doesn't return home on its own.
                    if (!triedQuitToHomeRecovery && targetScene == "Home")
                    {
                        triedQuitToHomeRecovery = true;
                        LastUsedQuitToHomeRecovery = true;
                        HomeCutsceneController.RestoreDepartureSnapshot();
                        LocationRouter.Go("Home");
                        idleStartTime = Time.realtimeSinceStartup;
                        yield return null;
                        continue;
                    }

                    // Diagnostic dump: two prior hypotheses (PlayerPrefs leakage,
                    // Time.timeScale stuck at 0) were both ruled out by fixes that didn't
                    // change this reproducible failure, so capture hard data instead of
                    // reasoning about more hypotheticals.
                    string diag =
                        $"Time.timeScale={Time.timeScale} Time.frameCount={Time.frameCount} " +
                        $"Time.realtimeSinceStartup={Time.realtimeSinceStartup:F1} " +
                        $"HomeCutsceneController.Instance={(HomeCutsceneController.Instance == null ? "NULL" : "set")} " +
                        $"UIManager.ui_manager={(ui == null ? "NULL" : "set")} " +
                        $"VNSceneManager.scene_manager={(VNSceneManager.scene_manager == null ? "NULL" : "set")} " +
                        $"VNSceneManager.current_conversation={(VNSceneManager.current_conversation == null ? "NULL" : VNSceneManager.current_conversation.name)}";

                    LastStuckDescription =
                        $"No scene change, visible choice, or dialogue advance for {idleTimeoutSeconds}s. " +
                        $"scene='{sceneName}' conversation='{convName}' cur_node={nodeIdx}" +
                        (LastUsedQuitToHomeRecovery ? " (after attempting Quit-to-Home recovery)" : "") +
                        $" | DIAGNOSTIC: {diag}";
                    yield break;
                }

                yield return null;
            }
        }

        // Finds any active, empty, interactable text field (TMP_InputField or legacy
        // InputField) and fills it with a placeholder, then fires its submit event the
        // same way pressing Enter/tapping away does. Returns true if one was handled.
        private static bool TryAutoFillActiveInputField()
        {
            var tmpFields = Object.FindObjectsByType<TMP_InputField>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var field in tmpFields)
            {
                if (!field.isActiveAndEnabled || !field.interactable) continue;
                if (!string.IsNullOrEmpty(field.text)) continue;
                field.text = "Player";
                field.onEndEdit?.Invoke(field.text);
                return true;
            }

            var legacyFields = Object.FindObjectsByType<InputField>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var field in legacyFields)
            {
                if (!field.isActiveAndEnabled || !field.interactable) continue;
                if (!string.IsNullOrEmpty(field.text)) continue;
                field.text = "Player";
                field.onEndEdit?.Invoke(field.text);
                return true;
            }

            return false;
        }

        // Resolves which choice to pick at a ShowChoiceNode encounter: honors a scripted
        // override for this encounter's position if one is set and still valid (visible,
        // non-quit) right now, otherwise falls back to the default first-visible-non-quit
        // policy. Every encounter is recorded in AllChosenIndices (so later runs can replay
        // this exact sequence positionally); encounters with 2+ non-quit visible options are
        // additionally recorded in Recording as a real divergence point for path exploration.
        private static int ResolveChoiceIndex(ShowChoiceNode node, string conversationName)
        {
            var nonQuit = VisibleNonQuitIndices(node);
            int encounterIndex = _encounterCounter++;

            string nodeKey = ChoicePointKey(node, conversationName);
            int visitCount = _nodeVisitCounts.TryGetValue(nodeKey, out var vc) ? vc : 0;
            _nodeVisitCounts[nodeKey] = visitCount + 1;

            int chosen;
            if (ScriptedChoices != null && encounterIndex < ScriptedChoices.Count && nonQuit.Contains(ScriptedChoices[encounterIndex]))
            {
                chosen = ScriptedChoices[encounterIndex];
            }
            else
            {
                chosen = FirstVisibleChoiceIndex(node, nonQuit, visitCount);
            }

            if (AllChosenIndices != null) AllChosenIndices.Add(chosen);

            if (Recording != null && nonQuit.Count >= 2 && chosen >= 0)
            {
                var entry = new ChoiceEncounter
                {
                    EncounterIndex = encounterIndex,
                    ConversationName = conversationName,
                    ChosenIndex = chosen,
                    ChosenText = node.choices[chosen].text,
                };
                foreach (var i in nonQuit)
                    if (i != chosen)
                        entry.Alternatives.Add(new ChoiceAlternative { Index = i, Text = node.choices[i].text });
                Recording.Add(entry);
            }

            return chosen;
        }

        // Content fingerprint for a choice point: the conversation it lives in plus its
        // authored choice texts (stable regardless of which are currently visible/gated),
        // so repeated launches of the same authored prompt -- even via a brand-new
        // GameObject instance -- are recognized as "the same choice point" for the revisit
        // rotation above.
        private static string ChoicePointKey(ShowChoiceNode node, string conversationName)
        {
            var texts = new List<string>();
            foreach (var c in node.choices)
                texts.Add(c?.text ?? "");
            return conversationName + "|" + string.Join("|", texts);
        }

        private static List<int> VisibleIndices(ShowChoiceNode node)
        {
            var result = new List<int>();
            var choices = node.choices;
            for (int i = 0; i < choices.Count; i++)
                if (MeetsRequirements(choices[i].requirements))
                    result.Add(i);
            return result;
        }

        private static List<int> VisibleNonQuitIndices(ShowChoiceNode node)
        {
            var result = new List<int>();
            foreach (var i in VisibleIndices(node))
                if (!LooksLikeQuit(node.choices[i].text ?? ""))
                    result.Add(i);
            return result;
        }

        // Picks a visible choice whose text doesn't look like a quit/exit option (per
        // explicit direction: quit and save flows shouldn't be exercised by this kind of
        // test), falling back to a quit-looking choice only if it's the only visible
        // option. Rotates by visitCount across repeat landings on the same node instance
        // (first landing picks nonQuit[0] same as before) so a repeatable stay/leave loop
        // advances to a different option each time instead of repeating "stay" forever.
        private static int FirstVisibleChoiceIndex(ShowChoiceNode node, List<int> nonQuit, int visitCount)
        {
            if (nonQuit.Count > 0) return nonQuit[visitCount % nonQuit.Count];
            var all = VisibleIndices(node);
            return all.Count > 0 ? all[visitCount % all.Count] : -1;
        }

        private static bool LooksLikeQuit(string text)
        {
            string t = text.ToLowerInvariant();
            return t.Contains("quit") || t.Contains("exit game") || t.Contains("exit to") || t.Contains("close game");
        }

        // Mirrors ShowChoiceNode's own private MeetsRequirements/CompareNumber exactly
        // (Assets/Scripts/Nodes/ShowChoiceNode.cs) -- reimplemented here since those are
        // private (not internal), so even same-assembly code can't call them directly.
        private static bool MeetsRequirements(List<FlexibleTraitRequirement> reqs)
        {
            if (reqs == null || reqs.Count == 0) return true;
            foreach (var r in reqs)
            {
                float current = StatsManager.Get_Numbered_Stat(r.ResolveKey());
                if (!Compare(current, r.compare, r.value)) return false;
            }
            return true;
        }

        private static bool Compare(float current, NumberCompare op, float target)
        {
            switch (op)
            {
                case NumberCompare.GreaterThan: return current > target;
                case NumberCompare.GreaterOrEqual: return current >= target;
                case NumberCompare.Equal: return Mathf.Approximately(current, target);
                case NumberCompare.LessOrEqual: return current <= target;
                case NumberCompare.LessThan: return current < target;
                default: return false;
            }
        }
    }
}
#endif
