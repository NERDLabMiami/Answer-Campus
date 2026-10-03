using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VNEngine
{
    public class PressAnyKeyToStartConversation : MonoBehaviour
    {
        public ConversationManager conversation_to_start;


        void Update()
        {
            if (LegacyInputCompat.AnyKeyDown())
            {
                conversation_to_start.Start_Conversation();
                Destroy(this);
            }
        }
    }
}