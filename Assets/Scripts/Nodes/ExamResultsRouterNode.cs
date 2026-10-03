using System.Collections.Generic;
using UnityEngine;

namespace VNEngine
{
    public class ExamResultsRouterNode : Node
    {
        [System.Serializable]
        public class ScoreRoute
        {
            public int minScore = 0;                 // inclusive, on 0–4 normalized scale
            public ConversationManager conversation;
        }

        [Header("Stat Keys")]
        public string gradesKey          = "Grades";

        [Header("Score Tier Routes (highest minScore that matches wins)")]
        public List<ScoreRoute> routes = new List<ScoreRoute>();

        [Header("Fallback")]
        public ConversationManager fallbackConversation;

        [Header("Optional: Update Grades")]
        public bool updateGrades = true;

        public override void Run_Node()
        {
            if (updateGrades)
                ApplyScoreToGrades();

            float combinedGrade = StatsManager.Get_Numbered_Stat(gradesKey);
            var chosen = PickRoute(routes, (int)combinedGrade) ?? fallbackConversation;

            if (chosen != null)
            {
                chosen.Start_Conversation();
                go_to_next_node = false;
                Finish_Node();
                return;
            }

            go_to_next_node = true;
            Finish_Node();
        }

        private ConversationManager PickRoute(List<ScoreRoute> list, int score)
        {
            if (list == null || list.Count == 0) return null;

            ConversationManager best = null;
            int bestMin = int.MinValue;

            foreach (var r in list)
            {
                if (r == null || r.conversation == null) continue;
                if (score >= r.minScore && r.minScore >= bestMin)
                {
                    bestMin = r.minScore;
                    best = r.conversation;
                }
            }
            return best;
        }

        // The manager records the exam's GPA points when it ends (the exam id is cleared by then),
        // so here we only refresh the class grade from the stored midterm/final halves.
        private void ApplyScoreToGrades()
        {
            StatsManager.Set_Numbered_Stat(gradesKey, GradeCalculator.ClassGpa());
        }

        public override void Button_Pressed() { }
    }
}
