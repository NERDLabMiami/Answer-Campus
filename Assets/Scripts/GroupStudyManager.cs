using UnityEngine;
using VNEngine;

public class GroupStudyManager : MonoBehaviour
{
    public ChallengeProfile[] studyProfiles;
    public FivePositionsGameManager gameManager;
    public ConversationManager conversationManager;
    public string postConversationKey; // optional

    public void StartStudySession(string characterName)
    {
        ChallengeProfile profile = GetProfile(characterName);
        if (profile == null)
        {
            Debug.LogError("No profile found for " + characterName);
            return;
        }

        Debug.Log($"Starting group study with {characterName}");
        gameManager.Initialize();
        gameManager.ConfigureChallenge(profile);
        gameManager.StartGame();
    }

    private ChallengeProfile GetProfile(string name)
    {
        foreach (var profile in studyProfiles)
        {
            if (profile.characterName.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return profile;
        }
        return null;
    }

    public void EndStudySession()
    {
        // Ensure UI is restored
        VNSceneManager.scene_manager.Show_UI(true);

        // Start conversation if assigned
        if (!string.IsNullOrEmpty(postConversationKey))
        {
            ConversationManager convo = VNSceneManager.scene_manager.starting_conversation;
            if (convo != null)
            {
                // Pass the stat into VNEngine's conversation variables if needed
                VNSceneManager.scene_manager.Start_Conversation(convo);
            }
            else
            {
                Debug.LogWarning($"Conversation '{postConversationKey}' not found in Resources.");
            }
        }
    }
}