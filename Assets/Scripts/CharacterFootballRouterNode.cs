using System.Collections.Generic;
using UnityEngine;
using VNEngine;

[System.Serializable]
public class FootballPostGameConversation
{
    public Character character;
    public ConversationManager conversation;
}

public class CharacterFootballRouterNode : Node
{
    [SerializeField] List<FootballPostGameConversation> firstTimeConversations;
    [SerializeField] ConversationManager repeatablePostGameFallback;

    public override void Run_Node()
    {
        foreach (var entry in firstTimeConversations)
        {
            if (entry.conversation == null) continue;

            bool invited = StatsManager.Get_Boolean_Stat(entry.character.ToString().ToLower() + "_football_invite_accepted");
            bool seen    = StatsManager.Get_Boolean_Stat(entry.character.ToString().ToLower() + "_football_post_game_seen");

            if (!invited || seen) continue;

            StatsManager.Set_Boolean_Stat(entry.character.ToString().ToLower() + "_football_post_game_seen", true);
            go_to_next_node = false;
            entry.conversation.Start_Conversation();
            Finish_Node();
            return;
        }

        go_to_next_node = false;
        if (repeatablePostGameFallback != null)
            repeatablePostGameFallback.Start_Conversation();
        Finish_Node();
    }
}
