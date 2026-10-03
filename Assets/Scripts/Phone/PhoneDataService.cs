// PhoneDataService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class PhoneDataService
{
    // ==== MAP ====
    public static List<CharacterLocation> GetCharacterLocations()
    {
        return PlayerPrefsExtra.GetList<CharacterLocation>(
            "characterLocations", new List<CharacterLocation>()); // set elsewhere via your routing/placement
    }
// PhoneDataService.cs  (add below GetCharacterLocations)
    public static List<string> GetAllLocationNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        // 1) From any loaded StageRouteIndex assets (authoritative list used by routing)
        foreach (var idx in Resources.LoadAll<StageRouteIndex>(""))
        {
            if (idx == null || idx.Routes == null) continue;
            foreach (var r in idx.Routes)
                if (r != null && r.location != null && !string.IsNullOrWhiteSpace(r.location.name))
                    names.Add(r.location.name);
        }

        // 2) From any Location components present (scene-driven declarations)
        foreach (var loc in GameObject.FindObjectsByType<Location>(FindObjectsInactive.Include))
        {
            if (loc == null) continue;
            if (!string.IsNullOrWhiteSpace(loc.scene)) names.Add(loc.scene);
            if (loc.data != null && !string.IsNullOrWhiteSpace(loc.data.name)) names.Add(loc.data.name);
        }

        // 3) From saved character pins (ensures we don't miss ad-hoc entries)
        foreach (var cl in GetCharacterLocations())
            if (!string.IsNullOrWhiteSpace(cl.location))
                names.Add(cl.location);

        return names.OrderBy(n => n).ToList();
    }
    // ==== FRIENDS & MESSAGES ====
    public static Dictionary<Character, List<TextMessage>> GetMessageThreads()
    {
        var messages = PlayerPrefsExtra.GetList<TextMessage>("messages", new List<TextMessage>());
        return messages.GroupBy(m => m.from).ToDictionary(g => g.Key, g => g.ToList());
    }

    public static void ClearThread(Character character)
    {
        var dict = GetMessageThreads();
        if (dict.Remove(character))
        {
            var flattened = dict.Values.SelectMany(m => m).ToList();
            PlayerPrefsExtra.SetList("messages", flattened);
            PlayerPrefs.Save();
        }
    }

    // ==== AGENDA ====
    public struct AgendaBucket
    {
        public string label;  // "This Week" | "Next Week" | "Later"
        public List<EventInfo> events;
    }

    public static int GetCurrentWeek()
    {
        return Mathf.RoundToInt(VNEngine.StatsManager.Get_Numbered_Stat("Week"));
    }

    public static List<AgendaBucket> GetAgendaBuckets()
    {
        // derive buckets using the same preview the Calendar uses
        int week = GetCurrentWeek();
        List<EventInfo> thisWeek = GameEvents.GetWeekPreview(week);
        List<EventInfo> nextWeek = GameEvents.GetWeekPreview(week + 1);
        var later = new List<EventInfo>();
        for (int w = week + 2; w <= SemesterHelper.FinalsWeek; w++)
        {
            var list = GameEvents.GetWeekPreview(w);
            if (list != null && list.Count > 0) later.AddRange(list);
        }

        return new List<AgendaBucket> {
            new AgendaBucket { label = "This Week", events = thisWeek ?? new List<EventInfo>() },
            new AgendaBucket { label = "Next Week", events = nextWeek ?? new List<EventInfo>() },
            new AgendaBucket { label = "Later", events = later }
        };
    }

    // Example: pull friend attendance for agenda rows
    public static List<Character> GetAttendeesFor(EventInfo evt)
    {
        // Convention: If you store attendance per event somewhere, look it up here.
        // Fallback heuristic: anyone whose CharacterLocation.location == evt.location this week.
        var locs = GetCharacterLocations();
        return locs.Where(l => l.location == evt.location).Select(l => l.character).Distinct().ToList();
    }
}
