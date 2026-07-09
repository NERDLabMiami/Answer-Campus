using System;
using System.Collections.Generic;
using UnityEngine;

public static class SemesterHelper
{
    public const int FinalsWeek = 14;
    public const int MidtermsWeek = 6;
    public const int MidtermsWarningStart = 4;
    public const int FinalsWarningStart = 5;
    public const int DaysPerWeek = 7;

    // Set this at game start from a GameSettings component or HomeCutsceneController.
    public static int SemesterYear = 2026;

    // Returns the grid-Monday anchor date for a given week.
    // All anchors are days {2,9,16,23} (or {16,23} for August) — always column 1 (grid-Monday).
    // Adding dayOffset=5 stays within the same month and always lands on column 6 (grid-Saturday).
    private static DateTime GetGridMonday(int week)
    {
        if (week <= 0) return new DateTime(SemesterYear, 8, 16);  // orientation week
        if (week == 1) return new DateTime(SemesterYear, 8, 23);  // last Aug week (first class)
        if (week >= 14) return new DateTime(SemesterYear, 12, 2); // finals
        // Weeks 2–13: Sep/Oct/Nov, 4 weeks per month starting on day 2
        int idx   = week - 2;
        int month = 9 + idx / 4;        // 9=Sep, 10=Oct, 11=Nov
        int day   = 2 + (idx % 4) * 7; // 2, 9, 16, 23
        return new DateTime(SemesterYear, month, day);
    }

    // Week 0 = orientation (Aug 16 anchor; activities start at dayOffset=4 = Aug 20).
    // Week 1 = first class week (Aug 23). Weeks 2–13: Sep/Oct/Nov. Week 14 = finals (Dec 2).
    public static DateTime GetDate(int week, int dayOffset)
    {
        return GetGridMonday(week).AddDays(dayOffset);
    }

    public static string GetDateLabel(int week, int dayOffset = 0)
    {
        var date = GetDate(week, dayOffset);
        return $"{date:MMMM} {date.Day}{OrdinalSuffix(date.Day)}, {SemesterYear}";
    }

    // Grid-based day names: column (day-1)%7 maps to these names (0=Sun…6=Sat).
    private static readonly string[] _gridDayNames =
        { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    // Returns the grid day name for a date based on its day-of-month column, not real DayOfWeek.
    public static string GetGridDayName(DateTime date) =>
        _gridDayNames[(date.Day - 1) % 7];

    // Returns "Saturday" — just the day of the week (grid-column based).
    public static string GetDayOfWeekLabel(int week, int dayOffset = 0)
    {
        return GetGridDayName(GetDate(week, dayOffset));
    }

    // Returns "Morning" (DayPhase 0) or "Afternoon" (DayPhase 1).
    private static string GetTimeLabel(int dayPhase) =>
        dayPhase == 1 ? "Afternoon" : "Morning";

    // Returns "Saturday Morning" — day of week combined with time of day.
    public static string GetDayAndTimeLabel(int week, int dayOffset, int dayPhase) =>
        $"{GetDayOfWeekLabel(week, dayOffset)} {GetTimeLabel(dayPhase)}";

    private static string OrdinalSuffix(int day)
    {
        if (day >= 11 && day <= 13) return "th";
        return (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
    }

    public static string GetMonthForWeek(int week)
    {
        return GetGridMonday(week).ToString("MMMM");
    }

    public static string GetStudyPrompt(int currentWeek)
    {
        int weeksUntilMidterms = MidtermsWeek - currentWeek;
        int weeksUntilFinals = FinalsWeek - currentWeek;

        if (weeksUntilMidterms >= 0 && weeksUntilMidterms <= MidtermsWarningStart)
        {
            return GetUrgencyMessage(weeksUntilMidterms, "Midterms");
        }
        else if (weeksUntilFinals >= 0 && weeksUntilFinals <= FinalsWarningStart)
        {
            return GetUrgencyMessage(weeksUntilFinals, "Finals");
        }
        else
        {
            return null;
        }
    }

    private static string GetUrgencyMessage(int weeksLeft, string examName)
    {
        if (weeksLeft > 2)
        {
            return GetRandomPhrase(new List<string>
            {
                $"{examName} are coming up. Start preparing!",
                $"{examName} are on the horizon. Get ready!",
                $"{examName} are approaching. Plan your study time!"
            });
        }
        else if (weeksLeft == 2)
        {
            return GetRandomPhrase(new List<string>
            {
                $"{examName} are getting closer. Hit the books!",
                $"{examName} are around the corner. Stay sharp!",
                $"Only 2 weeks left until {examName}. Let's focus!"
            });
        }
        else if (weeksLeft == 1)
        {
            return GetRandomPhrase(new List<string>
            {
                $"{examName} are next week. Time to crunch!",
                $"{examName} are just days away. Study hard!",
                $"{examName} are almost here. Finish strong!"
            });
        }
        else // weeksLeft == 0
        {
            return GetRandomPhrase(new List<string>
            {
                $"{examName} are this week. Give it your all!",
                $"{examName} have arrived. Stay focused!",
                $"It's {examName} week. You've got this!"
            });
        }
    }

    private static string GetRandomPhrase(List<string> phrases)
    {
        int index = UnityEngine.Random.Range(0, phrases.Count);
        return phrases[index];
    }
}
