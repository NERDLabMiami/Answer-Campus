using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VNEngine;

public static class FootballScheduler
{
    static FootballTeam[] opponents = new FootballTeam[]
    {
        new FootballTeam("Northport", "Grizzlies"),
        new FootballTeam("Central Tech", "Shock"),
        new FootballTeam("Valley State", "Hornets"),
        new FootballTeam("Eastern Pines", "Wolves"),
        new FootballTeam("Bayfront College", "Surge"),
        new FootballTeam("Riverside A&M", "Gators"),
        new FootballTeam("Highland University", "Stags"),
        new FootballTeam("Wheatley", "Titans") { isRival = true }
    };

    public static void GenerateSchedule()
    {
        List<FootballGame> schedule = new List<FootballGame>();
        // Every other week (odd weeks), skipping week 7 (Midterms) and week 16 (Finals)
        List<int> possibleWeeks = new List<int> { 3, 5, 7, 9, 11, 13 };

        // Shuffle opponents so matchups are random, but keep weeks sorted so that
        // gamesPlayed scales correctly with when in the season each game occurs.
        var shuffledOpponents = opponents.ToList();
        Shuffle(shuffledOpponents);

        for (int i = 0; i < possibleWeeks.Count; i++)
        {
            int gamesPlayed = Mathf.Clamp(i * 2, 0, 10);
            int oppWins = UnityEngine.Random.Range(0, gamesPlayed + 1);
            shuffledOpponents[i].wins   = oppWins;
            shuffledOpponents[i].losses = gamesPlayed - oppWins;

            schedule.Add(new FootballGame
            {
                week = possibleWeeks[i],
                opponent = shuffledOpponents[i],
                isHome = true,
                played = false
            });
        }

        string json = JsonUtility.ToJson(new FootballGameListWrapper { games = schedule });
        StatsManager.Set_String_Stat("FootballSchedule", json);
    }

    public static FootballGame GetThisWeeksGame(int currentWeek)
    {
        if (!StatsManager.String_Stat_Exists("FootballSchedule"))
        {
            FootballScheduler.GenerateSchedule();
        }

        string json = StatsManager.Get_String_Stat("FootballSchedule");
        if (string.IsNullOrEmpty(json)) return null;

        var wrapper = JsonUtility.FromJson<FootballGameListWrapper>(json);
        if (wrapper?.games == null) return null;

        return wrapper.games.Find(g => g.week == currentWeek);
    }

    public static (int wins, int losses) GetSeasonRecord(int currentWeek = 0)
    {
        if (!StatsManager.String_Stat_Exists("FootballSchedule")) return (0, 0);
        string json = StatsManager.Get_String_Stat("FootballSchedule");
        if (string.IsNullOrEmpty(json)) return (0, 0);
        var wrapper = JsonUtility.FromJson<FootballGameListWrapper>(json);
        if (wrapper?.games == null) return (0, 0);
        int wins   = wrapper.games.Count(g => g.played && g.won);
        int losses = wrapper.games.Count(g => (g.played && !g.won) ||
                                              (currentWeek > 0 && g.week < currentWeek && !g.played));
        return (wins, losses);
    }

    public static List<FootballGame> GetAllGames()
    {
        if (!StatsManager.String_Stat_Exists("FootballSchedule")) return new List<FootballGame>();
        string json = StatsManager.Get_String_Stat("FootballSchedule");
        if (string.IsNullOrEmpty(json)) return new List<FootballGame>();
        var wrapper = JsonUtility.FromJson<FootballGameListWrapper>(json);
        return wrapper?.games ?? new List<FootballGame>();
    }

    public static void SimulateUnplayedPastGames(int currentWeek)
    {
        if (!StatsManager.String_Stat_Exists("FootballSchedule")) return;
        string json = StatsManager.Get_String_Stat("FootballSchedule");
        if (string.IsNullOrEmpty(json)) return;
        var wrapper = JsonUtility.FromJson<FootballGameListWrapper>(json);
        if (wrapper?.games == null) return;

        bool changed = false;
        foreach (var game in wrapper.games)
        {
            if (game.week < currentWeek && !game.played)
            {
                game.played   = true;
                game.won      = false;
                // Deterministic scores seeded by week so values are stable across renders
                game.homeScore = (game.week * 3) % 14;
                game.awayScore = game.homeScore + 7 + (game.week % 14);
                changed = true;
            }
        }

        if (changed)
            StatsManager.Set_String_Stat("FootballSchedule", JsonUtility.ToJson(wrapper));
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int rnd = UnityEngine.Random.Range(i, list.Count);
            T temp = list[rnd];
            list[rnd] = list[i];
            list[i] = temp;
        }
    }
}
