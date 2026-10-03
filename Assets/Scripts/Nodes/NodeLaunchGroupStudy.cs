using UnityEngine;
using VNEngine;

public class NodeLaunchGroupStudy : Node
{
    [Header("Group Study Context")]
    public string characterName = "Beau";

    [Header("Scene Objects")]
    public GameObject studyGameRoot; // the parent GameObject to enable
    public GroupStudyManager groupStudyManager; // optional; auto-find if null

    [Header("Return Conversation")]
    public ConversationManager endGroupStudyConversation; // optional override

    public override void Run_Node()
    {
        VNSceneManager.scene_manager.Show_UI(false);

        if (studyGameRoot != null)
            studyGameRoot.SetActive(true);

        if (groupStudyManager == null)
            groupStudyManager = FindAnyObjectByType<GroupStudyManager>();

        if (groupStudyManager == null)
        {
            Debug.LogError("[NodeLaunchGroupStudy] No GroupStudyManager found.");
            Finish_Node();
            return;
        }

        if (endGroupStudyConversation != null && groupStudyManager.gameManager != null)
            groupStudyManager.gameManager.conversationManager = endGroupStudyConversation;

        groupStudyManager.StartStudySession(characterName);

        go_to_next_node = false;
        Finish_Node();
    }
}
