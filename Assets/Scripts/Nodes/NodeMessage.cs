using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
namespace VNEngine
{
    public class NodeMessage : Node
    {
        public TextMessage textMessage;

        public int showAfterWeek = 0; // 0 => show immediately

        // Optional: link to StageRouteIndex so invite timing matches route unlock week.
        // No manual stage entry needed — stage is read from the character's progression stat,
        // which NodeCheckpointCharacterStage sets earlier in the same conversation graph.
        public StageRouteIndex stageRouteIndex;

        public override void Run_Node()
        {
            int currentWeek = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));

            // Derive unlockWeek from StageRouteIndex when this is a location invite.
            // Reads the character's current stage stat for the destination scene so no
            // explicit stage number is needed here.
            int routeUnlockWeek = 0;
            if (stageRouteIndex != null && textMessage != null && !string.IsNullOrEmpty(textMessage.location))
            {
                int currentStage = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat(
                    $"{textMessage.from} - {textMessage.location} - Stage"));
                var route = stageRouteIndex.FindRoute(textMessage.from, textMessage.location, currentStage);
                if (route != null) routeUnlockWeek = route.unlockWeek;
            }

            if (textMessage != null)
                textMessage.unlockWeek = Mathf.Max(showAfterWeek, routeUnlockWeek);

            List<TextMessage> messages = PlayerPrefsExtra.GetList<TextMessage>("messages", new List<TextMessage>());

            if (!messages.Contains(textMessage))
            {
                textMessage.unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                Debug.Log("New Text Message From : " + textMessage.from);
                messages.Add(textMessage);
                PlayerPrefsExtra.SetList("messages", messages);

                int effectiveUnlockWeek = textMessage.unlockWeek;
                if (effectiveUnlockWeek <= 0 || currentWeek >= effectiveUnlockWeek)
                    StatsManager.Set_Boolean_Stat("PhoneHasNewActivity", true);
            }
            else
            {
                Debug.Log("Duplicate Message From : " + textMessage.from);
            }

            Finish_Node();
        }
    }
}
