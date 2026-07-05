using UnityEngine;

// Determines what time state the game enters when the player returns home
// after this checkpoint's conversation ends.
public enum HomeReturnState
{
    NewWeekMorning,    // Week+1, DayPhase=0 — default for all location visits
    SecondHalfOfWeek, // DayPhase=1, week unchanged — class or morning study
    FreeAction,        // no time change — reserved for future paths
}

namespace VNEngine
{
    public class NodeCheckpoint : Node
    {
        // Minimum week this checkpoint should bump the player to.
        // If current week is already >= this, we just +1 instead.
        // Only used when returnState is NewWeekMorning.
        [Tooltip("Use this only to advance time to a specific week")]
        public int week;

        [SerializeField] public HomeReturnState returnState = HomeReturnState.NewWeekMorning;

        public override void Run_Node()
        {
            float storedWeek = StatsManager.Get_Numbered_Stat("Week");
            int currentWeek = Mathf.FloorToInt(storedWeek);

            Debug.Log($"[NodeCheckpoint] Week: {currentWeek}, returnState: {returnState}");

            switch (returnState)
            {
                case HomeReturnState.SecondHalfOfWeek:
                    StatsManager.Set_Numbered_Stat("DayPhase", 1f);
                    Debug.Log("[NodeCheckpoint] SecondHalfOfWeek — DayPhase=1, week unchanged.");
                    break;

                case HomeReturnState.NewWeekMorning:
                    int newWeek;
                    if (week <= 0 || currentWeek >= week)
                        newWeek = currentWeek + 1;
                    else
                        newWeek = week;
                    Debug.Log($"[NodeCheckpoint] NewWeekMorning — advancing to week {newWeek}.");
                    StatsManager.Set_Numbered_Stat("Week",      newWeek);
                    StatsManager.Set_Numbered_Stat("DayPhase",  0f);
                    StatsManager.Set_Numbered_Stat("DayOffset", 0f);
                    StatsManager.Set_Boolean_Stat("ClassAttendedThisWeek", false);
                    FootballScheduler.SimulateUnplayedPastGames(newWeek);
                    break;

                case HomeReturnState.FreeAction:
                    Debug.Log("[NodeCheckpoint] FreeAction — no time change.");
                    break;
            }

            CheckpointManager.SaveCheckpoint();
            Finish_Node();
        }

        public override void Button_Pressed()
        {
            // Intentionally no-op; this node runs instantly.
        }

        public override void Finish_Node()
        {
            StopAllCoroutines();
            base.Finish_Node();
        }
    }
}