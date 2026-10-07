using UnityEngine;

namespace VNEngine
{
    [AddComponentMenu("Game Object/VN Engine/Branching/Gate Contact Node")]
    public class GateContactNode : Node
    {
        [Header("Character to check")]
        public Character character;

        [Header("Branches (null = continue current conversation)")]
        public ConversationManager isContactConversation;   // already met / positive interaction on record
        public ConversationManager notContactConversation;  // hasn't met yet

        public override void Run_Node()
        {
            bool isContact = Friend.IsFriend(character);
            ConversationManager target = isContact ? isContactConversation : notContactConversation;
            TakeBranch(target);
        }

        private void TakeBranch(ConversationManager target)
        {
            if (target != null)
            {
                target.Start_Conversation();
                go_to_next_node = false;
            }

            Finish_Node();
        }

        public override void Button_Pressed()
        {
        }

        public override void Finish_Node()
        {
            StopAllCoroutines();

            base.Finish_Node();
        }
    }
}
