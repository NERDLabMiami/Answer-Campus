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
        public string examRawScoreKey    = "ExamRawScore";
        public string studyBestScoreKey  = "StudyGameScore";
        public string currentExamIdKey   = "CurrentExamId";
        public string midtermsExamId     = "EXAM_MIDTERMS";
        public string finalsExamId       = "EXAM_FINALS";
        public string gradesKey          = "Grades";
        public string midtermScoreKey    = "MidtermScore";
        public string finalScoreKey      = "FinalScore";

        [Header("Score Tier Routes (highest minScore that matches wins)")]
        public List<ScoreRoute> routes = new List<ScoreRoute>();

        [Header("Fallback")]
        public ConversationManager fallbackConversation;

        [Header("Optional: Update Grades")]
        public bool updateGrades = true;

        public override void Run_Node()
        {
            int examScore = (int)StatsManager.Get_Numbered_Stat(examRawScoreKey);

            if (updateGrades)
                ApplyScoreToGrades(examScore);

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

        private void ApplyScoreToGrades(int examScore)
        {
            string examId = StatsManager.Get_String_Stat(currentExamIdKey);

            float normalizedExam = NormalizeExamScore(examScore);
            if (examId == midtermsExamId)
                StatsManager.Set_Numbered_Stat(midtermScoreKey, normalizedExam);
            else if (examId == finalsExamId)
                StatsManager.Set_Numbered_Stat(finalScoreKey, normalizedExam);
            else
                return;

            float studyNorm = NormalizeStudyScore((int)StatsManager.Get_Numbered_Stat(studyBestScoreKey));
            float mid = StatsManager.Get_Numbered_Stat(midtermScoreKey);
            float fin = StatsManager.Get_Numbered_Stat(finalScoreKey);

            float combined;
            if (fin > 0f)
                combined = (studyNorm + mid + fin) / 3f;
            else if (mid > 0f)
                combined = (studyNorm + mid) / 2f;
            else
                combined = studyNorm;

            combined = Mathf.Clamp(combined, 0f, 4f);
            StatsManager.Set_Numbered_Stat(gradesKey, combined);
        }

        private float NormalizeStudyScore(int raw)
        {
            if (raw >= 5) return 4f;
            if (raw == 4) return 3f;
            if (raw == 3) return 2f;
            if (raw >= 1) return 1f;
            return 0f;
        }

        private float NormalizeExamScore(int raw) => Mathf.Clamp(raw, 0, 4);

        public override void Button_Pressed() { }
    }
}
