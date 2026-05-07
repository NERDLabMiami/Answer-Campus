using System.Collections.Generic;
using UnityEngine;

namespace VNEngine
{
    [AddComponentMenu("Game Object/VN Engine/Branching/Gate Events Node")]
    public class GateEventsNode : Node
    {
        [Header("Requirements (ALL must pass)")]
        public List<EventRequirement> eventRequirements = new();

        [Header("On Success")]
        public ConversationManager successConversation;
        public bool continueCurrentOnSuccess = false;

        [Header("On Failure")]
        public ConversationManager failureConversation;
        public bool continueCurrentOnFailure = true;

        public override void Run_Node()
        {
            bool passed = EvaluateEventRequirements();

            if (passed)
            {
                if (successConversation != null)
                {
                    successConversation.Start_Conversation();
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }

                if (!continueCurrentOnSuccess)
                {
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }
            }
            else
            {
                if (failureConversation != null)
                {
                    failureConversation.Start_Conversation();
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }

                if (!continueCurrentOnFailure)
                {
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }
            }

            Finish_Node();
        }

        private bool EvaluateEventRequirements()
        {
            if (eventRequirements == null || eventRequirements.Count == 0)
                return true;

            for (int i = 0; i < eventRequirements.Count; i++)
            {
                var req = eventRequirements[i];
                if (req == null || string.IsNullOrEmpty(req.key))
                    continue;
                bool completed = GameEvents.IsCustomEventCompleted(req.key);

                if (req.check == EventCheckType.Completed && !completed)
                    return false;

                if (req.check == EventCheckType.NotCompleted && completed)
                    return false;
            }

            return true;
        }
    }
}
