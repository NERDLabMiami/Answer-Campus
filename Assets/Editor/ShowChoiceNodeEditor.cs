#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VNEngine;

[CustomEditor(typeof(ShowChoiceNode))]
public class ShowChoiceNodeEditor : Editor
{
    private SerializedProperty _choicesProp;
    private SerializedProperty _hideDialogueUIProp;
    private SerializedProperty _traitRegistryProp;

    private void OnEnable()
    {
        _choicesProp = serializedObject.FindProperty("choices");
        _hideDialogueUIProp = serializedObject.FindProperty("hideDialogueUI");
        _traitRegistryProp = serializedObject.FindProperty("traitRegistry");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(_hideDialogueUIProp);
        GUILayout.Space(6);

        string[] keys = VNNodeEditorGUI.ResolveTraitKeys(_traitRegistryProp);

        EditorGUILayout.LabelField("Choices", EditorStyles.boldLabel);

        int removeIndex = -1;
        for (int i = 0; i < _choicesProp.arraySize; i++)
        {
            var el = _choicesProp.GetArrayElementAtIndex(i);

            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                EditorGUILayout.PropertyField(el.FindPropertyRelative("text"), new GUIContent("Text"), true);
                EditorGUILayout.PropertyField(el.FindPropertyRelative("nextConversation"), new GUIContent("Next Conversation"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("enableLogging"), new GUIContent("Enable Logging"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("label"), new GUIContent("Label"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("category"), new GUIContent("Category"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("variant"), new GUIContent("Variant"));

                GUILayout.Space(4);
                VNNodeEditorGUI.DrawTraitRequirements(el.FindPropertyRelative("requirements"), keys);

                GUILayout.Space(4);
                if (GUILayout.Button("Remove Choice", GUILayout.MaxWidth(120)))
                    removeIndex = i;
            }

            GUILayout.Space(4);
        }

        if (removeIndex >= 0)
            _choicesProp.DeleteArrayElementAtIndex(removeIndex);

        if (GUILayout.Button("Add Choice", GUILayout.MaxWidth(120)))
            _choicesProp.InsertArrayElementAtIndex(_choicesProp.arraySize);

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
