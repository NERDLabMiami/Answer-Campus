#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;

namespace VNEngine.EditorTools
{
    [CustomEditor(typeof(Node), true)] // Applies to Node and all subclasses
    [CanEditMultipleObjects]
    public class NodeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var t in targets)
                {
                    EditorUtility.SetDirty((Node)t);
                    EditorSceneManager.MarkSceneDirty(((Node)t).gameObject.scene);
                }
            }
        }
    }

    [InitializeOnLoad]
    public static class AutoVNNodeDirtyHook
    {
        static AutoVNNodeDirtyHook()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
                MarkAllVNNodesDirty("Exiting Play Mode");
        }

        static void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
        {
            MarkAllVNNodesDirty("Scene Saving");
        }

        private static void MarkAllVNNodesDirty(string reason)
        {
            var nodes = GameObject.FindObjectsByType<Node>(FindObjectsInactive.Include); // include inactive
            foreach (var node in nodes)
            {
                EditorUtility.SetDirty(node);
                EditorSceneManager.MarkSceneDirty(node.gameObject.scene);
            }
            Debug.Log($"[VNEngine] Automatically marked {nodes.Length} node(s) dirty ({reason}).");
        }
    }
}
#endif