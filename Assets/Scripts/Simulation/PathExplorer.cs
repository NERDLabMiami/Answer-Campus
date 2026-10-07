#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AnswerCampus.Simulation
{
    // Public glue entry point for systematic dialogue-choice exploration -- reached from
    // Assets/Tests/Simulation via the same single reflection hop as PlaythroughSimRunner.
    //
    // Strategy (systematic single-divergence, scoped to ShowChoiceNode alternatives only --
    // Home-hub action sequencing is unchanged from PlaythroughSimRunner/HomeHubPolicy):
    // run the baseline playthrough once, recording every real divergence point (a
    // ShowChoiceNode encounter with 2+ visible, non-quit-looking options) and which choice
    // the baseline took. Then, for each unchosen alternative at each divergence point, re-run
    // the ENTIRE playthrough from scratch with a script that replays the baseline's choices
    // verbatim up to that point, diverges once, then resumes the normal policy for the rest
    // of that run. This is bounded (one extra full run per alternative actually found, not
    // exponential) but each run replays the full playthrough from scratch, so total runtime
    // is roughly (alternatives explored + 1) x one playthrough's time -- likely hours. See
    // MaxDivergenceRuns and the plan's staging notes before raising this.
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

            var tasks = new List<(ChoiceEncounter enc, ChoiceAlternative alt)>();
            foreach (var enc in divergencePoints)
                foreach (var alt in enc.Alternatives)
                    tasks.Add((enc, alt));

            report.TotalDivergencePoints = divergencePoints.Count;
            report.TotalAlternatives = tasks.Count;
            WriteReport(report);

            int cap = Mathf.Min(tasks.Count, MaxDivergenceRuns);
            for (int i = 0; i < cap; i++)
            {
                var (enc, alt) = tasks[i];

                var script = baselineChosenIndices.GetRange(0, enc.EncounterIndex);
                script.Add(alt.Index);

                var divergenceTrace = new List<string>();
                var run = PlaythroughSimRunner.RunBodySafely(divergenceTrace, script);
                while (run.MoveNext()) yield return run.Current;

                report.Results.Add(new DivergenceResult
                {
                    EncounterIndex = enc.EncounterIndex,
                    ConversationName = enc.ConversationName,
                    BaselineChoiceText = enc.ChosenText,
                    AlternativeText = alt.Text,
                    Verdict = PlaythroughSimRunner.LastVerdict,
                    Detail = PlaythroughSimRunner.LastVerdictDetail,
                    Trace = divergenceTrace,
                });
                WriteReport(report);
            }

            report.RunsExploredOfTotal = cap;
            WriteReport(report);
        }

        private class DivergenceResult
        {
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
            sb.AppendLine("**Systematic single-divergence, scoped to `ShowChoiceNode` dialogue alternatives only** " +
                          "-- Home-hub action sequencing (which location/mini-game is chosen each visit) is unchanged " +
                          "from the baseline policy in every run here, out of scope for this pass. For each real " +
                          "divergence point (a choice with 2+ visible, non-quit-looking options) the baseline hit, " +
                          "this re-runs the full playthrough from scratch, replaying the baseline's choices verbatim " +
                          "up to that point, picking the alternative once, then resuming normal policy. This is not " +
                          "an exhaustive search of the full combinatorial space (see `Tools/audit_vn_conversations.py`'s " +
                          "own caveat language for the same spirit) -- it only tests one changed choice at a time.");
            sb.AppendLine();

            sb.AppendLine($"## Baseline: {report.BaselineVerdict}");
            sb.AppendLine();
            sb.AppendLine(report.BaselineDetail);
            sb.AppendLine();
            sb.AppendLine($"Found **{report.TotalDivergencePoints}** divergence points ({report.TotalAlternatives} total alternatives).");
            if (report.RunsExploredOfTotal >= 0)
            {
                sb.AppendLine();
                sb.AppendLine(report.RunsExploredOfTotal < report.TotalAlternatives
                    ? $"Explored {report.RunsExploredOfTotal} of {report.TotalAlternatives} (capped by `MaxDivergenceRuns` -- raise it to explore the rest)."
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

                sb.AppendLine("| Encounter | Conversation | Baseline chose | Alternative tried | Verdict | Detail |");
                sb.AppendLine("|---|---|---|---|---|---|");
                foreach (var r in ordered)
                {
                    sb.AppendLine($"| {r.EncounterIndex} | {Escape(r.ConversationName)} | {Escape(r.BaselineChoiceText)} | " +
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
