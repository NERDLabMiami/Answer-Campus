using UnityEngine;
using System.Collections.Generic;
using System.Linq; // for counts on football record

namespace VNEngine
{
    public enum Trait { Humor, Charisma, Empathy, Grades }
    public enum NumberCompare { GreaterThan, GreaterOrEqual, Equal, LessOrEqual, LessThan }

    [System.Serializable]
    public class TraitRequirement
    {
        public Trait trait;
        public NumberCompare compare = NumberCompare.GreaterOrEqual;
        public float value = 1f;     // e.g., Empathy >= 2
    }

    [System.Serializable]
    public class TraitDelta
    {
        public Trait trait;
        public float amount; // positive or negative; modifies current value
    }

    /// <summary>
    /// Gates a branch on core traits and/or football performance,
    /// then applies success/failure deltas and optionally jumps.
    /// </summary>
    public class GateTraitsNode : Node
    {
        [Header("Requirements (All must pass)")]
        public List<TraitRequirement> traitRequirements = new List<TraitRequirement>();
        public FootballRequirement footballRequirement = new FootballRequirement { check = FootballCheckType.None };

        [Header("On Success")]
        public List<TraitDelta> successDeltas = new List<TraitDelta>();
        public ConversationManager successConversation;  // <-- jump target (conversation)
        [TextArea] public string successLogMessage;
        public bool continueCurrentOnSuccess = false;    // if no successConversation, continue current

        [Header("On Failure")]
        public List<TraitDelta> failureDeltas = new List<TraitDelta>();
        public ConversationManager failureConversation;  // <-- jump target (conversation)
        [TextArea] public string failureLogMessage;
        public bool continueCurrentOnFailure = true; 
        public override void Run_Node()
        {
            bool passed = EvaluateTraitRequirements() && EvaluateFootballRequirement();

            if (passed)
            {
                ApplyDeltas(successDeltas);
                if (!string.IsNullOrEmpty(successLogMessage))
                    //VNSceneManager.scene_manager.Add_To_Log("System", successLogMessage);

                if (successConversation != null)
                {
                    successConversation.Start_Conversation();
                    go_to_next_node = false;  // do not auto-advance the current conversation
                    Finish_Node();
                    return;
                }
                else if (!continueCurrentOnSuccess)
                {
                    // If no target and not continuing, just stop advancing
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }
            }
            else
            {
                ApplyDeltas(failureDeltas);
                if (!string.IsNullOrEmpty(failureLogMessage))
//                    VNSceneManager.scene_manager.Add_To_Log("System", failureLogMessage);

                if (failureConversation != null)
                {
                    failureConversation.Start_Conversation();
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }
                else if (!continueCurrentOnFailure)
                {
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }
            }

            // Fallthrough: keep going in the current conversation
            Finish_Node();
        }


        // ------- REQUIREMENT EVALUATION -------

        private bool EvaluateTraitRequirements()
        {
            for (int i = 0; i < traitRequirements.Count; i++)
            {
                var req = traitRequirements[i];
                float current = GetTrait(req.trait);
                if (!CompareNumber(current, req.compare, req.value))
                    return false;
            }
            return true;
        }

        private bool EvaluateFootballRequirement()
        {
            switch (footballRequirement.check)
            {
                case FootballCheckType.None:
                    return true;

                case FootballCheckType.IsWinningRecord:
                {
                    var (wins, losses, played, winRate) = GetFootballRecord();
                    return wins > losses;
                }

                case FootballCheckType.WinsAtLeast:
                {
                    var (wins, _, __, ___) = GetFootballRecord();
                    return wins >= Mathf.RoundToInt(footballRequirement.threshold);
                }

                case FootballCheckType.WinRateAtLeast:
                {
                    var record = GetFootballRecord();
                    // If no games played, treat as not meeting threshold (designer intent is typically progress-based)
                    return record.played > 0 && record.winRate >= footballRequirement.threshold;
                }

                default:
                    return true;
            }
        }

        private (int wins, int losses, int played, float winRate) GetFootballRecord()
        {
            string json = StatsManager.Get_String_Stat("FootballSchedule");
            if (string.IsNullOrEmpty(json))
                return (0, 0, 0, 0f);

            FootballGameListWrapper wrapper = JsonUtility.FromJson<FootballGameListWrapper>(json);
            if (wrapper == null || wrapper.games == null)
                return (0, 0, 0, 0f);

            int wins = wrapper.games.Count(g => g.played && g.won);
            int losses = wrapper.games.Count(g => g.played && !g.won);
            int played = wins + losses;
            float winRate = (played > 0) ? (float)wins / played : 0f;
            return (wins, losses, played, winRate);
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

        // ------- EFFECTS -------

        private void ApplyDeltas(List<TraitDelta> deltas)
        {
            foreach (var d in deltas)
            {
                float current = GetTrait(d.trait);
                SetTrait(d.trait, current + d.amount);
            }
        }

        // ------- TRAIT HELPERS (string keys centralized here) -------

        private static string TraitKey(Trait t)
        {
            switch (t)
            {
                case Trait.Humor:    return "Humor";
                case Trait.Charisma: return "Charisma";
                case Trait.Empathy:  return "Empathy";
                case Trait.Grades:   return "Grades";
                default:             return t.ToString();
            }
        }

        private static float GetTrait(Trait t)
        {
            return StatsManager.Get_Numbered_Stat(TraitKey(t));
        }

        private static void SetTrait(Trait t, float value)
        {
            StatsManager.Set_Numbered_Stat(TraitKey(t), value);
        }

        public override void Finish_Node()
        {
            StopAllCoroutines();
            base.Finish_Node();
        }
    }
}
