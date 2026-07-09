#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using VNEngine;

[CustomEditor(typeof(ShowChoiceNode))]
public class ShowChoiceNodeEditor : Editor
{
    private SerializedProperty _choicesProp;
    private SerializedProperty _hideDialogueUIProp;
    private ReorderableList _choices;

    private void OnEnable()
    {
        _choicesProp = serializedObject.FindProperty("choices");
        _hideDialogueUIProp = serializedObject.FindProperty("hideDialogueUI");

        _choices = new ReorderableList(serializedObject, _choicesProp, true, true, true, true);
        _choices.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Choices");

        _choices.elementHeightCallback = index =>
        {
            var el = _choicesProp.GetArrayElementAtIndex(index);
            float line = EditorGUIUtility.singleLineHeight;

            float h = 0f;
            h += 2f;

            var textProp = el.FindPropertyRelative("text");
            float textH = EditorGUI.GetPropertyHeight(textProp, true);
            h += textH + 2f;

            h += line + 12f;  // Next Conversation

            h += line + 6f;   // Enable Requirements label
            h += line + 4f;   // toggle row

            if (el.FindPropertyRelative("useTraits").boolValue)
                h += EditorGUI.GetPropertyHeight(el.FindPropertyRelative("traitRequirements"), true) + 2f;

            if (el.FindPropertyRelative("useEvents").boolValue)
                h += EditorGUI.GetPropertyHeight(el.FindPropertyRelative("eventRequirements"), true) + 2f;

            if (el.FindPropertyRelative("useGame").boolValue)
                h += EditorGUI.GetPropertyHeight(el.FindPropertyRelative("footballRequirement"), true) + 2f;

            if (el.FindPropertyRelative("useAffinity").boolValue)
            {
                h += EditorGUI.GetPropertyHeight(el.FindPropertyRelative("affinityRequirements"), true) + 2f;
                h += EditorGUI.GetPropertyHeight(el.FindPropertyRelative("affinityDeltas"), true) + 2f;
            }

            h += 6f;
            return h;
        };

        _choices.drawElementCallback = (rect, index, active, focused) =>
        {
            var el = _choicesProp.GetArrayElementAtIndex(index);

            var textProp     = el.FindPropertyRelative("text");
            var nextConvProp = el.FindPropertyRelative("nextConversation");

            var useTraitsProp = el.FindPropertyRelative("useTraits");
            var useEventsProp = el.FindPropertyRelative("useEvents");
            var useGameProp   = el.FindPropertyRelative("useGame");

            var traitReqsProp = el.FindPropertyRelative("traitRequirements");
            var eventReqsProp = el.FindPropertyRelative("eventRequirements");
            var footballProp  = el.FindPropertyRelative("footballRequirement");

            var useAffinityProp    = el.FindPropertyRelative("useAffinity");
            var affinityReqsProp   = el.FindPropertyRelative("affinityRequirements");
            var affinityDeltasProp = el.FindPropertyRelative("affinityDeltas");

            float line = EditorGUIUtility.singleLineHeight;

            rect.y += 2;
            rect.x += 6;
            rect.width -= 12;

            float textH = EditorGUI.GetPropertyHeight(textProp, true);
            EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, textH), textProp, new GUIContent("Text"), true);
            rect.y += textH + 2f;

            EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, line), nextConvProp, new GUIContent("Next Conversation"));
            rect.y += line + 12;

            EditorGUI.LabelField(new Rect(rect.x, rect.y, rect.width, line), "Enable Requirements");
            rect.y += line + 6;

            float col = rect.width / 4f;
            useTraitsProp.boolValue   = EditorGUI.ToggleLeft(new Rect(rect.x,           rect.y, col, line), "Traits",   useTraitsProp.boolValue);
            useEventsProp.boolValue   = EditorGUI.ToggleLeft(new Rect(rect.x + col,     rect.y, col, line), "Events",   useEventsProp.boolValue);
            useGameProp.boolValue     = EditorGUI.ToggleLeft(new Rect(rect.x + col * 2, rect.y, col, line), "Game",     useGameProp.boolValue);
            useAffinityProp.boolValue = EditorGUI.ToggleLeft(new Rect(rect.x + col * 3, rect.y, col, line), "Affinity", useAffinityProp.boolValue);
            rect.y += line + 4;

            if (useTraitsProp.boolValue)
            {
                float h = EditorGUI.GetPropertyHeight(traitReqsProp, true);
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, h), traitReqsProp, true);
                rect.y += h + 2;
            }

            if (useEventsProp.boolValue)
            {
                float h = EditorGUI.GetPropertyHeight(eventReqsProp, true);
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, h), eventReqsProp, true);
                rect.y += h + 2;
            }

            if (useGameProp.boolValue)
            {
                float h = EditorGUI.GetPropertyHeight(footballProp, true);
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, h), footballProp, true);
                rect.y += h + 2;
            }

            if (useAffinityProp.boolValue)
            {
                float h = EditorGUI.GetPropertyHeight(affinityReqsProp, true);
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, h), affinityReqsProp, true);
                rect.y += h + 2;

                h = EditorGUI.GetPropertyHeight(affinityDeltasProp, true);
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, h), affinityDeltasProp, true);
                rect.y += h + 2;
            }
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(_hideDialogueUIProp);

        GUILayout.Space(6);
        _choices.DoLayoutList();

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
