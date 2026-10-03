using UnityEngine;
using VNEngine;

/// <summary>
/// Single source of truth for the class grade. Two exams, each worth half (0-2.0),
/// so a failed midterm plus an ace final still lands on a C (2.0).
/// </summary>
public static class GradeCalculator
{
    public const float ExamHalf = 2f;

    public const string MidtermId = "EXAM_MIDTERMS";
    public const string FinalId   = "EXAM_FINALS";

    const string MidtermScoreKey = "MidtermScore";
    const string FinalScoreKey   = "FinalScore";
    const string MidtermLetterKey = "MidtermLetterGrade";
    const string FinalLetterKey   = "FinalLetterGrade";
    const string MidtermTakenKey = "MidtermTaken";
    const string FinalTakenKey   = "FinalTaken";
    const string StudyScoreKey   = "StudyGameScore";
    const string GradesKey       = "Grades";

    /// <summary>Percent correct on the exam so far, from strikes used against the strike pool.</summary>
    public static float ExamPercent(int strikes, int strikePool)
        => strikePool > 0 ? 100f * Mathf.Max(0, strikePool - strikes) / strikePool : 100f;

    /// <summary>Standard letter-grade band for a percent-correct value.</summary>
    public static string LetterGrade(float percent)
    {
        if (percent >= 93f) return "A";
        if (percent >= 90f) return "A-";
        if (percent >= 87f) return "B+";
        if (percent >= 83f) return "B";
        if (percent >= 80f) return "B-";
        if (percent >= 77f) return "C+";
        if (percent >= 73f) return "C";
        if (percent >= 70f) return "C-";
        if (percent >= 67f) return "D+";
        if (percent >= 63f) return "D";
        if (percent >= 60f) return "D-";
        return "F";
    }

    /// <summary>Standard 4.0-scale GPA value for a letter grade.</summary>
    public static float LetterGradeGpa(string letter) => letter switch
    {
        "A" => 4.0f, "A-" => 3.7f, "B+" => 3.3f, "B" => 3.0f, "B-" => 2.7f,
        "C+" => 2.3f, "C" => 2.0f, "C-" => 1.7f, "D+" => 1.3f, "D" => 1.0f, "D-" => 0.7f,
        _ => 0.0f, // F
    };

    /// <summary>The exam-in-progress's live letter grade, from strikes used so far.</summary>
    public static string CurrentExamLetterGrade(int strikes, int strikePool)
        => LetterGrade(ExamPercent(strikes, strikePool));

    /// <summary>This exam's GPA contribution (half the 4.0 scale) for a given letter grade.</summary>
    public static float ExamHalfPoints(string letterGrade) => LetterGradeGpa(letterGrade) * (ExamHalf / 4f);

    public static bool IsFinal(string examId)
        => string.Equals(examId, FinalId, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// GPA shown during an exam. Midterm assumes a perfect final (starts 4.0, floors at 2.0);
    /// the final starts at midtermHalf + 2.0 and floors at midtermHalf.
    /// </summary>
    public static float ProjectedGpa(string examId, int strikes, int strikePool)
    {
        float points = ExamHalfPoints(CurrentExamLetterGrade(strikes, strikePool));
        if (IsFinal(examId))
            return StatsManager.Get_Numbered_Stat(MidtermScoreKey) + points;
        return points + ExamHalf;
    }

    /// <summary>The other exam's recorded letter grade, for display during the current exam (blank if not yet taken).</summary>
    public static string PreviousExamLabel(string examId)
    {
        if (IsFinal(examId))
            return StatsManager.Get_Boolean_Stat(MidtermTakenKey)
                ? StatsManager.Get_String_Stat(MidtermLetterKey)
                : "";
        return ""; // Midterm has no prior exam
    }

    public static void RecordExam(string examId, string letterGrade)
    {
        float points = ExamHalfPoints(letterGrade);
        if (IsFinal(examId))
        {
            StatsManager.Set_Numbered_Stat(FinalScoreKey, points);
            StatsManager.Set_String_Stat(FinalLetterKey, letterGrade);
            StatsManager.Set_Boolean_Stat(FinalTakenKey, true);
        }
        else if (string.Equals(examId, MidtermId, System.StringComparison.OrdinalIgnoreCase))
        {
            StatsManager.Set_Numbered_Stat(MidtermScoreKey, points);
            StatsManager.Set_String_Stat(MidtermLetterKey, letterGrade);
            StatsManager.Set_Boolean_Stat(MidtermTakenKey, true);
        }
        else
        {
            Debug.LogWarning($"[GradeCalculator] RecordExam: unrecognized examId '{examId}', grade not recorded.");
            return;
        }
        RefreshGrades();
    }

    /// <summary>
    /// Both exams done -> mid + final. Midterm only -> midterm half scaled to 0-4.
    /// No exams yet -> study-based value.
    /// </summary>
    public static float ClassGpa()
    {
        float mid = StatsManager.Get_Numbered_Stat(MidtermScoreKey);
        float fin = StatsManager.Get_Numbered_Stat(FinalScoreKey);

        float gpa;
        if (StatsManager.Get_Boolean_Stat(FinalTakenKey)) gpa = mid + fin;
        else if (StatsManager.Get_Boolean_Stat(MidtermTakenKey)) gpa = mid * 2f;
        else gpa = StudyNorm(Mathf.RoundToInt(StatsManager.Get_Numbered_Stat(StudyScoreKey)));

        return Mathf.Clamp(gpa, 0f, 4f);
    }

    public static float RefreshGrades()
    {
        float gpa = ClassGpa();
        StatsManager.Set_Numbered_Stat(GradesKey, gpa);
        return gpa;
    }

    static float StudyNorm(int raw)
    {
        if (raw >= 5) return 4f;
        if (raw == 4) return 3f;
        if (raw == 3) return 2f;
        if (raw >= 1) return 1f;
        return 0f;
    }
}
