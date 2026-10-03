#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VNEngine.EditorTools
{
    // Migrates legacy ChoiceNode (raw Button_Events UnityEvent wiring) to the
    // current ShowChoiceNode (Choice.nextConversation field), scoped to only the
    // ChoiceNodes living under each open scene's "Conversations" GameObject.
    //
    // Mirrors the eligibility rules implemented independently in
    // Tools/audit_choice_migration.py (the Python dry-run you should run and
    // read FIRST, before using this). Only migrates a ChoiceNode when every
    // wired button does nothing but call ConversationManager.Start_Conversation;
    // anything else is left untouched and reported as flagged for manual review.
    //
    // See Docs/VNEngine Audit/Choice Migration/README.md for the full rules and
    // the validation steps (diff this tool's report against the Python dry-run's
    // migrate_fileids.json by hierarchy path before trusting a run).
    public static class ChoiceNodeMigrator
    {
        private const string ConversationsRootName = "Conversations";

        [MenuItem("VN Engine/Migrate Legacy Choice Nodes (Open Scenes)")]
        public static void MigrateOpenScenes()
        {
            int totalMigrated = 0, totalFlagged = 0, totalSkippedScenes = 0;

            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                Scene scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                GameObject conversationsRoot = scene.GetRootGameObjects()
                    .FirstOrDefault(go => go.name == ConversationsRootName);
                if (conversationsRoot == null)
                {
                    totalSkippedScenes++;
                    continue;
                }

                var (migratedLines, flaggedLines) = MigrateScene(scene, conversationsRoot);
                totalMigrated += migratedLines.Count;
                totalFlagged += flaggedLines.Count;

                WriteSceneReport(scene.name, migratedLines, flaggedLines);
            }

            EditorUtility.DisplayDialog(
                "Choice Node Migration",
                $"Migrated {totalMigrated}, flagged {totalFlagged} for review"
                + (totalSkippedScenes > 0 ? $"\n({totalSkippedScenes} open scene(s) had no '{ConversationsRootName}' GameObject and were skipped)" : "")
                + "\n\nSee Docs/VNEngine Audit/Choice Migration/<Scene>-editor-run.md",
                "OK");
        }

        private static (List<string> migrated, List<string> flagged) MigrateScene(Scene scene, GameObject conversationsRoot)
        {
            var migrated = new List<string>();
            var flagged = new List<string>();

            ChoiceNode[] nodes = conversationsRoot.GetComponentsInChildren<ChoiceNode>(true);
            if (nodes.Length == 0)
                return (migrated, flagged);

            Undo.SetCurrentGroupName("Migrate ChoiceNode -> ShowChoiceNode");
            int undoGroup = Undo.GetCurrentGroup();
            bool changedAnything = false;

            foreach (ChoiceNode node in nodes)
            {
                string path = HierarchyPath(conversationsRoot.transform, node.transform);
                var (verdict, reasons, choices) = Classify(node);

                if (verdict == Verdict.Flagged)
                {
                    flagged.Add($"| {path} | {string.Join("; ", reasons)} |");
                    continue;
                }

                GameObject go = node.gameObject;
                if (PrefabUtility.IsPartOfPrefabInstance(go))
                {
                    // Defensive: no ChoiceNode in the project is a prefab instance as of this
                    // writing (verified against all 9 location scenes), but don't silently
                    // touch one if the data ever changes -- flag it instead of guessing
                    // whether PrefabUtility.UnpackPrefabInstance is needed here.
                    flagged.Add($"| {path} | node is part of a PrefabInstance — skipped, needs manual review |");
                    continue;
                }

                ShowChoiceNode show = ObjectFactory.AddComponent<ShowChoiceNode>(go);
                show.choices = choices;
                Undo.DestroyObjectImmediate(node);
                changedAnything = true;

                string choiceSummary = string.Join("; ", choices.Select(c =>
                    $"\"{c.text}\" → {(c.nextConversation != null ? c.nextConversation.name : "(null)")}"));
                migrated.Add($"| {path} | {choiceSummary} |");
            }

            Undo.CollapseUndoOperations(undoGroup);
            if (changedAnything)
                EditorSceneManager.MarkSceneDirty(scene);

            return (migrated, flagged);
        }

        private enum Verdict { Migrate, Flagged }

        private static (Verdict verdict, List<string> reasons, List<ShowChoiceNode.Choice> choices) Classify(ChoiceNode node)
        {
            var reasons = new List<string>();
            var choices = new List<ShowChoiceNode.Choice>();

            if (!string.IsNullOrEmpty(node.Name_Of_Choice))
                reasons.Add($"non-empty banner text: '{node.Name_Of_Choice}'");
            if (node.Localize_Choice_Text)
                reasons.Add("Localize_Choice_Text=true (no mapping)");

            // ChoiceNode.Running() only ever iterates up to Number_Of_Choices -- any
            // button beyond that is stale/unreachable data and must be ignored even
            // if it still happens to carry a wired listener.
            int count = Mathf.Min(node.Number_Of_Choices, node.Button_Events.Length);
            for (int i = 0; i < count; i++)
            {
                var ev = node.Button_Events[i];
                int callCount = ev.GetPersistentEventCount();
                if (callCount == 0)
                    continue; // unwired button: skipped, matches runtime behavior

                if (callCount > 1)
                {
                    reasons.Add($"button {i}: multiple listeners ({callCount})");
                    continue;
                }

                Object target = ev.GetPersistentTarget(0);
                string method = ev.GetPersistentMethodName(0);

                if (method == "Start_Conversation_Partway_Through")
                {
                    reasons.Add($"button {i}: Start_Conversation_Partway_Through jump (no ShowChoiceNode equivalent)");
                    continue;
                }

                var targetConversation = target as ConversationManager;
                if (method != "Start_Conversation" || targetConversation == null)
                {
                    string typeName = target != null ? target.GetType().Name : "null";
                    reasons.Add($"button {i}: other action ({typeName}.{method})");
                    continue;
                }

                bool hasRequirement = i < node.Has_Requirements.Length
                    && node.Has_Requirements[i] != Choice_Stat_Requirement.No_Requirement;
                bool hasImage = i < node.choice_button_images.Length
                    && node.choice_button_images[i] != null;
                if (hasRequirement)
                    reasons.Add($"button {i}: stat requirement");
                if (hasImage)
                    reasons.Add($"button {i}: custom button image");
                if (hasRequirement || hasImage)
                    continue;

                string text = i < node.Button_Text.Length ? node.Button_Text[i] : "";
                choices.Add(new ShowChoiceNode.Choice { text = text, nextConversation = targetConversation });
            }

            return (reasons.Count == 0 ? Verdict.Migrate : Verdict.Flagged, reasons, choices);
        }

        private static string HierarchyPath(Transform root, Transform node)
        {
            var names = new List<string>();
            Transform t = node;
            while (t != null && t != root)
            {
                names.Add(t.name);
                t = t.parent;
            }
            names.Add(root.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static void WriteSceneReport(string sceneName, List<string> migrated, List<string> flagged)
        {
            var lines = new List<string>
            {
                $"# {sceneName} — Choice Node Migration (Editor Run)",
                "",
                "Scope: `ChoiceNode` instances under the `Conversations` GameObject only.",
                "",
                "Generated by `Assets/Editor/ChoiceNodeMigrator.cs` (VN Engine > Migrate Legacy Choice Nodes (Open Scenes)).",
                "",
                "Diff this file's paths against `Docs/VNEngine Audit/Choice Migration/migrate_fileids.json` "
                    + "(the Python dry-run's output) before trusting this run -- they should agree.",
                "",
                "## Migrated",
                "",
            };

            if (migrated.Count > 0)
            {
                lines.Add("| Path | Resulting choices |");
                lines.Add("|---|---|");
                lines.AddRange(migrated);
            }
            else
            {
                lines.Add("_None in this scene._");
            }

            lines.Add("");
            lines.Add("## Flagged");
            lines.Add("");

            if (flagged.Count > 0)
            {
                lines.Add("| Path | Reasons |");
                lines.Add("|---|---|");
                lines.AddRange(flagged);
            }
            else
            {
                lines.Add("_None in this scene._");
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string outDir = Path.Combine(projectRoot, "Docs", "VNEngine Audit", "Choice Migration");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, $"{sceneName}-editor-run.md");
            File.WriteAllText(outPath, string.Join("\n", lines));
        }
    }
}
#endif
