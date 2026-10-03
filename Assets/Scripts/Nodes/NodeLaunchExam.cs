using UnityEngine;
using VNEngine;

public class NodeLaunchExam : Node
{
    [Header("Exam Context")]
    public string examId = "EXAM_MIDTERMS"; // or EXAM_FINALS
    public ChallengeProfile challengeProfile;

    [Header("Scene Objects")]
    public GameObject studyGameRoot; // the parent GameObject to enable
    public FivePositionsGameManager gameManager; // optional; auto-find if null

    [Header("Return Conversation")]
    public ConversationManager endExamConversation;

    public override void Run_Node()
    {
        if (studyGameRoot != null)
            studyGameRoot.SetActive(true);

        if (gameManager == null)
            gameManager = FindAnyObjectByType<FivePositionsGameManager>();

        if (gameManager == null)
        {
            Debug.LogError("[NodeLaunchExam] No FivePositionsGameManager found.");
            Finish_Node();
            return;
        }

        // Stamp which exam is being taken (used for routing after the minigame)
        if (!string.IsNullOrEmpty(examId))
            StatsManager.Set_String_Stat("CurrentExamId", examId);

        // Set pending configuration on the manager (no params)
        gameManager.pendingChallengeProfile = challengeProfile;
        gameManager.pendingEndConversation  = endExamConversation;

        // Launch deterministically (no params)
        gameManager.LaunchExam();
        go_to_next_node = false;
        Finish_Node();
    }
}