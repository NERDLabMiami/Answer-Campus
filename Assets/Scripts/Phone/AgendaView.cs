using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using VNEngine;

public class AgendaView : MonoBehaviour
{
    public Transform panelRoot;

    [Header("Prefabs")]
    public GameObject groupLabelPrefab;  // TextMeshProUGUI — section headers
    public GameObject agendaRowPrefab;   // AgendaEventButton — alert rows and completed task rows
    public GameObject infoRowPrefab;     // AgendaInfoRow — label+value display rows

    [Header("Event")]
    public StringEvent onEventSelected;
    [Serializable] public class StringEvent : UnityEvent<string> {}

    public void Render()
    {
        Clear(panelRoot);

        int currentWeek = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));

        RenderThisWeekSection(currentWeek);
        RenderAcademicsSection(currentWeek);
        RenderGamesSection(currentWeek);
        RenderCompletedTasksSection();
    }

    // ── Section 1: THIS WEEK ─────────────────────────────────────────────────
    // Only rendered when midterms, finals, or a football game fall on the current week.

    private void RenderThisWeekSection(int currentWeek)
    {
        var alerts = new List<(string label, EventType type, string key)>();

        if (currentWeek == SemesterHelper.MidtermsWeek && !GameEvents.IsCustomEventCompleted(GameEvents.MidtermsEventId))
            alerts.Add(("Midterms", EventType.Midterms, $"Midterms:{currentWeek}"));

        if (currentWeek == SemesterHelper.FinalsWeek && !GameEvents.IsCustomEventCompleted(GameEvents.FinalsEventId))
            alerts.Add(("Finals", EventType.Finals, $"Finals:{currentWeek}"));

        var footballGame = FootballScheduler.GetThisWeeksGame(currentWeek);
        if (footballGame != null && !footballGame.played)
            alerts.Add(($"Sentinels vs. {footballGame.opponent.mascot}", EventType.FootballHomeGame, $"FootballHomeGame:{currentWeek}"));

        if (alerts.Count == 0) return;

        InsertHeader("THIS WEEK");
        foreach (var (label, type, key) in alerts)
        {
            var row = Instantiate(agendaRowPrefab, panelRoot);
            var btn = row.GetComponent<AgendaEventButton>();
            var capturedKey = key;
            btn?.Bind(label, type, false, () => onEventSelected?.Invoke(capturedKey));
        }
    }

    // ── Section 2: GAMES ─────────────────────────────────────────────────────
    // Season record, past game scores, and the next upcoming opponent.

    private void RenderGamesSection(int currentWeek)
    {
        var allGames = FootballScheduler.GetAllGames();
        if (allGames.Count == 0) return;

        var (wins, losses) = FootballScheduler.GetSeasonRecord(currentWeek);

        InsertHeader("Sentinel Football");
        if (wins > 0 || losses > 0)
            InsertInfoRow("Season Record", $"{wins}–{losses}");

        var pastGames = allGames.Where(g => g.week < currentWeek).OrderBy(g => g.week).ToList();
        foreach (var game in pastGames)
        {
            string result     = game.won ? "W" : "L";
            string score      = $"{game.homeScore}–{game.awayScore}";
            string dateLabel  = GetGameDateLabel(game.week);
            InsertInfoRow($"{dateLabel}  vs. {game.opponent.mascot}", $"{result}  {score}");
        }

        var nextGame = allGames.Where(g => g.week >= currentWeek && !g.played).OrderBy(g => g.week).FirstOrDefault();
        if (nextGame != null)
        {
            string oppRecord = $"{nextGame.opponent.wins}–{nextGame.opponent.losses}";
            string dateLabel = GetGameDateLabel(nextGame.week);
            InsertSubheader("Next Game");
            InsertInfoRow(dateLabel, $"vs. {nextGame.opponent.mascot} ({oppRecord})");
        }
        else
        {
            InsertInfoRow("Season complete", $"Final: {wins}–{losses}");
        }
    }

    private static string GetGameDateLabel(int week)
    {
        var date = SemesterHelper.GetDate(week, 5); // always grid-Saturday with grid-Monday anchor
        return $"{SemesterHelper.GetGridDayName(date)}, {date:MMMM d}";
    }

    private static string GetExamDateLabel(int week)
    {
        var date = SemesterHelper.GetDate(week, 0); // always grid-Monday
        return $"{SemesterHelper.GetGridDayName(date)}, {date:MMMM d}";
    }

    // ── Section 3: ACADEMICS ─────────────────────────────────────────────────
    // Current GPA and countdown to the next exam.

    private void RenderAcademicsSection(int currentWeek)
    {
        InsertHeader("ACADEMIC LIFE");

        float gpa = StatsManager.Get_Numbered_Stat("Grades");
        bool hasStudied = StatsManager.Get_Numbered_Stat("StudyGameScore") > 0;
        if (hasStudied || gpa > 0f)
            InsertInfoRow("GPA", gpa.ToString("F1"));

        bool midtermsCompleted = GameEvents.IsCustomEventCompleted(GameEvents.MidtermsEventId);
        bool finalsCompleted   = GameEvents.IsCustomEventCompleted(GameEvents.FinalsEventId);

        if (!midtermsCompleted)
        {
            InsertInfoRow(GetExamDateLabel(SemesterHelper.MidtermsWeek), "Midterm Exam");
        }
        else if (!finalsCompleted)
        {
            InsertInfoRow(GetExamDateLabel(SemesterHelper.FinalsWeek), "Final Exam");
        }
        else
        {
            InsertInfoRow("Exams complete");
        }
    }

    // ── Section 4: COMPLETED TASKS ───────────────────────────────────────────
    // Historical list of completed custom events, excluding exams (shown in ACADEMICS).
    // Section is omitted entirely when nothing has been completed yet.

    private void RenderCompletedTasksSection()
    {
        var completedTasks = GameEvents.LoadCustomEvents()
            .Where(e => e.completed
                     && e.id != GameEvents.MidtermsEventId
                     && e.id != GameEvents.FinalsEventId)
            .OrderBy(e => e.week)
            .ToList();

        if (completedTasks.Count == 0) return;

        InsertHeader("COMPLETED TASKS");
        foreach (var task in completedTasks)
        {
            var row = Instantiate(agendaRowPrefab, panelRoot);
            var btn = row.GetComponent<AgendaEventButton>();
            btn?.Bind(task.name, EventType.Custom, true, null);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void InsertSubheader(string label)
    {
        var go  = Instantiate(agendaRowPrefab, panelRoot);
        var btn = go.GetComponent<AgendaEventButton>();
        btn?.BindInfo(label);
    }

    private void InsertHeader(string label)
    {
        var go = Instantiate(groupLabelPrefab, panelRoot);
        var t  = go.GetComponent<TextMeshProUGUI>();
        if (t) t.text = label;
    }

    private void InsertInfoRow(string label, string value = null)
    {
        var go  = Instantiate(infoRowPrefab, panelRoot);
        var row = go.GetComponent<AgendaInfoRow>();
        row?.Bind(label, value);
    }

    private void Clear(Transform t)
    {
        if (!t) return;
        for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
    }
}
