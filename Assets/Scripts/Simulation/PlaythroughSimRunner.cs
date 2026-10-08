#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using VNEngine;

namespace AnswerCampus.Simulation
{
    // Public glue entry point -- the only thing Assets/Tests/Simulation reaches into,
    // via a single reflection hop, since a custom Test assembly can never compile-time
    // reference the predefined Assembly-CSharp assembly that this file lives in.
    public static class PlaythroughSimRunner
    {
        private const int MaxHomeVisits = 3000;

        // Wall-clock, not frame count: this PlayMode test executes frames far faster than
        // normal gameplay (confirmed Time.deltaTime as low as ~0.0007s/frame, ~1400fps,
        // since nothing throttles it to vsync). A raw frame-count budget sized for ~60fps
        // (this used to be 400 frames) undercounts badly for anything driven by
        // WaitForSeconds/Time.deltaTime instead of player input -- confirmed as the exact
        // cause of a consistent false "stuck" on literally every NavigateOut call, since
        // HomeCutsceneController.LeaveRoom()'s overlay fade (0.4s) alone needs ~570 frames
        // at the measured rate, already exceeding the old 400-frame budget on its own.
        private const float IdleTimeoutSeconds = 20f;

        // Move-In Day's cutscene (HomeCutsceneController.OrchestrateSceneLoad) is a real,
        // one-time wall-clock-timed sequence -- holdDuration, per-object reveal pauses, the
        // night-view hold, two overlay fades -- driven by WaitForSeconds/Time.deltaTime, not
        // by frame count. Confirmed via diagnostic logging that this PlayMode test executes
        // frames far faster than normal gameplay (Time.deltaTime measured at ~0.0007s/frame,
        // ~1400fps, since nothing throttles it to vsync) -- a single object's 1-second reveal
        // fade alone took 1302 frames, so a frame-count budget sized for ~60fps (the old 400-
        // frame IdleFrameLimit, since replaced by IdleTimeoutSeconds above) massively
        // undercounts how many frames this specific wall-clock-timed sequence needs. Budgeted
        // in real elapsed seconds instead, via Time.realtimeSinceStartup, so it's correct
        // regardless of the actual frame rate.
        private const float MoveInDayTimeoutSeconds = 60f;

        public static IEnumerator Run()
        {
            var trace = new List<string>();
            var safe = RunBodySafely(trace);
            while (safe.MoveNext()) yield return safe.Current;
            WriteReport(trace, LastVerdict, LastVerdictDetail);
        }

        // internal (not private): AnswerCampus.Simulation.PathExplorer reuses this exact
        // playthrough logic to re-run it from scratch per divergence point. No asmdef
        // boundary between these two files -- both compile into Assembly-CSharp -- so
        // internal visibility is all that's needed.
        internal static string LastVerdict = "inconclusive";
        internal static string LastVerdictDetail = "";

        // Runs one full playthrough, catching any exception so a single bad run (baseline
        // or a path-exploration divergence) reports as "error" instead of crashing whatever
        // is iterating over many of these in sequence.
        internal static IEnumerator RunBodySafely(List<string> trace, List<int> scriptedChoices = null, Dictionary<int, int> scriptedGateOutcomes = null)
        {
            IEnumerator body = RunBody(trace, scriptedChoices, scriptedGateOutcomes);
            bool moved = true;
            while (moved)
            {
                try
                {
                    moved = body.MoveNext();
                }
                catch (System.Exception e)
                {
                    LastVerdict = "error";
                    LastVerdictDetail = $"Unhandled exception: {e}";
                    yield break;
                }
                if (moved) yield return body.Current;
            }
        }

        internal static IEnumerator RunBody(List<string> trace, List<int> scriptedChoices = null, Dictionary<int, int> scriptedGateOutcomes = null)
        {
            // The real game's own ending content legitimately loads "Main" (the main menu)
            // after its closing cutscene -- a different terminal scene than every other
            // DriveUntilSceneIs("Home", ...) call in this method expects. Call this right
            // after any such drive succeeds; if it reached Main instead of Home, record
            // that as the successful ending it is and have the caller yield break, rather
            // than falling through to code that assumes "Home" and would otherwise try to
            // keep progressing a playthrough that has already legitimately finished.
            bool HandleIfReachedMainMenu(string context)
            {
                if (!ConversationDriver.LastReachedMainMenu) return false;
                trace.Add($"{context}: the game's own ending content returned to the Main Menu -- treating as a completed playthrough.");
                LastVerdict = "reached_ending";
                LastVerdictDetail = $"{context}: reached Main Menu via the game's own ending content instead of Home.";
                return true;
            }

            ConversationDriver.BeginNewRun(scriptedChoices, scriptedGateOutcomes);
            HomeHubPolicy.ResetForNewRun();
            SimStateReset.ResetAll();

            // Drive the actual New Game entry point (Main.unity's save-slot UI) instead of
            // jumping straight to Student Center and force-starting Orientation ourselves.
            // That direct bypass meant this tool never exercised the real reset/start path
            // at all -- confirmed separately that real bugs (stale StatsManager/PlayerPrefs/
            // SaveManager state leaking into a "new" game) live exactly in that path and
            // this bypass would never have caught any of them.
            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var saveSlotController = Object.FindFirstObjectByType<SaveSlotController>();
            if (saveSlotController == null || saveSlotController.slots == null || saveSlotController.slots.Length == 0)
            {
                LastVerdict = "error";
                LastVerdictDetail = "Could not find a SaveSlotController with slots in the 'Main' scene.";
                yield break;
            }

            var slot = saveSlotController.slots[0];
            if (slot == null)
            {
                LastVerdict = "error";
                LastVerdictDetail = "SaveSlotController.slots[0] is unassigned.";
                yield break;
            }

            // Mirrors the real player flow: an occupied slot's button is wired to Load, not
            // New Game (confirmed in SaveSlotController.RefreshUI()) -- a leftover save in
            // slot 0 (this run's own prior leftovers already handled by SimStateReset.
            // ResetAll(), but nothing here guards against a save genuinely left on disk from
            // outside this tool) must be deleted first or "New Game" silently resumes it.
            if (slot.deleteButton != null && slot.deleteButton.gameObject.activeInHierarchy)
            {
                trace.Add("[Main Menu] Slot 0 had an existing save -- clicking Delete.");
                slot.deleteButton.onClick.Invoke();
                yield return null;
            }

            if (slot.newGameButton == null || !slot.newGameButton.gameObject.activeInHierarchy)
            {
                LastVerdict = "error";
                LastVerdictDetail = "Slot 0's New Game button is missing or inactive after clearing any existing save.";
                yield break;
            }

            trace.Add("[Main Menu] Clicking New Game on slot 0.");
            slot.newGameButton.onClick.Invoke();

            // Defensive, same reasoning as the later apartment-invite step: Home.unity has
            // no VNSceneManager to reset Time.timeScale on load (unlike scenes that have
            // one), and Move-In Day's hold/fade/reveal sequence is driven entirely by
            // WaitForSeconds and Time.deltaTime loops -- if anything in Main.unity's own UI
            // flow (fades, transitions) left timeScale at 0, every one of those waits hangs
            // forever with no error, which matches "IsCutscenePlaying stays true forever,
            // orientationButton never activates" exactly.
            Time.timeScale = 1f;

            // New Game loads straight into Home.unity's Move-In Day sequence, which ends on
            // a real UI button (HomeCutsceneController.orientationButton) requiring a click
            // before it proceeds to Student Center -- not something ConversationDriver's
            // generic conversation/choice mashing drives, since it's outside VNEngine's
            // node/choice system entirely.
            float moveInStartTime = Time.realtimeSinceStartup;
            HomeCutsceneController home = null;
            while (Time.realtimeSinceStartup - moveInStartTime < MoveInDayTimeoutSeconds)
            {
                home = HomeCutsceneController.Instance;
                if (home != null && home.orientationButton != null && home.orientationButton.gameObject.activeInHierarchy)
                    break;
                yield return null;
            }

            if (home == null || home.orientationButton == null || !home.orientationButton.gameObject.activeInHierarchy)
            {
                string diag =
                    $"Time.timeScale={Time.timeScale} Time.frameCount={Time.frameCount} " +
                    $"scene='{SceneManager.GetActiveScene().name}' " +
                    $"HomeCutsceneController.Instance={(home == null ? "NULL" : "set")} " +
                    (home != null
                        ? $"IsCutscenePlaying={home.IsCutscenePlaying} " +
                          $"orientationButton={(home.orientationButton == null ? "NULL" : (home.orientationButton.gameObject.activeInHierarchy ? "active" : "inactive"))} " +
                          $"Week={StatsManager.Get_Numbered_Stat("Week")} " +
                          $"HomeInitialized={StatsManager.Get_Boolean_Stat("HomeInitialized")} " +
                          $"DayOffset={StatsManager.Get_Numbered_Stat("DayOffset")} " +
                          $"DayPhase={StatsManager.Get_Numbered_Stat("DayPhase")}"
                        : "");
                LastVerdict = "stuck";
                LastVerdictDetail = $"Move-In Day's orientation button never appeared after {MoveInDayTimeoutSeconds}s. | DIAGNOSTIC: {diag}";
                yield break;
            }

            trace.Add("[Home] Move-In Day -- clicking the orientation button.");
            home.orientationButton.onClick.Invoke();

            // Wait for the subsequent night-view hold + overlay fade to actually leave Home
            // (same pattern as the apartment-invite wait below: DriveUntilSceneIs("Home", ..)
            // would trivially "succeed" immediately since the scene hasn't changed yet).
            float leaveForOrientationStartTime = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name == "Home" &&
                   Time.realtimeSinceStartup - leaveForOrientationStartTime < MoveInDayTimeoutSeconds)
            {
                yield return null;
            }
            if (SceneManager.GetActiveScene().name == "Home")
            {
                LastVerdict = "stuck";
                LastVerdictDetail = $"Never left Home for Orientation after clicking the orientation button ({MoveInDayTimeoutSeconds}s).";
                yield break;
            }

            // Student Center's own VNSceneManager auto-starts its starting_conversation
            // (Orientation) on load (VNSceneManager.Start_Scene()) -- no manual
            // GameObject.Find/Start_Conversation() needed, unlike the old direct-load bypass.
            trace.Add("Reached Student Center; VNSceneManager auto-starts 'Orientation'.");
            WriteReport(trace, "running", "In progress -- driving through Orientation.");

            var driveToHome = ConversationDriver.DriveUntilSceneIs("Home", IdleTimeoutSeconds);
            while (driveToHome.MoveNext()) yield return driveToHome.Current;

            if (HandleIfReachedMainMenu("During Orientation")) yield break;
            if (!ConversationDriver.LastDriveSucceeded)
            {
                LastVerdict = "stuck";
                LastVerdictDetail = "During Orientation: " + ConversationDriver.LastStuckDescription;
                yield break;
            }
            if (ConversationDriver.LastUsedQuitToHomeRecovery)
                trace.Add("[FINDING] Needed Quit-to-Home recovery during Orientation -- a conversation there didn't return to Home on its own.");
            trace.Add("Reached Home for the first time");

            // The map isn't usable yet this early -- the only intended way forward is
            // Leilani's text-message invite to the Apartment (HomeCutsceneController.
            // NavigateOut("Apartment", ...), a quick-reply path entirely separate from the
            // characterLocations map pins HomeHubPolicy reads). Take that specific, known
            // first step explicitly rather than whatever the general policy would pick.
            trace.Add("[First action] NavigateOut to 'Apartment' (Leilani's text invite)");
            trace.Add($"[DIAGNOSTIC] Time.timeScale={Time.timeScale} before apartment NavigateOut");
            WriteReport(trace, "running", "In progress -- taking Leilani's apartment invite as the first action.");
            Time.timeScale = 1f; // Defensive: isolate whether something during Orientation re-set this after SimStateReset.ResetAll()'s own reset.
            HomeCutsceneController.NavigateOut("Apartment", "Leilani's apartment invite");

            // DriveUntilSceneIs("Home", ...) succeeds the instant it sees scene=="Home" --
            // calling it immediately here would trivially "succeed" without ever waiting,
            // since the NavigateOut-triggered load hasn't taken effect yet and the active
            // scene is still reported as "Home". Wait for the scene to actually leave Home
            // first (same pattern the main loop below uses), then wait for it to return.
            float apartmentLeaveStartTime = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name == "Home" &&
                   Time.realtimeSinceStartup - apartmentLeaveStartTime < IdleTimeoutSeconds)
            {
                yield return null;
            }
            if (SceneManager.GetActiveScene().name == "Home")
            {
                // Diagnostic dump: PlayerPrefs leakage and Time.timeScale stuck-at-0 were both
                // ruled out by fixes that didn't change this reproducible failure (only the
                // baseline run -- the first one in the process -- ever gets past this point).
                // Capture hard data on the actual blocked state instead of more hypotheses.
                string diag =
                    $"Time.timeScale={Time.timeScale} Time.frameCount={Time.frameCount} " +
                    $"HomeCutsceneController.Instance={(HomeCutsceneController.Instance == null ? "NULL" : "set")} " +
                    $"CutsceneOverlayController.Instance={(CutsceneOverlayController.Instance == null ? "NULL" : "set")} " +
                    $"UIManager.ui_manager={(VNEngine.UIManager.ui_manager == null ? "NULL" : "set")} " +
                    $"VNSceneManager.scene_manager={(VNSceneManager.scene_manager == null ? "NULL" : "set")} " +
                    $"VNSceneManager.current_conversation={(VNSceneManager.current_conversation == null ? "NULL" : VNSceneManager.current_conversation.name)}";

                LastVerdict = "stuck";
                LastVerdictDetail = $"NavigateOut to 'Apartment' never left Home after {IdleTimeoutSeconds}s. | DIAGNOSTIC: {diag}";
                yield break;
            }

            var driveFromInvite = ConversationDriver.DriveUntilSceneIs("Home", IdleTimeoutSeconds);
            while (driveFromInvite.MoveNext()) yield return driveFromInvite.Current;

            if (HandleIfReachedMainMenu("After Leilani's apartment invite")) yield break;
            if (!ConversationDriver.LastDriveSucceeded)
            {
                LastVerdict = "stuck";
                LastVerdictDetail = "After Leilani's apartment invite: " + ConversationDriver.LastStuckDescription;
                yield break;
            }
            if (ConversationDriver.LastUsedQuitToHomeRecovery)
                trace.Add("[FINDING] Needed Quit-to-Home recovery after the apartment invite -- that conversation didn't return to Home on its own.");
            trace.Add("Returned to Home after the apartment invite");

            int visits = 0;
            while (visits < MaxHomeVisits)
            {
                visits++;

                for (int i = 0; i < 5; i++) yield return null;

                string scene = SceneManager.GetActiveScene().name;
                float week = StatsManager.Get_Numbered_Stat("Week");

                if (scene == "Home" && week >= SemesterHelper.FinalsWeek + 1)
                {
                    trace.Add($"[Visit {visits}] Week {week}: reached ending.");
                    LastVerdict = "reached_ending";
                    LastVerdictDetail = $"Week {week} reached while on Home; semester-end condition satisfied.";
                    yield break;
                }

                if (scene != "Home")
                {
                    trace.Add($"[Visit {visits}] Auto-redirected to '{scene}' (likely exam/forced event).");
                    var drive = ConversationDriver.DriveUntilSceneIs("Home", IdleTimeoutSeconds);
                    while (drive.MoveNext()) yield return drive.Current;
                    if (HandleIfReachedMainMenu($"[Visit {visits}] After redirect to '{scene}'")) yield break;
                    if (!ConversationDriver.LastDriveSucceeded)
                    {
                        LastVerdict = "stuck";
                        LastVerdictDetail = $"[Visit {visits}] After redirect to '{scene}': " + ConversationDriver.LastStuckDescription;
                        yield break;
                    }
                    if (ConversationDriver.LastUsedQuitToHomeRecovery)
                        trace.Add($"[FINDING][Visit {visits}] Needed Quit-to-Home recovery after redirect to '{scene}' -- that conversation didn't return to Home on its own.");
                    continue;
                }

                var action = HomeHubPolicy.ChooseAction();
                float dayPhaseBefore = StatsManager.Get_Numbered_Stat("DayPhase");
                float dayOffsetBefore = StatsManager.Get_Numbered_Stat("DayOffset");
                float weekBefore = week;
                trace.Add($"[Visit {visits}] Week={week} DayPhase={dayPhaseBefore} DayOffset={dayOffsetBefore} action={action.Description}");
                WriteReport(trace, "running", $"In progress -- visit {visits}, about to try action '{action.Description}'.");

                action.Invoke();

                float actionIdleStartTime = Time.realtimeSinceStartup;
                bool progressed = false;
                while (Time.realtimeSinceStartup - actionIdleStartTime < IdleTimeoutSeconds)
                {
                    yield return null;
                    if (SceneManager.GetActiveScene().name != "Home" ||
                        StatsManager.Get_Numbered_Stat("Week") != weekBefore ||
                        StatsManager.Get_Numbered_Stat("DayPhase") != dayPhaseBefore ||
                        StatsManager.Get_Numbered_Stat("DayOffset") != dayOffsetBefore)
                    {
                        progressed = true;
                        break;
                    }
                }

                if (!progressed)
                {
                    LastVerdict = "stuck";
                    LastVerdictDetail = $"[Visit {visits}] No scene or time-stat change after action '{action.Description}'.";
                    yield break;
                }

                if (SceneManager.GetActiveScene().name != "Home")
                {
                    var drive = ConversationDriver.DriveUntilSceneIs("Home", IdleTimeoutSeconds);
                    while (drive.MoveNext()) yield return drive.Current;
                    if (HandleIfReachedMainMenu($"[Visit {visits}] After action '{action.Description}'")) yield break;
                    if (!ConversationDriver.LastDriveSucceeded)
                    {
                        LastVerdict = "stuck";
                        LastVerdictDetail = $"[Visit {visits}] After action '{action.Description}': " + ConversationDriver.LastStuckDescription;
                        yield break;
                    }
                    if (ConversationDriver.LastUsedQuitToHomeRecovery)
                        trace.Add($"[FINDING][Visit {visits}] Needed Quit-to-Home recovery after action '{action.Description}' -- that conversation didn't return to Home on its own.");
                }
            }

            LastVerdict = "inconclusive_safety_cap";
            LastVerdictDetail = $"Hit the {MaxHomeVisits}-visit safety cap without reaching the ending or getting stuck.";
        }

        private static void WriteReport(List<string> trace, string verdict, string detail)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Playthrough Simulation");
            sb.AppendLine();
            sb.AppendLine("Generated by the Unity PlayMode playthrough simulator " +
                          "(`Assets/Scripts/Simulation/PlaythroughSimRunner.cs`), driven by a single `[UnityTest]` " +
                          "(`Assets/Tests/Simulation/PlaythroughSimulationTests.cs`).");
            sb.AppendLine();
            sb.AppendLine("**This is a single deterministic trace (first-available-choice policy), not an " +
                          "exhaustive search of all conversation branches** -- mirrors the caveat language used " +
                          "by the static audit tool (`Tools/audit_vn_conversations.py`).");
            sb.AppendLine();
            sb.AppendLine($"## Verdict: {verdict}");
            sb.AppendLine();
            sb.AppendLine(detail);
            sb.AppendLine();
            sb.AppendLine("## Trace");
            sb.AppendLine();
            foreach (var line in trace)
                sb.AppendLine("- " + line);

            string outDir = Path.Combine(Application.dataPath, "..", "Docs", "VNEngine Audit");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "PlaythroughSimulation.md");
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log($"[PlaythroughSimRunner] Wrote report to {outPath}. Verdict: {verdict}. Detail: {detail}");
        }
    }
}
#endif
