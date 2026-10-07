#if UNITY_EDITOR
using UnityEngine;
using VNEngine;

namespace AnswerCampus.Simulation
{
    // Replicates Main.unity's job (reset stats/PlayerPrefs for a fresh game) directly,
    // rather than driving Main.unity's own menu UI -- same kind of UI-layer bypass as
    // the Study/Football shortcuts in HomeHubPolicy.
    public static class SimStateReset
    {
        public static void ResetAll()
        {
            StatsManager.Clear_All_Stats();

            // Full wipe, matching MenuOptions.ResetProgress()'s own "new game" convention --
            // not just the CP_ checkpoint keys. PlayerPrefsExtra-based lists like
            // "characterLocations" and "messages" (character-routing pins, phone text
            // threads) aren't covered by StatsManager and otherwise silently accumulate
            // across repeated runs within one long-lived Unity session (PathExplorer runs
            // many full playthroughs back to back without restarting the process), which
            // was confirmed to make every run after the first behave differently from a
            // genuinely fresh game.
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();

            // VNEngine.SaveManager writes real files to Application.persistentDataPath
            // (save_slot_N.gd), entirely separate from StatsManager/PlayerPrefs -- neither
            // of the wipes above touches them. This is the prime suspect for a run that
            // skips Orientation and resumes mid-semester: if a run's Orientation gets stuck
            // before its own "Week"-initializing checkpoint runs, ConversationDriver's
            // Quit-to-Home recovery sends the game to Home with no Week stat and no CP_
            // checkpoint, and HomeCutsceneController.EnsureStatsPopulated() then falls back
            // to whatever's on disk in this slot -- silently resuming a stale save (written
            // by an earlier run that hit an in-game save checkpoint, e.g. the Apartment
            // conversations) instead of reaching the "Fresh new game" branch that would
            // otherwise apply once Week/PlayerPrefs are both genuinely gone.
            VNEngine.SaveManager.DeleteAllSaves();

            // Time.timeScale is a global engine property that persists across scene loads
            // within one process -- it's NOT covered by StatsManager or PlayerPrefs. Several
            // places in this codebase set it to 0 expecting whatever scene loads next to put
            // it back to 1 (VNSceneManager.Awake() and LoadSceneNode.Run_Node() both do this
            // for the scenes that have a VNSceneManager) -- but Home.unity has no
            // VNSceneManager and never resets it itself. If anything ever leaves it at 0, the
            // very next run's HomeCutsceneController.LeaveRoom() -> CutsceneOverlayController
            // .FadeIn() hangs forever, since that coroutine's loop is driven entirely by
            // Time.deltaTime (confirmed: the baseline run completes a full 16-week
            // playthrough cleanly, but every subsequent run within the same process then
            // fails identically at that exact step -- consistent with timeScale getting
            // stuck at 0 somewhere near the end of the first run and never recovering).
            Time.timeScale = 1f;
        }
    }
}
#endif
