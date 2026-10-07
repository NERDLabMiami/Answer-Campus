#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VNEngine;

namespace AnswerCampus.Simulation
{
    public class HomeAction
    {
        public string Description;
        public Action Invoke;
    }

    // Decides what to do on a given Home.unity visit, in priority order:
    //   (a) go to class, if it's morning and class hasn't been attended this week --
    //       class is the one authored half-day action (its "Set Week" checkpoints are
    //       consistently configured HomeReturnState.SecondHalfOfWeek: DayPhase 0->1,
    //       same week) that unlocks a second, afternoon action. Must be checked BEFORE
    //       map pins: every map-pin/routed conversation's checkpoint defaults to
    //       HomeReturnState.NewWeekMorning (full week jump, confirmed with zero
    //       exceptions across Dining Hall/Apartment/Green), so taking one first would
    //       always consume the day and permanently skip class.
    //   (b) play the scheduled football/cheer game, if one exists and is unplayed -- checked
    //       before free-roam wandering (c) because that candidate list is nearly always
    //       non-empty (everywhere except Apartment is freely walkable), so if this were
    //       checked after it, a pending game would get starved indefinitely and never get
    //       resolved via the safe bypass below. The bypass records a result directly
    //       instead of loading the real Cheer scene, since that minigame needs real timed
    //       input (catching/erasing falling letters) this driver can't provide -- loading it
    //       for real got the simulated player stuck on its intro screen with no way forward.
    //   (c) rotate through every other freely-walkable location (everywhere except
    //       Apartment, which Home.unity's Map.lockableLocations gates invite-only --
    //       confirmed the ONLY lockable entry there) instead of always grabbing whichever
    //       character pin exists first. A real player doesn't beeline for the nearest pin
    //       every single afternoon; they wander. Whichever location comes up, that scene's
    //       own Conversation Router either resolves a matching pin or falls through to its
    //       fallback conversations -- so rotating destinations is what actually exercises
    //       fallback content instead of one fast-advancing character's pin (e.g. BREANNA's)
    //       winning every visit and starving every other location's fallback conversations
    //       and every other character's pin of ever being reached.
    //   (d) otherwise, the study mini-game's completion hook, purely to advance time
    //       and keep the loop moving (a guaranteed-available fallback since it bypasses
    //       the Laptop UI's own visibility gating -- documented simplification, only
    //       affects time-filler pacing, not story-path content)
    public static class HomeHubPolicy
    {
        // Deterministic, not UnityEngine.Random: a path-exploration divergence run reaching
        // this call after a different number of prior choices could get a different rotation
        // outcome purely from drift, contaminating the comparison (same reasoning as the
        // football-outcome fix below). Reset per playthrough so every run starts identically.
        private static int _visitRotation;

        public static void ResetForNewRun() => _visitRotation = 0;

        public static HomeAction ChooseAction()
        {
            int week = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));
            float dayPhase = StatsManager.Get_Numbered_Stat("DayPhase");
            bool classAttended = StatsManager.Get_Boolean_Stat("ClassAttendedThisWeek");

            if (dayPhase < 1f && !classAttended)
            {
                return new HomeAction
                {
                    Description = "Go to class (Lecture Hall)",
                    Invoke = () =>
                    {
                        StatsManager.Set_Boolean_Stat("ClassAttendedThisWeek", true);
                        LocationRouter.Go("Lecture Hall");
                    },
                };
            }

            var game = FootballScheduler.GetThisWeeksGame(week);
            if (game != null && !game.played)
            {
                return new HomeAction
                {
                    Description = $"Play football/cheer game (week {week})",
                    Invoke = () =>
                    {
                        // Deterministic, not Random.value: UnityEngine.Random is a stateful
                        // sequence, so a path-exploration divergence run reaching this call
                        // after a different number of prior choices could get a different
                        // outcome purely from RNG drift, contaminating the comparison.
                        bool won = week % 2 == 0;
                        CheerGameManager.RecordGameResult(week, won, 14, 10);
                        StatsManager.Set_Boolean_Stat("JustReturnedFromGame", true);
                        StatsManager.Set_Numbered_Stat("DayPhase", 1f);
                        LocationRouter.Go("Home");
                    },
                };
            }

            // Mirrors Map.cs/MapAvailability.Build()'s own rules for what's clickable right
            // now: skip anything invite-gated with no current invite (Apartment, confirmed
            // the only entry in Home.unity's Map.lockableLocations) and anything time-gated
            // against the current half of the day (e.g. Lecture Hall's morningOnly) -- a
            // pin at a time-gated destination can't be routed into anyway, since the router
            // only clears a pin on a successful match.
            var pins = PhoneDataService.GetCharacterLocations();
            var pinnedNames = new HashSet<string>(pins.Select(p => p.location), StringComparer.Ordinal);

            var map = UnityEngine.Object.FindFirstObjectByType<Map>();
            var lockedNames = new HashSet<string>(StringComparer.Ordinal);
            if (map?.lockableLocations != null)
            {
                foreach (var loc in map.lockableLocations)
                    if (loc != null && !string.IsNullOrEmpty(loc.scene))
                        lockedNames.Add(loc.scene);
            }

            var excluded = new HashSet<string>(StringComparer.Ordinal)
            {
                "Home", "Lecture Hall", "Cheer", "Shuttle", "Football Game",
            };

            var candidates = PhoneDataService.GetAllLocationNames()
                .Where(name => !excluded.Contains(name))
                .Where(name => !lockedNames.Contains(name) || pinnedNames.Contains(name))
                .Where(name =>
                {
                    var locData = LocationData.Find(name);
                    return !(locData != null && locData.morningOnly && dayPhase != 0f);
                })
                .ToList();

            if (candidates.Count > 0)
            {
                string destination = candidates[_visitRotation % candidates.Count];
                _visitRotation++;
                bool hasPin = pinnedNames.Contains(destination);

                return new HomeAction
                {
                    Description = hasPin
                        ? $"NavigateOut to '{destination}' (routed conversation pending there)"
                        : $"NavigateOut to '{destination}' (exploring -- no pin, fallback content)",
                    Invoke = () => HomeCutsceneController.NavigateOut(destination),
                };
            }

            return new HomeAction
            {
                Description = "Study (time-advance fallback)",
                Invoke = () => HomeCutsceneController.Instance.OnStudyComplete(),
            };
        }
    }
}
#endif
