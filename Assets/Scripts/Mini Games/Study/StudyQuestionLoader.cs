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
    [Tooltip("If true, Study modes pull all weeks up to the player's current Week stat. If false, study pulls all weeks in the JSON.")]
    public bool studyUsesUnlockedWeeksOnly = true;

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

        int currentWeek = GetCurrentWeekStat();

        // Load only the current week's questions so the primary word is always the exam word for this week
        var wk = all.FirstOrDefault(w => TryParseWeek(w.week) == currentWeek);
        currentQuestions = (wk?.questions ?? new List<QuestionAnswerPair>())
            .Where(q => q != null && IsValidAnswer(q.answer))
            .ToList();

        // Fallback: if this week has no questions, load all unlocked weeks
        if (currentQuestions.Count == 0)
        {
            var fallback = all.Where(w => TryParseWeek(w.week) <= currentWeek && TryParseWeek(w.week) > 0);
            currentQuestions = fallback
                .Where(w => w.questions != null)
                .SelectMany(w => w.questions)
                .Where(q => q != null && IsValidAnswer(q.answer))
                .ToList();
        }

        ResetAlreadyUsedFlags();
        Debug.Log($"[StudyQuestionLoader] Loaded {currentQuestions.Count} study questions (week={currentWeek}).");
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

        // 1) Primary path: collect isPrimary questions from weeks tagged with this exam's ID
        if (!string.IsNullOrWhiteSpace(examId))
        {
            var examWeeks = all.Where(w => string.Equals(w.examId, examId, StringComparison.OrdinalIgnoreCase));
            currentQuestions = examWeeks
                .Where(w => w.questions != null)
                .SelectMany(w => w.questions)
                .Where(q => q != null && q.isPrimary && IsValidAnswer(q.answer))
                .ToList();

            if (currentQuestions.Count > 0)
            {
                ResetAlreadyUsedFlags();
                Debug.Log($"[StudyQuestionLoader] Exam '{examId}': {currentQuestions.Count} word(s) loaded.");
                return;
            }

            // 2) Fallback: look for a week named exactly as the exam ID (old behavior)
            var byId = all.FirstOrDefault(w => string.Equals(w.week, examId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byId != null && byId.questions != null)
            {
                currentQuestions = byId.questions.Where(q => q != null && IsValidAnswer(q.answer)).ToList();
                ResetAlreadyUsedFlags();
                Debug.Log($"[StudyQuestionLoader] Loaded {currentQuestions.Count} exam questions for id='{examId}' (named week fallback).");
                return;
            }
        }

        // 3) Final fallback: current week number
        int currentWeek = GetCurrentWeekStat();
        if (currentWeek <= 0) currentWeek = 1;
        var wk = all.FirstOrDefault(w => TryParseWeek(w.week) == currentWeek);
        currentQuestions = (wk?.questions ?? new List<QuestionAnswerPair>())
            .Where(q => q != null && IsValidAnswer(q.answer))
            .ToList();

        ResetAlreadyUsedFlags();
        Debug.Log($"[StudyQuestionLoader] Loaded {currentQuestions.Count} exam questions (week fallback, week={currentWeek}).");
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
                string a = NormalizeAnswer(q.answer);
                if (string.IsNullOrEmpty(a)) { bandC.Add(q); continue; }

                int seen = GetStatInt(SeenKey(a));
                int succ = GetStatInt(SuccessKey(a));
                int fail = GetStatInt(FailKey(a));

                bool failedUnmastered = (fail > 0 && succ <= 0);
                bool unseen = (seen <= 0);

                if (failedUnmastered) bandA.Add(q);
                else if (unseen) bandB.Add(q);
                else bandC.Add(q);
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
