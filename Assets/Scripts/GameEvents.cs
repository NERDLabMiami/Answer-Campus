using System;
using System.Collections.Generic;
using UnityEngine;
using VNEngine;

[Serializable]
public class CustomEvent
{
    public string id;          // stable key
    public string name;        // e.g., "Art Show Opening"
    public int week;           // absolute week number
    public Sprite icon;
    public string location;    // scene name
    public bool unlocked;      // set true when a convo unlocks it
    public bool completed; 
}

[Serializable]
class CustomEventList { public List<CustomEvent> items = new(); }

public enum EventType { Midterms, Finals, FootballHomeGame, Custom }
[Serializable]
public struct EventInfo
{
    public string id;
    public EventType type;
    public string label;     // display label
    public int week;
    public string location;  // may be null
    public Sprite icon;
}

public static class GameEvents
{
    const string CustomEventsKey = "CustomEvents";
    public const string MidtermsEventId = "EXAM_MIDTERMS";
    public const string FinalsEventId   = "EXAM_FINALS";

    public static void EnsureSemesterRequiredEventsRegistered()
    {
        // Midterms
        if (!HasCustomEvent(MidtermsEventId))
        {
            RegisterOrUpdateCustomEvent(new CustomEvent
            {
                id        = MidtermsEventId,
                name      = "Midterms",
                week      = SemesterHelper.MidtermsWeek,
                location  = "Lecture Hall",
                unlocked  = true,
                completed = false
            });
        }

        // Finals
        if (!HasCustomEvent(FinalsEventId))
        {
            RegisterOrUpdateCustomEvent(new CustomEvent
            {
                id        = FinalsEventId,
                name      = "Finals",
                week      = SemesterHelper.FinalsWeek,
                location  = "Lecture Hall",
                unlocked  = true,
                completed = false
            });
        }
    }

    public static List<CustomEvent> LoadCustomEvents()
    {
        if (!StatsManager.String_Stat_Exists(CustomEventsKey)) return new();
        string json = StatsManager.Get_String_Stat(CustomEventsKey);
        if (string.IsNullOrEmpty(json)) return new();
        var list = JsonUtility.FromJson<CustomEventList>(json);
        return list?.items ?? new();
    }


    private static void SaveCustomEvents(List<CustomEvent> items)
    {
        var wrap = new CustomEventList { items = items ?? new() };
        string json = JsonUtility.ToJson(wrap);
        StatsManager.Set_String_Stat(CustomEventsKey, json);
    }

    public static void RegisterOrUpdateCustomEvent(CustomEvent ev)
    {
        var items = LoadCustomEvents();
        int idx = items.FindIndex(x => x.id == ev.id);
        if (idx >= 0) items[idx] = ev; else items.Add(ev);
        SaveCustomEvents(items);
    }
    public static bool IsCustomEventCompleted(string key)
    {
        if (!TryGetCustomEvent(key, out var ev)) return false;
        return ev.completed;
    }

    private static bool TryGetCustomEvent(string key, out CustomEvent found)
    {
        found = null;
        if (string.IsNullOrEmpty(key)) return false;

        var items = LoadCustomEvents();
        for (int i = 0; i < items.Count; i++)
        {
            var ev = items[i];
            if (ev == null) continue;

            if (string.Equals(ev.id, key, StringComparison.Ordinal) ||
                string.Equals(ev.name, key, StringComparison.Ordinal))
            {
                found = ev;
                return true;
            }
        }

        return false;
    }
    public static string BuildStageRouteEventId(Character character, string sceneName, int stage)
    {
        // Human-readable but stable
        return $"{character}_{sceneName}_Stage{stage}";
    }
    public static bool HasCustomEvent(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;

        foreach (var ev in LoadCustomEvents())
        {
            // Match by ID or by Name
            if (ev.id == key || ev.name == key)
                return true;
        }

        return false;
    }

    public static bool MarkCustomEventCompleted(string key, bool completed = true)
    {
        if (string.IsNullOrEmpty(key)) return false;

        var items = LoadCustomEvents();
        bool changed = false;

        for (int i = 0; i < items.Count; i++)
        {
            var ev = items[i];
            if (ev == null) continue;

            if (string.Equals(ev.id, key, StringComparison.Ordinal) ||
                string.Equals(ev.name, key, StringComparison.Ordinal))
            {
                if (ev.completed != completed)
                {
                    ev.completed = completed;
                    items[i] = ev;
                    changed = true;
                }
                break;
            }
        }

        if (changed) SaveCustomEvents(items);
        return changed;
    }
    // Finds a required exam (Finals takes priority over Midterms) that's due this week or
    // OVERDUE (ev.week <= currentWeek) and not yet completed. GetWeekPreview's strict
    // "ev.week == week" match is correct for "what's on the agenda this week" display, but a
    // forced exam redirect must still catch an exam whose week got skipped entirely by a
    // story checkpoint's explicit week-jump -- confirmed to happen (a "Set Week" node
    // configured to land on week 15 skips week 14 outright) -- which would otherwise let the
    // player skip Finals forever, since Calendar.TryRedirectToRequiredEvent() only ever
    // checks the current week value against GetWeekPreview(currentWeek).
    public static bool TryFindOverdueRequiredEvent(int currentWeek, out EventInfo found)
    {
        found = default;
        var items = LoadCustomEvents();

        var finals = items.Find(e => e != null && e.id == FinalsEventId);
        if (finals != null && !finals.completed && finals.week <= currentWeek)
        {
            found = new EventInfo { id = finals.id, type = EventType.Finals, label = finals.name, week = finals.week, location = finals.location, icon = finals.icon };
            return true;
        }

        var midterms = items.Find(e => e != null && e.id == MidtermsEventId);
        if (midterms != null && !midterms.completed && midterms.week <= currentWeek)
        {
            found = new EventInfo { id = midterms.id, type = EventType.Midterms, label = midterms.name, week = midterms.week, location = midterms.location, icon = midterms.icon };
            return true;
        }

        return false;
    }

    public static List<EventInfo> GetWeekPreview(int week)
    {
        var outList = new List<EventInfo>();
        
        // Football (home) this week?
        var game = FootballScheduler.GetThisWeeksGame(week);
        if (game != null && game.isHome && !game.played)
        {
            outList.Add(new EventInfo {
                type = EventType.FootballHomeGame,
                label = $"Sentinels vs. {game.opponent.mascot}",
                week = week,
                location = "FootballStadium"
            });
        }

        // Custom unlocked events
        foreach (var ev in LoadCustomEvents())
        {
            if (ev.unlocked && ev.week == week && !ev.completed)
            {
                outList.Add(new EventInfo {
                    id = ev.id,
                    type = EventType.Custom,
                    label = ev.name,
                    week = ev.week,
                    location = ev.location,
                    icon = ev.icon
                });
            }
        }

        return outList;
    }
}
