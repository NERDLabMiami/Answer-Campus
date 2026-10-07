#!/usr/bin/env bash
# Runs a playthrough-simulation test (Assets/Scripts/Simulation/) in Unity batch mode.
#
# Requires Unity's Editor to be CLOSED for this project -- Unity will not open a second
# instance against a project that's already open interactively. If you have the project
# open, instead use Test Runner > PlayMode > Run All inside that Editor session.
#
# IMPORTANT: deliberately does NOT pass -quit. -runTests already quits on its own once the
# run finishes; adding -quit explicitly races against it and can win, causing Unity to exit
# before the (slower, PlayMode-domain-reload-requiring) test machinery actually starts --
# confirmed the hard way: a run with -quit present exited in under a minute having never
# shown any sign the test runner activated at all.
#
# Usage: Tools/run_playthrough_simulation.sh [testFilter] [path-to-Unity-executable]
#   testFilter defaults to the single-baseline test; pass the path-exploration test's full
#   name to run that instead.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TEST_FILTER="${1:-AnswerCampus.Simulation.Tests.PlaythroughSimulationTests.FullPlaythrough_DoesNotDeadEnd_AndReachesSemesterEnd}"
UNITY_BIN="${2:-/Applications/Unity/Hub/Editor/6000.5.8f1/Unity.app/Contents/MacOS/Unity}"

if [ ! -x "$UNITY_BIN" ]; then
  echo "Unity executable not found or not executable at: $UNITY_BIN" >&2
  echo "Pass the correct path as the second argument." >&2
  exit 1
fi

"$UNITY_BIN" \
  -batchmode -nographics \
  -projectPath "$REPO_ROOT" \
  -runTests -testPlatform PlayMode \
  -testFilter "$TEST_FILTER" \
  -testResults /tmp/answer-campus-playthrough-results.xml \
  -logFile /tmp/answer-campus-playthrough.log

echo "Unity exited with code $?"
echo "Test results: /tmp/answer-campus-playthrough-results.xml"
echo "Full log:     /tmp/answer-campus-playthrough.log"
echo "Report:       Docs/VNEngine Audit/PlaythroughSimulation.md or PathExploration.md"
