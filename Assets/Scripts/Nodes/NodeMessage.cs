using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
namespace VNEngine
{
    public class NodeMessage : Node
    {
        public TextMessage textMessage;

        // Optional: link to StageRouteIndex so invite timing matches route unlock week.
        // No manual stage entry needed — stage is read from the character's progression stat,
        // which NodeCheckpointCharacterStage sets earlier in the same conversation graph.
        public StageRouteIndex stageRouteIndex;

        public override void Run_Node()
        {
            int currentWeek = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));

            // A `location` by itself is just an optional navigation target for a quick
            // reply (TextThreadPanel navigates there on accept) - it says nothing about
            // timing. Only `locationForTimingOnly` means this location represents a real
            // scheduled location-opening that this message's availability should be
            // gated on; everything else (no location, or a location that's just a "go
            // there if you want" shortcut) is a live conversational message, interactive
            // as soon as this node runs, no week-gating at all.
            int computedUnlockWeek = 0;
            if (textMessage != null && textMessage.locationForTimingOnly && !string.IsNullOrEmpty(textMessage.location))
            {
                if (stageRouteIndex != null)
                {
                    int currentStage = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat(
                        $"{textMessage.from} - {textMessage.location} - Stage"));
                    // GetNextRoute (already used by NodeCheckpointCharacterStage for the
                    // same purpose) finds the next not-yet-reached stage, so it resolves
                    // correctly even at the default, never-touched stage of 0 - no prior
                    // checkpoint needs to have run first.
                    var route = stageRouteIndex.GetNextRoute(textMessage.from, textMessage.location, currentStage);
                    if (route != null)
                        computedUnlockWeek = route.unlockWeek;
                    else
                        Debug.LogWarning($"[NodeMessage] {textMessage.from}'s message marks '{textMessage.location}' as timing-relevant but has no StageRouteIndex route beyond stage {currentStage} - unlocking immediately.");
                }
                else
                {
                    Debug.LogWarning($"[NodeMessage] {textMessage.from}'s message marks '{textMessage.location}' as timing-relevant but has no StageRouteIndex assigned - unlocking immediately.");
                }
            }
            else if (textMessage != null && stageRouteIndex != null && !string.IsNullOrEmpty(textMessage.location))
            {
                Debug.LogWarning($"[NodeMessage] {textMessage.from}'s message has a StageRouteIndex assigned but 'Location For Timing Only' is unchecked, so it's ignored - this message unlocks immediately and '{textMessage.location}' is used only as the quick-reply navigation target.");
            }

            if (textMessage != null)
                textMessage.unlockWeek = computedUnlockWeek;

            // This node's `textMessage` is a persistent scene object that is never
            // recreated as "already answered" - scene reloads restore it fresh, and
            // a conversation can revisit this same node (e.g. a game-event message
            // that fires again on a later talk to the character this node is
            // attached to). A dedupe check against the current "messages" list alone
            // isn't durable against that, since it only proves this exact entry is
            // *currently* stored, not that it was ever truly delivered - so track
            // delivery with its own persistent stat instead, and store a deep copy
            // rather than this component's own live field, which quick replies never
            // mutate (they only edit separately-deserialized copies from GetAll()).
            string sentKey = $"MsgSent|{textMessage.from}|{textMessage.location}|{textMessage.body}";

            if (!StatsManager.Get_Boolean_Stat(sentKey))
            {
                List<TextMessage> messages = PlayerPrefsExtra.GetList<TextMessage>("messages", new List<TextMessage>());

                var stored = JsonUtility.FromJson<TextMessage>(JsonUtility.ToJson(textMessage));
                stored.unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                Debug.Log("New Text Message From : " + textMessage.from);
                messages.Add(stored);
                PlayerPrefsExtra.SetList("messages", messages);
                StatsManager.Set_Boolean_Stat(sentKey, true);

                // Don't flag a notification for a message the player can't see yet -
                // the Phone hides threads from senders who aren't contacts (see
                // TextThreads.GetThread/FriendsView.Render). It'll surface on its
                // own, with no missed notification needed, once they're added via
                // NodeContact.
                int effectiveUnlockWeek = textMessage.unlockWeek;
                if (Friend.IsFriend(textMessage.from) &&
                    (effectiveUnlockWeek <= 0 || currentWeek >= effectiveUnlockWeek))
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
