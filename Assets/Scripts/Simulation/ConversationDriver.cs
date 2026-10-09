#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

    // One possible branch of a Gate*Node (AnswerCampus's state-driven branching nodes under
    // Assets/Scripts/Nodes/Gate*.cs, e.g. "is contact" / "not contact", or a specific
    // GateAffinityNode tier). Id is only meaningful within the GateEncounter that produced it.
    public class GateBranch
    {
        public int Id;
        public string Label;
    }

    // One Gate*Node encounter during a run -- the gate equivalent of ChoiceEncounter. Unlike a
    // ShowChoiceNode, a gate's branch is decided purely from game/save state (no player input,
    // no visible panel), so this is recorded via Node.OnBeforeNodeRuns (ConversationManager.cs)
    // rather than by watching UI, and "alternatives" are the branches NOT naturally taken.
    public class GateEncounter
    {
        public int EncounterIndex;
        public string GateType;
        public string ConversationName;
        public int TakenBranchId;
        public string TakenBranchLabel;
        public List<GateBranch> Alternatives = new List<GateBranch>();
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

        // Gate*Node scripting/recording -- same shape as the choice fields above, keyed by
        // their own encounter counter since gates and choices are independent event streams.
        // ScriptedGateOutcomes maps a gate encounter index to the GateBranch.Id to force once;
        // every other gate encounter in the run evaluates its real condition normally.
        public static Dictionary<int, int> ScriptedGateOutcomes { get; private set; }
        public static List<GateEncounter> GateRecording { get; private set; }
        public static string LastGateForcingMismatch { get; private set; }
        private static int _gateEncounterCounter;
        private static bool _gateHookSubscribed;

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
        public static void BeginNewRun(List<int> scriptedChoices, Dictionary<int, int> scriptedGateOutcomes = null)
        {
            ScriptedChoices = scriptedChoices;
            Recording = new List<ChoiceEncounter>();
            AllChosenIndices = new List<int>();
            _encounterCounter = 0;
            _nodeVisitCounts = new Dictionary<string, int>();

            ScriptedGateOutcomes = scriptedGateOutcomes;
            GateRecording = new List<GateEncounter>();
            LastGateForcingMismatch = null;
            _gateEncounterCounter = 0;
            EnsureGateHookSubscribed();
        }

        // Subscribes exactly once per domain lifetime. Play Mode has no domain reload between
        // the many runs PathExplorer drives within one test, so subscribing in BeginNewRun
        // directly would stack up a duplicate invocation per prior run.
        private static void EnsureGateHookSubscribed()
        {
            if (_gateHookSubscribed) return;
            _gateHookSubscribed = true;
            ConversationManager.OnBeforeNodeRuns += HandleBeforeNodeRuns;
        }

        // Observes (and sometimes fully replaces) every node right before it runs. Returns
        // true to skip the node's real Run_Node() entirely -- used only for NodeLaunchGroupStudy
        // (see BypassGroupStudy). For the six Gate*Node types (Assets/Scripts/Nodes/Gate*.cs),
        // records a GateEncounter and, if ScriptedGateOutcomes names this encounter, mutates
        // the backing game state right now so the node's own Run_Node() -- which still runs
        // normally afterward -- naturally evaluates to the forced branch. No-op (false) for
        // every other node type (dialogue, choices, etc. are handled elsewhere/normally).
        private static bool HandleBeforeNodeRuns(Node node)
        {
            if (node is NodeLaunchGroupStudy launchNode)
                return BypassGroupStudy(launchNode);

            if (node is NodeLaunchExam examNode)
                return BypassExam(examNode);

            var branchSet = DescribeGate(node);
            if (branchSet == null) return false;

            int encounterIndex = _gateEncounterCounter++;
            var cm = node.GetComponentInParent<ConversationManager>();
            string conversationName = cm != null ? cm.name : "<unknown>";

            if (ScriptedGateOutcomes != null && ScriptedGateOutcomes.TryGetValue(encounterIndex, out int forcedId))
            {
                ForceGateBranch(node, forcedId);
                var afterForcing = DescribeGate(node);
                if (afterForcing != null && afterForcing.TakenId != forcedId)
                {
                    // Known limitation (see PathExploration.md's own caveat text): forcing a
                    // GateAffinityNode branch can be overridden by an earlier same-character
                    // branch under first-match-wins semantics. Report the mismatch rather than
                    // silently mis-attributing whatever branch actually ran.
                    LastGateForcingMismatch =
                        $"Encounter {encounterIndex} ({branchSet.GateType} in '{conversationName}'): intended to " +
                        $"force branch {forcedId} ('{branchSet.AllBranches.FirstOrDefault(a => a.Id == forcedId)?.Label}') " +
                        $"but the node will take branch {afterForcing.TakenId} ('{afterForcing.TakenLabel}') instead.";
                }
                if (afterForcing != null) branchSet = afterForcing;
            }

            var entry = new GateEncounter
            {
                EncounterIndex = encounterIndex,
                GateType = branchSet.GateType,
                ConversationName = conversationName,
                TakenBranchId = branchSet.TakenId,
                TakenBranchLabel = branchSet.TakenLabel,
            };
            foreach (var b in branchSet.AllBranches)
                if (b.Id != branchSet.TakenId)
                    entry.Alternatives.Add(b);

            if (GateRecording != null) GateRecording.Add(entry);
            return false;
        }

        // NodeLaunchGroupStudy (Assets/Scripts/Nodes/NodeLaunchGroupStudy.cs) hands off to
        // GroupStudyManager/FivePositionsGameManager -- a real, input-driven minigame (falling
        // letters, a player-moved eraser) with no way for this automation to play it. Confirmed
        // to leave ConversationDriver's drive loop parked indefinitely: the node sets
        // go_to_next_node=false and the conversation only resumes when the minigame's own
        // EndGame() coroutine finishes, which needs real wall-clock gameplay. Mirrors
        // HomeHubPolicy's football bypass: skip the real minigame entirely, apply its Group-mode
        // completion stats directly, and jump straight to whatever conversation it would have
        // resumed into.
        private static bool BypassGroupStudy(NodeLaunchGroupStudy launchNode)
        {
            var groupStudyManager = launchNode.groupStudyManager ?? Object.FindAnyObjectByType<GroupStudyManager>();
            if (groupStudyManager == null) return false; // let the real node log its own "no manager" error

            ConversationManager endConversation = launchNode.endGroupStudyConversation
                ?? groupStudyManager.conversationManager
                ?? groupStudyManager.gameManager?.conversationManager;

            // Deterministic, not UnityEngine.Random -- same reasoning as HomeHubPolicy's
            // football bypass: a path-exploration divergence run reaching this call after a
            // different number of prior choices could get a different outcome purely from
            // RNG drift, contaminating the comparison.
            const float simulatedLongestStreak = 3f;
            StatsManager.Set_Numbered_Stat("GroupStudyLongestStreak", simulatedLongestStreak);
            StatsManager.Set_String_Stat("StudyGameScore", simulatedLongestStreak.ToString());

            VNSceneManager.scene_manager.Show_UI(true);
            if (endConversation != null)
                endConversation.Start_Conversation();
            else
                Debug.LogWarning("[ConversationDriver] BypassGroupStudy: no end conversation resolved; leaving conversation parked.");

            return true;
        }

        // NodeLaunchExam (Assets/Scripts/Nodes/NodeLaunchExam.cs) is the exam counterpart of
        // NodeLaunchGroupStudy -- it hands off to FivePositionsGameManager (same real,
        // input-driven minigame), sets go_to_next_node=false, and only resumes via
        // FivePositionsGameManager.EndGame() calling VNSceneManager.scene_manager
        // .Start_Conversation(pendingEndConversation) once the minigame finishes. Left
        // unbypassed, every week-6+ "Go to class" visit parks here forever: with
        // DriveUntilSceneIs's stuck-detector now able to actually notice (see the
        // madeProgress fix above), that surfaces as an endless Quit-to-Home-recover-and-retry
        // loop at the same exam gate instead of a silent hang -- still never making it past
        // the exam to any later content. Mirrors BypassGroupStudy: skip the real minigame,
        // record a deterministic (perfect, zero-strike) exam result via GradeCalculator, and
        // jump straight to the conversation the minigame would have resumed into.
        private static bool BypassExam(NodeLaunchExam launchNode)
        {
            string examId = launchNode.examId;
            if (!string.IsNullOrEmpty(examId))
                StatsManager.Set_String_Stat("CurrentExamId", examId);

            // Deterministic, not UnityEngine.Random -- same reasoning as BypassGroupStudy: a
            // path-exploration divergence run reaching this call after a different number of
            // prior choices should get the same grade, not RNG-drift contamination.
            int strikePool = launchNode.challengeProfile != null ? launchNode.challengeProfile.examStrikePool : 4;
            GradeCalculator.RecordExam(examId, 0, strikePool);

            VNSceneManager.scene_manager.Show_UI(true);
            if (launchNode.endExamConversation != null)
                launchNode.endExamConversation.Start_Conversation();
            else
                Debug.LogWarning("[ConversationDriver] BypassExam: no end conversation resolved; leaving conversation parked.");

            return true;
        }

        // Read-only peek at which branch a Gate*Node's own condition logic currently takes --
        // duplicated per gate type the same way MeetsRequirements/Compare below duplicate
        // ShowChoiceNode's private logic, since each Gate*Node's evaluation is private to it.
        // Returns null for a node this explorer doesn't track, or (GateGameNode only) one
        // configured with no real alternate branch (FootballCheckType.None).
        private class GateBranchSet
        {
            public string GateType;
            public int TakenId;
            public string TakenLabel;
            public List<GateBranch> AllBranches = new List<GateBranch>();
        }

        private static GateBranchSet DescribeGate(Node node)
        {
            switch (node)
            {
                case GateContactNode gcn:
                {
                    bool isContact = Friend.IsFriend(gcn.character);
                    var set = new GateBranchSet { GateType = "GateContactNode" };
                    set.AllBranches.Add(new GateBranch { Id = 0, Label = "is contact" });
                    set.AllBranches.Add(new GateBranch { Id = 1, Label = "not contact" });
                    set.TakenId = isContact ? 0 : 1;
                    set.TakenLabel = isContact ? "is contact" : "not contact";
                    return set;
                }

                case GateLastGameResultNode _:
                {
                    bool won = StatsManager.Get_Boolean_Stat("LastGame_Won");
                    var set = new GateBranchSet { GateType = "GateLastGameResultNode" };
                    set.AllBranches.Add(new GateBranch { Id = 0, Label = "won" });
                    set.AllBranches.Add(new GateBranch { Id = 1, Label = "lost" });
                    set.TakenId = won ? 0 : 1;
                    set.TakenLabel = won ? "won" : "lost";
                    return set;
                }

                case GateAffinityNode gan:
                {
                    var set = new GateBranchSet { GateType = "GateAffinityNode" };
                    int takenId = -1;
                    for (int i = 0; i < gan.branches.Count; i++)
                    {
                        var b = gan.branches[i];
                        if (b == null || b.character == Character.NONE) continue;
                        set.AllBranches.Add(new GateBranch
                        {
                            Id = i,
                            Label = $"branch[{i}]: {b.character}_affinity {b.compare} {b.threshold}",
                        });
                        if (takenId < 0)
                        {
                            float current = StatsManager.Get_Numbered_Stat(b.character.ToString() + "_affinity");
                            if (Compare(current, b.compare, b.threshold))
                                takenId = i;
                        }
                    }
                    int fallbackId = gan.branches.Count;
                    set.AllBranches.Add(new GateBranch { Id = fallbackId, Label = "fallback" });
                    set.TakenId = takenId >= 0 ? takenId : fallbackId;
                    set.TakenLabel = set.AllBranches.First(b => b.Id == set.TakenId).Label;
                    return set;
                }

                case GateTraitsNode gtn:
                {
                    bool passed = true;
                    foreach (var req in gtn.traitRequirements)
                    {
                        float current = StatsManager.Get_Numbered_Stat(req.ResolveKey());
                        if (!Compare(current, req.compare, req.value)) { passed = false; break; }
                    }
                    var set = new GateBranchSet { GateType = "GateTraitsNode" };
                    set.AllBranches.Add(new GateBranch { Id = 0, Label = "requirements met" });
                    if (gtn.traitRequirements != null && gtn.traitRequirements.Count > 0)
                        set.AllBranches.Add(new GateBranch { Id = 1, Label = "requirements not met" });
                    set.TakenId = passed ? 0 : 1;
                    set.TakenLabel = passed ? "requirements met" : "requirements not met";
                    return set;
                }

                case GateEventsNode gen:
                {
                    bool passed = true;
                    if (gen.eventRequirements != null)
                    {
                        foreach (var req in gen.eventRequirements)
                        {
                            if (req == null || string.IsNullOrEmpty(req.key)) continue;
                            bool completed = GameEvents.IsCustomEventCompleted(req.key);
                            if (req.check == EventCheckType.Completed && !completed) { passed = false; break; }
                            if (req.check == EventCheckType.NotCompleted && completed) { passed = false; break; }
                        }
                    }
                    var set = new GateBranchSet { GateType = "GateEventsNode" };
                    set.AllBranches.Add(new GateBranch { Id = 0, Label = "requirements met" });
                    if (gen.eventRequirements != null && gen.eventRequirements.Count > 0)
                        set.AllBranches.Add(new GateBranch { Id = 1, Label = "requirements not met" });
                    set.TakenId = passed ? 0 : 1;
                    set.TakenLabel = passed ? "requirements met" : "requirements not met";
                    return set;
                }

                case GateGameNode ggn:
                {
                    if (ggn.footballRequirement.check == FootballCheckType.None) return null;
                    bool passed = EvaluateFootballRequirementPeek(ggn.footballRequirement);
                    var set = new GateBranchSet { GateType = "GateGameNode" };
                    set.AllBranches.Add(new GateBranch { Id = 0, Label = "requirement met" });
                    set.AllBranches.Add(new GateBranch { Id = 1, Label = "requirement not met" });
                    set.TakenId = passed ? 0 : 1;
                    set.TakenLabel = passed ? "requirement met" : "requirement not met";
                    return set;
                }

                default:
                    return null;
            }
        }

        // Mirrors GateGameNode.EvaluateFootballRequirement/GetFootballRecord exactly (both
        // private there) -- same duplication pattern as MeetsRequirements/Compare below.
        private static bool EvaluateFootballRequirementPeek(FootballRequirement req)
        {
            switch (req.check)
            {
                case FootballCheckType.None:
                    return true;
                case FootballCheckType.IsWinningRecord:
                {
                    var r = GetFootballRecordPeek();
                    return r.wins > r.losses;
                }
                case FootballCheckType.WinsAtLeast:
                {
                    var r = GetFootballRecordPeek();
                    return r.wins >= Mathf.RoundToInt(req.threshold);
                }
                case FootballCheckType.WinRateAtLeast:
                {
                    var r = GetFootballRecordPeek();
                    return r.played > 0 && r.winRate >= req.threshold;
                }
                default:
                    return true;
            }
        }

        private static (int wins, int losses, int played, float winRate) GetFootballRecordPeek()
        {
            string json = StatsManager.Get_String_Stat("FootballSchedule");
            if (string.IsNullOrEmpty(json)) return (0, 0, 0, 0f);

            FootballGameListWrapper wrapper = null;
            try { wrapper = JsonUtility.FromJson<FootballGameListWrapper>(json); }
            catch { /* ignore */ }

            if (wrapper?.games == null || wrapper.games.Count == 0)
                return (0, 0, 0, 0f);

            int wins = wrapper.games.Count(g => g.played && g.won);
            int losses = wrapper.games.Count(g => g.played && !g.won);
            int played = wins + losses;
            float winRate = played > 0 ? (float)wins / played : 0f;

            return (wins, losses, played, winRate);
        }

        // Mutates whatever backing state a Gate*Node reads so its Run_Node(), running a
        // moment later in the same synchronous call chain, takes the given branch id.
        private static void ForceGateBranch(Node node, int branchId)
        {
            switch (node)
            {
                case GateContactNode gcn:
                    StatsManager.Set_Boolean_Stat(gcn.character.ToString() + "_is_friend", branchId == 0);
                    break;

                case GateLastGameResultNode _:
                    StatsManager.Set_Boolean_Stat("LastGame_Won", branchId == 0);
                    break;

                case GateAffinityNode gan:
                    ForceAffinityBranch(gan, branchId);
                    break;

                case GateTraitsNode gtn:
                    ForceTraitsBranch(gtn, branchId);
                    break;

                case GateEventsNode gen:
                    ForceEventsBranch(gen, branchId);
                    break;

                case GateGameNode ggn:
                    ForceFootballBranch(ggn, branchId);
                    break;
            }
        }

        private static void ForceAffinityBranch(GateAffinityNode gan, int branchId)
        {
            int fallbackId = gan.branches.Count;
            if (branchId == fallbackId)
            {
                var characters = new HashSet<Character>();
                foreach (var b in gan.branches)
                    if (b != null && b.character != Character.NONE) characters.Add(b.character);
                foreach (var c in characters)
                    StatsManager.Set_Numbered_Stat(c.ToString() + "_affinity", ValueViolatingAll(gan.branches, c));
                return;
            }

            if (branchId < 0 || branchId >= gan.branches.Count) return;
            var target = gan.branches[branchId];
            if (target == null || target.character == Character.NONE) return;
            StatsManager.Set_Numbered_Stat(target.character.ToString() + "_affinity",
                ValueSatisfying(target.compare, target.threshold));
        }

        private static void ForceTraitsBranch(GateTraitsNode gtn, int branchId)
        {
            if (gtn.traitRequirements == null || gtn.traitRequirements.Count == 0) return;

            if (branchId == 0)
            {
                foreach (var req in gtn.traitRequirements)
                    StatsManager.Set_Numbered_Stat(req.ResolveKey(), ValueSatisfying(req.compare, req.value));
            }
            else
            {
                // EvaluateTraitRequirements() short-circuits on the first failing requirement,
                // so failing just this one is sufficient regardless of the rest.
                var first = gtn.traitRequirements[0];
                StatsManager.Set_Numbered_Stat(first.ResolveKey(), ValueViolatingSingle(first.compare, first.value));
            }
        }

        private static void ForceEventsBranch(GateEventsNode gen, int branchId)
        {
            if (gen.eventRequirements == null || gen.eventRequirements.Count == 0) return;

            if (branchId == 0)
            {
                foreach (var req in gen.eventRequirements)
                {
                    if (req == null || string.IsNullOrEmpty(req.key)) continue;
                    GameEvents.MarkCustomEventCompleted(req.key, req.check == EventCheckType.Completed);
                }
            }
            else
            {
                var req = gen.eventRequirements.FirstOrDefault(r => r != null && !string.IsNullOrEmpty(r.key));
                if (req != null)
                    GameEvents.MarkCustomEventCompleted(req.key, req.check != EventCheckType.Completed);
            }
        }

        private static void ForceFootballBranch(GateGameNode ggn, int branchId)
        {
            bool forceMet = branchId == 0;
            var req = ggn.footballRequirement;
            var wrapper = new FootballGameListWrapper();

            switch (req.check)
            {
                case FootballCheckType.IsWinningRecord:
                    AddFootballGames(wrapper, forceMet ? 2 : 0, forceMet ? 0 : 2);
                    break;

                case FootballCheckType.WinsAtLeast:
                {
                    int neededWins = Mathf.Max(0, Mathf.RoundToInt(req.threshold));
                    int wins = forceMet ? neededWins : Mathf.Max(0, neededWins - 1);
                    AddFootballGames(wrapper, wins, 0);
                    break;
                }

                case FootballCheckType.WinRateAtLeast:
                {
                    const int totalGames = 4;
                    int wins = 0;
                    for (int w = 0; w <= totalGames; w++)
                    {
                        bool meets = (float)w / totalGames >= req.threshold;
                        if (meets == forceMet) { wins = w; break; }
                    }
                    AddFootballGames(wrapper, wins, totalGames - wins);
                    break;
                }

                default:
                    return;
            }

            StatsManager.Set_String_Stat("FootballSchedule", JsonUtility.ToJson(wrapper));
        }

        private static void AddFootballGames(FootballGameListWrapper wrapper, int wins, int losses)
        {
            int week = 3;
            for (int i = 0; i < wins; i++)
                wrapper.games.Add(new FootballGame { week = week++, opponent = new FootballTeam("Rival", "Mascot"), played = true, won = true });
            for (int i = 0; i < losses; i++)
                wrapper.games.Add(new FootballGame { week = week++, opponent = new FootballTeam("Rival", "Mascot"), played = true, won = false });
        }

        private static float ValueSatisfying(NumberCompare op, float threshold)
        {
            switch (op)
            {
                case NumberCompare.GreaterThan: return threshold + 1f;
                case NumberCompare.LessThan: return threshold - 1f;
                default: return threshold; // GreaterOrEqual / LessOrEqual / Equal
            }
        }

        private static float ValueViolatingSingle(NumberCompare op, float threshold)
        {
            switch (op)
            {
                case NumberCompare.GreaterThan:
                case NumberCompare.GreaterOrEqual:
                    return threshold - 1f;
                case NumberCompare.LessThan:
                case NumberCompare.LessOrEqual:
                    return threshold + 1f;
                default: // Equal
                    return threshold + 1f;
            }
        }

        // Picks a value failing every branch authored for one character in a GateAffinityNode
        // (used to force the "fallback" branch). Assumes authored tiers for a given character
        // share one comparison direction (documented as "top-to-bottom, first match wins" --
        // i.e. descending GreaterOrEqual tiers is the expected authoring shape); a character
        // mixing Greater* and Less* branches is a known limitation surfaced via
        // LastGateForcingMismatch rather than solved generically here.
        private static float ValueViolatingAll(List<AffinityBranch> branches, Character character)
        {
            float? minGreaterThreshold = null;
            float? maxLessThreshold = null;
            foreach (var b in branches)
            {
                if (b == null || b.character != character) continue;
                if (b.compare == NumberCompare.GreaterThan || b.compare == NumberCompare.GreaterOrEqual || b.compare == NumberCompare.Equal)
                    minGreaterThreshold = minGreaterThreshold.HasValue ? Mathf.Min(minGreaterThreshold.Value, b.threshold) : b.threshold;
                if (b.compare == NumberCompare.LessThan || b.compare == NumberCompare.LessOrEqual)
                    maxLessThreshold = maxLessThreshold.HasValue ? Mathf.Max(maxLessThreshold.Value, b.threshold) : b.threshold;
            }

            if (minGreaterThreshold.HasValue) return minGreaterThreshold.Value - 1f;
            if (maxLessThreshold.HasValue) return maxLessThreshold.Value + 1f;
            return 0f;
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
            ConversationManager lastKnownConversation = null;
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
                    // Deliberately NOT treated as progress by itself -- calling this on a node
                    // that ignores it (or that finished Run_Node() with go_to_next_node=false
                    // and is waiting on something this driver can't trigger) must still let the
                    // idle timeout below fire. Real progress is judged solely by whether
                    // cur_node/the active conversation actually changed.
                    cm.Button_Pressed();
                }

                if (cm != lastKnownConversation || (cm != null && cm.cur_node != lastKnownCurNode))
                {
                    lastKnownConversation = cm;
                    lastKnownCurNode = cm != null ? cm.cur_node : -1;
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
