using System.Collections.Generic;
using UnityEngine;

namespace VNEngine
{
    [System.Serializable]
    public class AffinityBranch
    {
        public Character character;
        public NumberCompare compare = NumberCompare.GreaterOrEqual;
        public float threshold;
        public ConversationManager conversation; // null = continue current conversation
    }

    [AddComponentMenu("Game Object/VN Engine/Branching/Gate Affinity Node")]
    public class GateAffinityNode : Node
    {
        [Header("Branches (evaluated top-to-bottom, first match wins)")]
        public List<AffinityBranch> branches = new List<AffinityBranch>();

        [Header("Fallback (no branch matched)")]
        public ConversationManager fallbackConversation;
        public bool continueCurrentOnFallback = true;

        public override void Run_Node()
        {
            for (int i = 0; i < branches.Count; i++)
            {
                var b = branches[i];
                if (b == null || b.character == Character.NONE) continue;

                float current = StatsManager.Get_Numbered_Stat(b.character.ToString() + "_affinity");
                if (CompareNumber(current, b.compare, b.threshold))
                {
                    TakeBranch(b.conversation, continueIfNull: true);
                    return;
                }
            }

            TakeBranch(fallbackConversation, continueIfNull: continueCurrentOnFallback);
        }

        private void TakeBranch(ConversationManager target, bool continueIfNull)
        {
            if (target != null)
            {
                target.Start_Conversation();
                go_to_next_node = false;
            }
            else if (!continueIfNull)
            {
                go_to_next_node = false;
            }

            Finish_Node();
        }

        public override void Finish_Node()
        {
            StopAllCoroutines();
            base.Finish_Node();
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
    }
}
