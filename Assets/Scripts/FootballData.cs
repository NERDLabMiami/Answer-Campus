using System;
using System.Collections.Generic;

[Serializable]
public class FootballTeam
{
    public string schoolName;
    public string mascot;
    public bool isRival = false;
    public int wins;
    public int losses;

    public FootballTeam(string name, string mascot)
    {
        this.schoolName = name;
        this.mascot = mascot;
    }
}

[Serializable]
public class FootballGame
{
    public int week;
    public FootballTeam opponent;
    public bool isHome;
    public bool played;
    public bool won;
    public int homeScore;
    public int awayScore;
}

[Serializable]
public class FootballGameListWrapper
{
    public List<FootballGame> games = new List<FootballGame>();
}
