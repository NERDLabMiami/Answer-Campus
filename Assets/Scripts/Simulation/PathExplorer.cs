#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AnswerCampus.Simulation
{
    // Public glue entry point for systematic divergence exploration -- reached from
    // Assets/Tests/Simulation via the same single reflection hop as PlaythroughSimRunner.
    //
    // Strategy (systematic single-divergence; Home-hub action sequencing is unchanged from
    // PlaythroughSimRunner/HomeHubPolicy in every run): run the baseline playthrough once,
    // recording every real divergence point and which way the baseline went. Two kinds are
    // recorded independently (see ConversationDriver.Recording/GateRecording):
    //   - ShowChoiceNode encounters with 2+ visible, non-quit-looking options (player choice).
    //   - Gate*Node encounters (Assets/Scripts/Nodes/Gate*.cs) -- silent, state-driven
    //     branches (contact/affinity/traits/events/football/last-game-result), recorded via
    //     ConversationManager.OnBeforeNodeRuns since they have no visible UI to watch for.
    // Then, for each unchosen alternative at each divergence point (of either kind), re-run
    // the ENTIRE playthrough from scratch: replay the baseline's choices verbatim up to (or,
    // for a gate task, all the way through) that point, diverge once -- picking the other
    // choice, or forcing the gate's alternate branch by overwriting its backing game state --
    // then resume the normal policy for the rest of that run. This is bounded (one extra full
    // run per alternative actually found, not exponential) but each run replays the full
    // playthrough from scratch, so total runtime is roughly (alternatives explored + 1) x one
    // playthrough's time -- likely hours. See MaxDivergenceRuns and the plan's staging notes
    // before raising this.
    public static class PathExplorer
    {
        // Raised from the initial staged cap of 20 once per-run cost was confirmed
        // acceptable and the Main-Menu/timing fixes proved stable across 20 clean runs.
        // 104 alternatives currently exist in the baseline; headroom included for future
        // content growth. Per the plan's stated end goal, uncapped exploration is fine once
        // the per-run cost is known -- this is a generous cap rather than a true bound.
        private const int MaxDivergenceRuns = 200;

        public static IEnumerator Run()
        {
            var report = new Report();

            var baselineTrace = new List<string>();
            var baseline = PlaythroughSimRunner.RunBodySafely(baselineTrace);
            while (baseline.MoveNext()) yield return baseline.Current;

            report.BaselineTrace = baselineTrace;
            report.BaselineVerdict = PlaythroughSimRunner.LastVerdict;
            report.BaselineDetail = PlaythroughSimRunner.LastVerdictDetail;

            // Snapshot before any further run overwrites these (BeginNewRun() replaces them).
            var baselineChosenIndices = new List<int>(ConversationDriver.AllChosenIndices);
            var divergencePoints = new List<ChoiceEncounter>(ConversationDriver.Recording);
            var gateDivergencePoints = new List<GateEncounter>(ConversationDriver.GateRecording);

            // Unified task list so one cap/loop covers both dialogue-choice alternatives and
            // Gate*Node alternate branches (Assets/Scripts/Nodes/Gate*.cs) -- a choice task
            // replays baseline choices up to its encounter then diverges once; a gate task
            // replays baseline choices verbatim throughout (to reproduce identical state) and
            // forces one gate's alternate branch once via ScriptedGateOutcomes. Both resume
            // default policy for everything after the single forced divergence.
            //
            // Gate tasks are queued first, ahead of the (usually much larger) choice task
            // list, so a run surfaces fresh gate coverage quickly instead of only reaching it
            // after every already-proven choice alternative has re-run first.
            var tasks = new List<DivergenceTask>();
            foreach (var genc in gateDivergencePoints)
                foreach (var alt in genc.Alternatives)
                    tasks.Add(DivergenceTask.ForGate(genc, alt));
            foreach (var enc in divergencePoints)
                foreach (var alt in enc.Alternatives)
                    tasks.Add(DivergenceTask.ForChoice(enc, alt));

            report.TotalDivergencePoints = divergencePoints.Count;
            report.TotalAlternatives = divergencePoints.Sum(e => e.Alternatives.Count);
            report.TotalGateEncounters = gateDivergencePoints.Count;
            report.TotalGateAlternatives = gateDivergencePoints.Sum(e => e.Alternatives.Count);
            WriteReport(report);

            int cap = Mathf.Min(tasks.Count, MaxDivergenceRuns);
            for (int i = 0; i < cap; i++)
            {
                var task = tasks[i];

                List<int> scriptedChoices;
                Dictionary<int, int> scriptedGateOutcomes = null;
                if (task.Kind == TaskKind.Choice)
                {
                    scriptedChoices = baselineChosenIndices.GetRange(0, task.EncounterIndex);
                    scriptedChoices.Add(task.ChoiceAlternativeIndex);
                }
                else
                {
                    scriptedChoices = baselineChosenIndices;
                    scriptedGateOutcomes = new Dictionary<int, int> { { task.EncounterIndex, task.GateAlternativeBranchId } };
                }

                var divergenceTrace = new List<string>();
                var run = PlaythroughSimRunner.RunBodySafely(divergenceTrace, scriptedChoices, scriptedGateOutcomes);
                while (run.MoveNext()) yield return run.Current;

                string detail = PlaythroughSimRunner.LastVerdictDetail;
                if (task.Kind == TaskKind.Gate && !string.IsNullOrEmpty(ConversationDriver.LastGateForcingMismatch))
                    detail += " | " + ConversationDriver.LastGateForcingMismatch;

                report.Results.Add(new DivergenceResult
                {
                    Kind = task.Kind,
                    EncounterIndex = task.EncounterIndex,
                    ConversationName = task.ConversationName,
                    BaselineChoiceText = task.BaselineLabel,
                    AlternativeText = task.AlternativeLabel,
                    Verdict = PlaythroughSimRunner.LastVerdict,
                    Detail = detail,
                    Trace = divergenceTrace,
                });
                WriteReport(report);
            }

            report.RunsExploredOfTotal = cap;
            WriteReport(report);
        }

        private enum TaskKind { Choice, Gate }

        // Carries either a choice divergence or a gate divergence under one shape so the cap
        // and run loop above don't need to be duplicated per kind.
        private class DivergenceTask
        {
            public TaskKind Kind;
            public int EncounterIndex;
            public string ConversationName;
            public string BaselineLabel;
            public string AlternativeLabel;
            public int ChoiceAlternativeIndex;
            public int GateAlternativeBranchId;

            public static DivergenceTask ForChoice(ChoiceEncounter enc, ChoiceAlternative alt) => new DivergenceTask
            {
                Kind = TaskKind.Choice,
                EncounterIndex = enc.EncounterIndex,
                ConversationName = enc.ConversationName,
                BaselineLabel = enc.ChosenText,
                AlternativeLabel = alt.Text,
                ChoiceAlternativeIndex = alt.Index,
            };

            public static DivergenceTask ForGate(GateEncounter enc, GateBranch alt) => new DivergenceTask
            {
                Kind = TaskKind.Gate,
                EncounterIndex = enc.EncounterIndex,
                ConversationName = $"{enc.ConversationName} ({enc.GateType})",
                BaselineLabel = enc.TakenBranchLabel,
                AlternativeLabel = alt.Label,
                GateAlternativeBranchId = alt.Id,
            };
        }

        private class DivergenceResult
        {
            public TaskKind Kind;
            public int EncounterIndex;
            public string ConversationName;
            public string BaselineChoiceText;
            public string AlternativeText;
            public string Verdict;
            public string Detail;
            public List<string> Trace;
        }

        private class Report
        {
            public List<string> BaselineTrace = new List<string>();
            public string BaselineVerdict = "running";
            public string BaselineDetail = "";
            public int TotalDivergencePoints;
            public int TotalAlternatives;
            public int TotalGateEncounters;
            public int TotalGateAlternatives;
            public int RunsExploredOfTotal = -1;
            public List<DivergenceResult> Results = new List<DivergenceResult>();
        }

        private static void WriteReport(Report report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Path Exploration");
            sb.AppendLine();
            sb.AppendLine("Generated by the Unity PlayMode path explorer " +
                          "(`Assets/Scripts/Simulation/PathExplorer.cs`), driven by a single `[UnityTest]` " +
                          "(`Assets/Tests/Simulation/PlaythroughSimulationTests.cs`).");
            sb.AppendLine();
            sb.AppendLine("**Systematic single-divergence.** For each real divergence point the baseline hit, this " +
                          "re-runs the full playthrough from scratch, replaying the baseline verbatim up to that " +
                          "point, diverging once, then resuming normal policy. This is not an exhaustive search of " +
                          "the full combinatorial space (see `Tools/audit_vn_conversations.py`'s own caveat language " +
                          "for the same spirit) -- it only tests one changed decision at a time. Two independent " +
                          "divergence sources are covered:");
            sb.AppendLine();
            sb.AppendLine("- **Choice divergences** -- `ShowChoiceNode` dialogue alternatives (2+ visible, " +
                          "non-quit-looking options). Home-hub action sequencing (which location/mini-game is " +
                          "chosen each visit) is unchanged from the baseline policy in every run, out of scope here.");
            sb.AppendLine("- **Gate divergences** -- the state-driven `Gate*Node` branches under " +
                          "`Assets/Scripts/Nodes/Gate*.cs` (contact, affinity, traits, custom events, football " +
                          "record, last-game-result). These have no player input, so each alternative is produced " +
                          "by directly overwriting the backing `StatsManager`/`Friend`/`GameEvents`/football-schedule " +
                          "state right before the gate runs (`ConversationDriver.ForceGateBranch`), then letting the " +
                          "rest of the run proceed normally. **Known limitation:** `GateAffinityNode`'s branches are " +
                          "first-match-wins, so forcing branch *i* can be silently overridden by an earlier branch " +
                          "on the same character if that character's authored tiers mix comparison directions. When " +
                          "this happens the forced branch didn't actually match what the row claims to test -- it's " +
                          "called out in that row's own Detail column rather than solved generically.");
            sb.AppendLine();

            sb.AppendLine($"## Baseline: {report.BaselineVerdict}");
            sb.AppendLine();
            sb.AppendLine(report.BaselineDetail);
            sb.AppendLine();
            int totalAll = report.TotalAlternatives + report.TotalGateAlternatives;
            sb.AppendLine($"Found **{report.TotalDivergencePoints}** choice divergence points ({report.TotalAlternatives} " +
                          $"alternatives) and **{report.TotalGateEncounters}** gate encounters ({report.TotalGateAlternatives} " +
                          $"alternatives) -- {totalAll} total.");
            if (report.RunsExploredOfTotal >= 0)
            {
                sb.AppendLine();
                sb.AppendLine(report.RunsExploredOfTotal < totalAll
                    ? $"Explored {report.RunsExploredOfTotal} of {totalAll} (capped by `MaxDivergenceRuns` -- raise it to explore the rest)."
                    : $"Explored all {report.RunsExploredOfTotal} alternatives found.");
            }
            sb.AppendLine();

            sb.AppendLine("## Divergence results");
            sb.AppendLine();
            if (report.Results.Count == 0)
            {
                sb.AppendLine("_None run yet._");
            }
            else
            {
                // Surface anything other than a clean ending first.
                var ordered = new List<DivergenceResult>(report.Results);
                ordered.Sort((a, b) =>
                {
                    bool aOk = a.Verdict == "reached_ending";
                    bool bOk = b.Verdict == "reached_ending";
                    if (aOk != bOk) return aOk ? 1 : -1;
                    return a.EncounterIndex.CompareTo(b.EncounterIndex);
                });

                sb.AppendLine("| Kind | Encounter | Conversation | Baseline | Alternative tried | Verdict | Detail |");
                sb.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var r in ordered)
                {
                    sb.AppendLine($"| {r.Kind} | {r.EncounterIndex} | {Escape(r.ConversationName)} | {Escape(r.BaselineChoiceText)} | " +
                                  $"{Escape(r.AlternativeText)} | {r.Verdict} | {Escape(r.Detail)} |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Baseline trace");
            sb.AppendLine();
            foreach (var line in report.BaselineTrace)
                sb.AppendLine("- " + line);

            string outDir = Path.Combine(Application.dataPath, "..", "Docs", "VNEngine Audit");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "PathExploration.md");
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log($"[PathExplorer] Wrote report to {outPath}. {report.Results.Count} divergence run(s) so far.");
        }

        private static string Escape(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
        }
    }
}
#endif
