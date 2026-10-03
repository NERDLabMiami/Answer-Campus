using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace VNEngine
{
    public enum Trait { Humor, Charisma, Empathy, Grades }
    public enum NumberCompare { GreaterThan, GreaterOrEqual, Equal, LessOrEqual, LessThan }


// GateTraitsNode.cs (add alongside existing types)
    [System.Serializable]
    public class FlexibleTraitRequirement
    {
        // Back-compat path:
        public Trait enumTrait = Trait.Humor; // existing enum

        // New flexible path (preferred):
        public string traitKey;               // if non-empty, use this

        public NumberCompare compare = NumberCompare.GreaterOrEqual;
        public float value = 1f;
        public string ResolveKey()
        {
            if (!string.IsNullOrEmpty(traitKey)) return traitKey;
            return GateTraitsNode.TraitKey(enumTrait); // existing mapper
        }
    }

    public enum FootballCheckType
    {
        None,               // ignore football
        IsWinningRecord,    // wins > losses
        WinsAtLeast,        // wins >= threshold
        WinRateAtLeast      // wins/played >= threshold (0..1)
    }

    [System.Serializable]
    public class FootballRequirement
    {
        public FootballCheckType check = FootballCheckType.None;
        public float threshold = 0f; // used for WinsAtLeast or WinRateAtLeast
    }

    [System.Serializable]
    public class TraitDelta
    {
        public Trait trait = Trait.Humor;   // legacy back-compat (field name preserved for existing prefab data)
        public string traitKey;             // preferred
        public float amount; // positive or negative; modifies current value

        public string ResolveKey() => string.IsNullOrEmpty(traitKey) ? GateTraitsNode.TraitKey(trait) : traitKey;
    }

    /// <summary>
    /// Gates a branch on core traits, then applies success/failure deltas and optionally jumps.
    /// </summary>
    public class GateTraitsNode : Node
    {
        [Header("Requirements (All must pass)")]
        public List<FlexibleTraitRequirement> traitRequirements = new();
        [SerializeField] private TraitRegistry traitRegistry;

        [Header("Trait Requirements Met")]
        public List<TraitDelta> successDeltas = new List<TraitDelta>();
        // Mutually exclusive: continue the current conversation, or jump to successConversation.
        public bool continueCurrentOnSuccess = true;
        public ConversationManager successConversation;  // used only when continueCurrentOnSuccess is false

        [Header("Trait Requirements Not Met")]
        public List<TraitDelta> failureDeltas = new List<TraitDelta>();
        // Mutually exclusive: continue the current conversation, or jump to failureConversation.
        public bool continueCurrentOnFailure = true;
        public ConversationManager failureConversation;  // used only when continueCurrentOnFailure is false

        [Header("Advanced")]
        public bool logOutcome = false;  // logs "Trait Requirements Met/Not Met" when this node runs

        private void Reset()
        {
            if (traitRegistry == null) traitRegistry = TraitRegistry.Load();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying && traitRegistry == null) traitRegistry = TraitRegistry.Load();
        }
#endif

        public override void Run_Node()
        {
            bool passed = EvaluateTraitRequirements();

            if (passed)
            {
                ApplyDeltas(successDeltas);
                if (logOutcome)
                    VNSceneManager.scene_manager.Add_To_Log("System", "Trait Requirements Met");

                if (!continueCurrentOnSuccess && successConversation != null)
                {
                    successConversation.Start_Conversation();
                    go_to_next_node = false;  // do not auto-advance the current conversation
                    Finish_Node();
                    return;
                }
            }
            else
            {
                ApplyDeltas(failureDeltas);
                if (logOutcome)
                    VNSceneManager.scene_manager.Add_To_Log("System", "Trait Requirements Not Met");

                if (!continueCurrentOnFailure && failureConversation != null)
                {
                    failureConversation.Start_Conversation();
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }
            }

            // Fallthrough: continue the current conversation
            Finish_Node();
        }


        // ------- REQUIREMENT EVALUATION -------

        private bool EvaluateTraitRequirements()
        {
            for (int i = 0; i < traitRequirements.Count; i++)
            {
                var req = traitRequirements[i];
                float current = StatsManager.Get_Numbered_Stat(req.ResolveKey());
                if (!CompareNumber(current, req.compare, req.value))
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

        // ------- EFFECTS -------

        private void ApplyDeltas(List<TraitDelta> deltas)
        {
            foreach (var d in deltas)
            {
                string key = d.ResolveKey();
                float current = StatsManager.Get_Numbered_Stat(key);
                StatsManager.Set_Numbered_Stat(key, current + d.amount);
            }
        }

        // ------- TRAIT HELPERS (string keys centralized here) -------

        public static IEnumerable<string> AllTraitKeys()
            => System.Enum.GetValues(typeof(Trait)).Cast<Trait>().Select(TraitKey);
        public static string TraitKey(Trait t)
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

        public override void Finish_Node()
        {
            StopAllCoroutines();
            base.Finish_Node();
        }
    }
}
