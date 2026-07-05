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
    public int strikesPerWord = 3;     // strikes allowed before that word fails
    public int maxWordAttempts = 5;    // total words (success or fail) before game ends; use 4 for exam profiles
    }