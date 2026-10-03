using UnityEngine;
using UnityEditor;

namespace VNEngine
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(CloserLookNode))]
    public class CloserLookNodeEditor : Editor
    {
        private SerializedProperty m_image;
        private SerializedProperty m_caption_text;
        private SerializedProperty m_fade_in;
        private SerializedProperty m_fade_in_time;
        private SerializedProperty m_hide_dialogue_ui;

        private void OnEnable()
        {
            m_image = serializedObject.FindProperty("image");
            m_caption_text = serializedObject.FindProperty("caption_text");
            m_fade_in = serializedObject.FindProperty("fade_in");
            m_fade_in_time = serializedObject.FindProperty("fade_in_time");
            m_hide_dialogue_ui = serializedObject.FindProperty("hide_dialogue_ui");
        }

        override public void OnInspectorGUI()
        {
            serializedObject.Update();

            var node = target as CloserLookNode;

            VNNodeEditorGUI.DrawCategoryBanner(VNNodeEditorGUI.NodeCategory.Background);

            EditorGUILayout.PropertyField(m_image, new GUIContent("Image", "The image to show full-screen, dimmed behind it. The player dismisses it by clicking anywhere or pressing the submit key."));
            if (m_image.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("An Image must be assigned for this node to do anything.", MessageType.Warning);
            }

            EditorGUILayout.PropertyField(m_caption_text, new GUIContent("Caption", "Optional text shown below the image. Leave blank to show only the image."));

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(m_fade_in, new GUIContent("Fade in", "Fade the overlay in instead of showing it instantly."));
            if (node.fade_in)
                EditorGUILayout.PropertyField(m_fade_in_time, new GUIContent("Fade in time (seconds)", "How long the fade in takes."));

            EditorGUILayout.PropertyField(m_hide_dialogue_ui, new GUIContent("Hide dialogue UI", "Hides the dialogue/speaker panels while the image is shown, and restores them once it's dismissed."));

            serializedObject.ApplyModifiedProperties();
        }
    }
}
