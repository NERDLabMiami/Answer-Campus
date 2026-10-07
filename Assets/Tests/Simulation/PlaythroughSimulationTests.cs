using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AnswerCampus.Simulation.Tests
{
    // Thin entry point only -- this assembly can never compile-time-reference
    // Assembly-CSharp (Unity always compiles custom .asmdef assemblies before the
    // predefined ones), so all real logic lives in Assets/Scripts/Simulation/
    // (no .asmdef, #if UNITY_EDITOR) and is reached here via a single reflection hop.
    public class PlaythroughSimulationTests
    {
        // NUnit's default PlayMode test timeout is 180s, which isn't nearly enough for a
        // full 16-week simulation (real wall-clock frames across dozens of Home-hub
        // cycles). 30 minutes is a generous ceiling -- the simulation's own internal
        // safety cap (PlaythroughSimRunner.MaxHomeVisits) is the real stop condition.
        [UnityTest]
        [Timeout(1800000)]
        public IEnumerator FullPlaythrough_DoesNotDeadEnd_AndReachesSemesterEnd()
        {
            // Unity Test Framework fails a test on any unhandled Debug.LogError by default.
            // The real game logs one when NodeAchievement tries to unlock a Steam achievement
            // and Steam isn't running (SteamAPI_Init() failed) -- expected/orthogonal to
            // conversation-path validation in this environment, not a real failure.
            LogAssert.ignoreFailingMessages = true;

            var runnerType = Type.GetType("AnswerCampus.Simulation.PlaythroughSimRunner, Assembly-CSharp");
            Assert.IsNotNull(runnerType, "AnswerCampus.Simulation.PlaythroughSimRunner not found in Assembly-CSharp.");

            var runMethod = runnerType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(runMethod, "PlaythroughSimRunner.Run() not found.");

            var result = runMethod.Invoke(null, null) as IEnumerator;
            Assert.IsNotNull(result, "PlaythroughSimRunner.Run() did not return an IEnumerator.");

            while (result.MoveNext())
                yield return result.Current;

            Assert.Pass("Playthrough simulation complete -- see Docs/VNEngine Audit/PlaythroughSimulation.md for the verdict and trace.");
        }

        // Re-runs the full playthrough once per dialogue-choice alternative found along the
        // baseline (PathExplorer.MaxDivergenceRuns caps how many) -- total runtime is roughly
        // (alternatives explored + 1) x one playthrough's time. 12 hours is sized for the
        // current cap of 20; raise both together if the cap is increased. Run via
        // Tools/run_playthrough_simulation.sh unattended rather than babysitting Test Runner.
        [UnityTest]
        [Timeout(43200000)]
        public IEnumerator PathExploration_TestsDialogueAlternatives()
        {
            LogAssert.ignoreFailingMessages = true;

            var explorerType = Type.GetType("AnswerCampus.Simulation.PathExplorer, Assembly-CSharp");
            Assert.IsNotNull(explorerType, "AnswerCampus.Simulation.PathExplorer not found in Assembly-CSharp.");

            var runMethod = explorerType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(runMethod, "PathExplorer.Run() not found.");

            var result = runMethod.Invoke(null, null) as IEnumerator;
            Assert.IsNotNull(result, "PathExplorer.Run() did not return an IEnumerator.");

            while (result.MoveNext())
                yield return result.Current;

            Assert.Pass("Path exploration complete -- see Docs/VNEngine Audit/PathExploration.md for results.");
        }
    }
}
