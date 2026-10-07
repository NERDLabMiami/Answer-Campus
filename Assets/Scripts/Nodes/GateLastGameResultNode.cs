using UnityEngine;

namespace VNEngine
{
    [AddComponentMenu("Game Object/VN Engine/Branching/Gate Last Game Result")]
    public class GateLastGameResultNode : Node
    {
        [Header("On Win")]
        public ConversationManager wonConversation;
        public bool continueCurrentOnWin = false;

        [Header("On Loss")]
        public ConversationManager lostConversation;
        public bool continueCurrentOnLose = true;

        public override void Run_Node()
        {
            bool won = StatsManager.Get_Boolean_Stat("LastGame_Won");

            if (won)
            {
                if (wonConversation != null)
                {
                    wonConversation.Start_Conversation();
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }

                if (!continueCurrentOnWin)
                {
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }
            }
            else
            {
                if (lostConversation != null)
                {
                    lostConversation.Start_Conversation();
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }

                if (!continueCurrentOnLose)
                {
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }
            }

            Finish_Node();
        }
    }
}
