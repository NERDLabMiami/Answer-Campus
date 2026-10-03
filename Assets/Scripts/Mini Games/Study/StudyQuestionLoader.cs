using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VNEngine;

[Serializable]
public class QuestionWeek
{
    public string week;
    public string examId;
    public List<QuestionAnswerPair> questions;
}

[Serializable]
public class QuestionWeekList
{
    public List<QuestionWeek> weeks;
}

public enum GameMode { Solo, Group, Exam }

public class StudyQuestionLoader : MonoBehaviour
{
    [Header("Data")]
    public TextAsset questionsJSON;

    [Header("Mode")]
    public GameMode currentMode = GameMode.Solo;
    public bool useDefinitions = true;

    [Header("Study selection")]
    [Tooltip("If the player has never seen a word (or has failed it without mastering it), prefer those words.")]
    public bool preferUnseenOrUnmastered = true;

    [Tooltip("Optional: if set, only include questions whose answer is exactly this many letters.")]
    public int requiredAnswerLength = 5;

    [NonSerialized] public List<QuestionAnswerPair> currentQuestions = new List<QuestionAnswerPair>();

    // --- Public API ---------------------------------------------------------

    /// <summary>
    /// Call whenever mode changes or when Week changes.
    /// Exam mode should call LoadQuestionsForExam(); Study modes should call LoadQuestionsForStudy().
    /// </summary>
    public void LoadQuestionsForMode()
    {
        if (currentMode == GameMode.Exam) LoadQuestionsForExam();
        else LoadQuestionsForStudy();
    }

    public void LoadQuestionsForStudy()
    {
        var all = LoadAllWeeksFromJson();
        if (all == null || all.Count == 0)
        {
            currentQuestions = new List<QuestionAnswerPair>();
            return;
        }

        // Clamp to the range of weeks actually present in the data, so an out-of-range
        // "Week" stat (e.g. 0 during orientation, before week 1 begins) still resolves
        // to a real week's study sheet instead of finding nothing.
        var weekNumbers = all.Select(w => TryParseWeek(w.week)).Where(w => w > 0).ToList();
        int currentWeek = weekNumbers.Count > 0
            ? Mathf.Clamp(GetCurrentWeekStat(), weekNumbers.Min(), weekNumbers.Max())
            : GetCurrentWeekStat();

        if (currentMode == GameMode.Solo)
        {
            // Solo always reads strictly from the current week's study sheet. No
            // fallback to other weeks: an empty week just means an empty session.
            var wk = all.FirstOrDefault(w => TryParseWeek(w.week) == currentWeek);
            currentQuestions = (wk?.questions ?? new List<QuestionAnswerPair>())
                .Where(q => q != null && IsValidAnswer(q.answer))
                .ToList();

            if (currentQuestions.Count == 0)
                Debug.LogWarning($"[StudyQuestionLoader] No valid questions found for week {currentWeek}.");
        }
        else
        {
            // Group prefers past weeks' unseen/unmastered words; only once those are
            // exhausted does the current week's content become available too.
            var pastQuestions = all
                .Where(w => TryParseWeek(w.week) > 0 && TryParseWeek(w.week) < currentWeek && w.questions != null)
                .SelectMany(w => w.questions)
                .Where(q => q != null && IsValidAnswer(q.answer))
                .ToList();

            var currentWeekQuestions = all
                .Where(w => TryParseWeek(w.week) == currentWeek && w.questions != null)
                .SelectMany(w => w.questions)
                .Where(q => q != null && IsValidAnswer(q.answer))
                .ToList();

            currentQuestions = HasUnseenOrUnmastered(pastQuestions)
                ? pastQuestions
                : pastQuestions.Concat(currentWeekQuestions).ToList();
        }

        ResetAlreadyUsedFlags();
        Debug.Log($"[StudyQuestionLoader] Loaded {currentQuestions.Count} study questions (mode={currentMode}, week={currentWeek}).");
    }

    public void LoadQuestionsForExam()
    {
        var all = LoadAllWeeksFromJson();
        if (all == null || all.Count == 0)
        {
            currentQuestions = new List<QuestionAnswerPair>();
            return;
        }

        string examId = StatsManager.Get_String_Stat("CurrentExamId");
        bool isFinals = string.Equals(examId, "EXAM_FINALS", StringComparison.OrdinalIgnoreCase);
        int startWeek = isFinals ? 6 : 1;
        int endWeek = isFinals ? 11 : 7;

        // One question per week in range. Finals shares weeks 6-7 with midterms, so on
        // those weeks it picks a different (non-primary) question to avoid repeating
        // the exact word already tested on the midterm.
        currentQuestions = new List<QuestionAnswerPair>();
        for (int week = startWeek; week <= endWeek; week++)
        {
            var wk = all.FirstOrDefault(w => TryParseWeek(w.week) == week);
            var valid = (wk?.questions ?? new List<QuestionAnswerPair>())
                .Where(q => q != null && IsValidAnswer(q.answer))
                .ToList();

            if (valid.Count == 0)
            {
                Debug.LogWarning($"[StudyQuestionLoader] Week {week} has no usable exam question.");
                continue;
            }

            bool avoidMidtermOverlap = isFinals && week <= 7;
            QuestionAnswerPair chosen = avoidMidtermOverlap
                ? valid.FirstOrDefault(q => !q.isPrimary) ?? valid.FirstOrDefault(q => q.isPrimary)
                : valid.FirstOrDefault(q => q.isPrimary) ?? valid.First();

            currentQuestions.Add(chosen);
        }

        ResetAlreadyUsedFlags();
        Debug.Log($"[StudyQuestionLoader] Exam '{examId}': {currentQuestions.Count} word(s) loaded (weeks {startWeek}-{endWeek}).");
    }

    public QuestionAnswerPair GetRandomQuestion()
    {
        if (currentQuestions == null || currentQuestions.Count == 0)
            return new QuestionAnswerPair { question = "Missing data", answer = "error", definition = "" };

        // If everything was used, reset.
        var available = currentQuestions.Where(q => q != null && !q.alreadyUsed).ToList();
        if (available.Count == 0)
        {
            ResetAlreadyUsedFlags();
            available = currentQuestions.Where(q => q != null && !q.alreadyUsed).ToList();
        }

        QuestionAnswerPair chosen;

        // Study modes: always present the primary (exam) word first if it hasn't been used yet
        if (currentMode != GameMode.Exam)
        {
            var primary = available.FirstOrDefault(q => q.isPrimary);
            if (primary != null)
            {
                primary.alreadyUsed = true;
                MarkSeen(primary.answer);
                return primary;
            }
        }

        if (!preferUnseenOrUnmastered)
        {
            chosen = available[UnityEngine.Random.Range(0, available.Count)];
        }
        else
        {
            // Ranking:
            // 1) failed-without-success (unmastered) words
            // 2) unseen words
            // 3) everything else
            // Within band, random selection.

            var bandA = new List<QuestionAnswerPair>();
            var bandB = new List<QuestionAnswerPair>();
            var bandC = new List<QuestionAnswerPair>();

            foreach (var q in available)
            {
                switch (ClassifyBand(q))
                {
                    case SeenBand.FailedUnmastered: bandA.Add(q); break;
                    case SeenBand.Unseen: bandB.Add(q); break;
                    default: bandC.Add(q); break;
                }
            }

            if (bandA.Count > 0) chosen = bandA[UnityEngine.Random.Range(0, bandA.Count)];
            else if (bandB.Count > 0) chosen = bandB[UnityEngine.Random.Range(0, bandB.Count)];
            else chosen = bandC[UnityEngine.Random.Range(0, bandC.Count)];
        }

        chosen.alreadyUsed = true;
        MarkSeen(chosen.answer);
        return chosen;
    }

    public string GetDisplayPrompt(QuestionAnswerPair pair, bool useDefinitions)
        => useDefinitions ? pair.definition : pair.question;

    // --- Progress tracking --------------------------------------------------

    public void MarkSeen(string answer)
    {
        string a = NormalizeAnswer(answer);
        if (string.IsNullOrEmpty(a)) return;
        IncStatInt(SeenKey(a));
    }

    public void MarkSuccess(string answer)
    {
        string a = NormalizeAnswer(answer);
        if (string.IsNullOrEmpty(a)) return;
        IncStatInt(SuccessKey(a));
    }

    public void MarkFail(string answer)
    {
        string a = NormalizeAnswer(answer);
        if (string.IsNullOrEmpty(a)) return;
        IncStatInt(FailKey(a));
    }

    // --- Internals ----------------------------------------------------------

    private enum SeenBand { FailedUnmastered, Unseen, Known }

    private SeenBand ClassifyBand(QuestionAnswerPair q)
    {
        string a = NormalizeAnswer(q.answer);
        if (string.IsNullOrEmpty(a)) return SeenBand.Known;

        int seen = GetStatInt(SeenKey(a));
        int succ = GetStatInt(SuccessKey(a));
        int fail = GetStatInt(FailKey(a));

        if (fail > 0 && succ <= 0) return SeenBand.FailedUnmastered;
        if (seen <= 0) return SeenBand.Unseen;
        return SeenBand.Known;
    }

    /// <summary>True if any question in the pool is unseen or failed-without-success.</summary>
    private bool HasUnseenOrUnmastered(IEnumerable<QuestionAnswerPair> pool)
        => pool.Any(q => q != null && ClassifyBand(q) != SeenBand.Known);

    private List<QuestionWeek> LoadAllWeeksFromJson()
    {
        if (questionsJSON == null || string.IsNullOrWhiteSpace(questionsJSON.text))
        {
            Debug.LogError("[StudyQuestionLoader] questionsJSON is missing.");
            return null;
        }

        try
        {
            var list = JsonUtility.FromJson<QuestionWeekList>(questionsJSON.text);
            return list?.weeks ?? new List<QuestionWeek>();
        }
        catch (Exception e)
        {
            Debug.LogError($"[StudyQuestionLoader] Failed to parse JSON: {e.Message}");
            return null;
        }
    }

    private void ResetAlreadyUsedFlags()
    {
        if (currentQuestions == null) return;
        foreach (var q in currentQuestions)
            if (q != null) q.alreadyUsed = false;
    }

    private static int TryParseWeek(string week)
        => int.TryParse(week, out var w) ? w : 0;

    private static string NormalizeAnswer(string answer)
        => string.IsNullOrWhiteSpace(answer) ? "" : answer.Trim().ToLowerInvariant();

    private bool IsValidAnswer(string answer)
    {
        string a = NormalizeAnswer(answer);
        if (string.IsNullOrEmpty(a)) return false;
        if (!a.All(char.IsLetter)) return false;
        if (requiredAnswerLength > 0 && a.Length != requiredAnswerLength) return false;
        return true;
    }

    private static int GetCurrentWeekStat()
    {
        // Support both keys (some scenes used "Week", others used "current_week").
        float w = StatsManager.Get_Numbered_Stat("Week");
        if (w <= 0) w = StatsManager.Get_Numbered_Stat("current_week");
        return Mathf.RoundToInt(w);
    }

    // Stats keys are scoped to avoid collisions with other systems.
    private const string Prefix = "TKAM_STUDY_";
    private static string SeenKey(string a) => $"{Prefix}{a}_SEEN";
    private static string SuccessKey(string a) => $"{Prefix}{a}_SUCCESS";
    private static string FailKey(string a) => $"{Prefix}{a}_FAIL";

    private static int GetStatInt(string key)
        => Mathf.RoundToInt(StatsManager.Get_Numbered_Stat(key));

    private static void IncStatInt(string key)
    {
        int v = GetStatInt(key);
        StatsManager.Set_Numbered_Stat(key, v + 1);
    }
}
