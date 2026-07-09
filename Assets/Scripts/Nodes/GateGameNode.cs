using System;
using System.Linq;
using UnityEngine;

namespace VNEngine
{
    [AddComponentMenu("Game Object/VN Engine/Branching/Gate Game Node")]
    public class GateGameNode : Node
    {
        [Header("Football Requirement")]
        public FootballRequirement footballRequirement = new FootballRequirement { check = FootballCheckType.None };

        [Header("On Success")]
        public ConversationManager successConversation;
        public bool continueCurrentOnSuccess = false;

        [Header("On Failure")]
        public ConversationManager failureConversation;
        public bool continueCurrentOnFailure = true;

        public override void Run_Node()
        {
            bool passed = EvaluateFootballRequirement();

            if (passed)
            {
                if (successConversation != null)
                {
                    successConversation.Start_Conversation();
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }

                if (!continueCurrentOnSuccess)
                {
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }
            }
            else
            {
                if (failureConversation != null)
                {
                    failureConversation.Start_Conversation();
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }

                if (!continueCurrentOnFailure)
                {
                    go_to_next_node = false;
                    Finish_Node();
                    return;
                }
            }

            Finish_Node();
        }

        private bool EvaluateFootballRequirement()
        {
            switch (footballRequirement.check)
            {
                case FootballCheckType.None:
                    return true;

                case FootballCheckType.IsWinningRecord:
                {
                    var r = GetFootballRecord();
                    return r.wins > r.losses;
                }

                case FootballCheckType.WinsAtLeast:
                {
                    var r = GetFootballRecord();
                    return r.wins >= Mathf.RoundToInt(footballRequirement.threshold);
                }

                case FootballCheckType.WinRateAtLeast:
                {
                    var r = GetFootballRecord();
                    return r.played > 0 && r.winRate >= footballRequirement.threshold;
                }

                default:
                    return true;
            }
        }

        private (int wins, int losses, int played, float winRate) GetFootballRecord()
        {
            string json = StatsManager.Get_String_Stat("FootballSchedule");
            if (string.IsNullOrEmpty(json)) return (0, 0, 0, 0f);

            FootballGameListWrapper wrapper = null;
            try { wrapper = JsonUtility.FromJson<FootballGameListWrapper>(json); }
            catch { /* ignore */ }

            if (wrapper?.games == null || wrapper.games.Count == 0)
                return (0, 0, 0, 0f);

            int wins = wrapper.games.Count(g => g.played && g.won);
            int losses = wrapper.games.Count(g => g.played && !g.won);
            int played = wins + losses;
            float winRate = played > 0 ? (float)wins / played : 0f;

            return (wins, losses, played, winRate);
        }
    }
}
