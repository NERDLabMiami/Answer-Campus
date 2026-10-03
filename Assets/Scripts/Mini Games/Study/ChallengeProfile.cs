using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ChallengeProfile", menuName = "GroupStudy/ChallengeProfile")]
public class ChallengeProfile : ScriptableObject
{
    public string characterName;
    public enum PromptType { Definitions, Questions }
    public PromptType promptType;
    public float preDropHangTime = 0.35f; // seconds a letter waits before falling
    public float minSpawnInterval = 1f;
    public float maxSpawnInterval = 2f;
    [Range(0f, 1f)] public float chanceOfCorrectLetter = 0.5f;
    public List<QuestionAnswerPair> customQuestions;
    public float timerDuration = 60f;
    [Header("No-Timer Rules")]
    [Tooltip("Group study: total wrong letters the whole session can absorb before it ends.")]
    public int sharedStrikePool = 5;
    [Tooltip("Exam: total strikes before striking out. Each strike costs 2.0 / pool GPA points.")]
    public int examStrikePool = 4;
    public int maxWordAttempts = 5;    // words in the session/exam
    [HideInInspector] public int strikesPerWord = 3; // deprecated: kept so existing assets deserialize
}
