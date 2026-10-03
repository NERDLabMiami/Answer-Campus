#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Linq;
using VNEngine;

[CustomEditor(typeof(GateTraitsNode))]
public class GateTraitsNodeEditor : Editor
{
    SerializedProperty traitRegistryProp;
    SerializedProperty traitRequirementsProp;
    SerializedProperty successDeltasProp, successConversationProp, continueCurrentOnSuccessProp;
    SerializedProperty failureDeltasProp, failureConversationProp, continueCurrentOnFailureProp;
    SerializedProperty logOutcomeProp;

    private bool show_advanced = false;

    void OnEnable()
    {
        traitRegistryProp            = serializedObject.FindProperty("traitRegistry");
        traitRequirementsProp        = serializedObject.FindProperty("traitRequirements");
        successDeltasProp            = serializedObject.FindProperty("successDeltas");
        successConversationProp      = serializedObject.FindProperty("successConversation");
        continueCurrentOnSuccessProp = serializedObject.FindProperty("continueCurrentOnSuccess");
        failureDeltasProp            = serializedObject.FindProperty("failureDeltas");
        failureConversationProp      = serializedObject.FindProperty("failureConversation");
        continueCurrentOnFailureProp = serializedObject.FindProperty("continueCurrentOnFailure");
        logOutcomeProp               = serializedObject.FindProperty("logOutcome");

        MigrateTraitKeys();
    }

    // Fills the preferred string traitKey from the legacy enum, once, for existing serialized data.
    void MigrateTraitKeys()
    {
        var builtInKeys = GateTraitsNode.AllTraitKeys().ToArray();

        if (traitRequirementsProp != null)
        {
            for (int i = 0; i < traitRequirementsProp.arraySize; i++)
            {
                var r = traitRequirementsProp.GetArrayElementAtIndex(i);
                MigrateKey(r, "traitKey", "enumTrait", builtInKeys);
            }
        }
        MigrateDeltaKeys(successDeltasProp, builtInKeys);
        MigrateDeltaKeys(failureDeltasProp, builtInKeys);
    }

    void MigrateDeltaKeys(SerializedProperty deltasProp, string[] builtInKeys)
    {
        if (deltasProp == null) return;
        for (int i = 0; i < deltasProp.arraySize; i++)
        {
            var d = deltasProp.GetArrayElementAtIndex(i);
            MigrateKey(d, "traitKey", "trait", builtInKeys);
        }
    }

    void MigrateKey(SerializedProperty element, string keyPropName, string enumPropName, string[] builtInKeys)
    {
        var keyProp = element.FindPropertyRelative(keyPropName);
        if (keyProp == null || !string.IsNullOrEmpty(keyProp.stringValue)) return;

        var enumProp = element.FindPropertyRelative(enumPropName);
        if (enumProp == null) return;

        int idx = Mathf.Clamp(enumProp.enumValueIndex, 0, builtInKeys.Length - 1);
        keyProp.stringValue = builtInKeys.Length > 0 ? builtInKeys[idx] : string.Empty;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        VNNodeEditorGUI.DrawCategoryBanner(VNNodeEditorGUI.NodeCategory.Branching);

        EditorGUILayout.PropertyField(traitRegistryProp, new GUIContent("Trait Registry"));
        if (traitRegistryProp.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("No TraitRegistry assigned. Falling back to legacy enum list.", MessageType.Warning);
            if (GUILayout.Button("Create Default TraitRegistry in Assets/Resources"))
            {
                var reg = ScriptableObject.CreateInstance<TraitRegistry>();
                reg.numberedTraitKeys = new System.Collections.Generic.List<string> { "Humor", "Charisma", "Empathy", "Grades" };
                System.IO.Directory.CreateDirectory("Assets/Resources");
                var path = "Assets/Resources/TraitRegistry.asset";
                AssetDatabase.CreateAsset(reg, path);
                AssetDatabase.SaveAssets();
                traitRegistryProp.objectReferenceValue = reg;
            }
        }

        string[] keys = GetTraitKeys();

        EditorGUILayout.Space(6);
        VNNodeEditorGUI.DrawTraitRequirements(traitRequirementsProp, keys);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Trait Requirements Met", EditorStyles.boldLabel);
        DrawJumpSection(successConversationProp, continueCurrentOnSuccessProp);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Trait Requirements Not Met", EditorStyles.boldLabel);
        DrawJumpSection(failureConversationProp, continueCurrentOnFailureProp);

        if (continueCurrentOnSuccessProp.boolValue && continueCurrentOnFailureProp.boolValue)
        {
            EditorGUILayout.HelpBox(
                "Both outcomes are set to Continue Current Conversation, so this node will never jump anywhere — " +
                "it will only apply trait changes in place. Set a Jump To on at least one outcome if that's not intended.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();
        show_advanced = EditorGUILayout.Foldout(show_advanced, "Advanced", true);
        if (show_advanced)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Trait Requirements Met", EditorStyles.miniBoldLabel);
            DrawDeltas(successDeltasProp, keys);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Trait Requirements Not Met", EditorStyles.miniBoldLabel);
            DrawDeltas(failureDeltasProp, keys);

            EditorGUILayout.Space(10);
            EditorGUILayout.PropertyField(logOutcomeProp, new GUIContent("Log Outcome"));

            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    // --- helpers --------------------------------------------------------------

    string[] GetTraitKeys() => VNNodeEditorGUI.ResolveTraitKeys(traitRegistryProp);

    // Mutually exclusive: continue the current conversation, or jump to another one.
    // Only the relevant control is shown, so the two can't be checked or unchecked together.
    void DrawJumpSection(SerializedProperty conversationProp, SerializedProperty continueProp)
    {
        EditorGUILayout.PropertyField(continueProp, new GUIContent("Continue Current Conversation"));
        if (!continueProp.boolValue)
        {
            EditorGUILayout.PropertyField(conversationProp, new GUIContent("Jump To"));
        }
    }

    void DrawDeltas(SerializedProperty deltasProp, string[] keys)
    {
        if (deltasProp == null) return;

        for (int j = 0; j < deltasProp.arraySize; j++)
        {
            var d = deltasProp.GetArrayElementAtIndex(j);
            using (new EditorGUILayout.HorizontalScope())
            {
                var keyProp = d.FindPropertyRelative("traitKey");
                var amountProp = d.FindPropertyRelative("amount");

                VNNodeEditorGUI.DrawTraitKeyPopup(keyProp, keys);
                EditorGUILayout.LabelField("change by", GUILayout.Width(62));
                if (amountProp != null) EditorGUILayout.PropertyField(amountProp, GUIContent.none, GUILayout.MaxWidth(60));

                if (GUILayout.Button("-", GUILayout.Width(22)))
                    deltasProp.DeleteArrayElementAtIndex(j);
            }
        }

        if (GUILayout.Button("Add Trait Change", GUILayout.MaxWidth(160)))
        {
            int j = deltasProp.arraySize;
            deltasProp.InsertArrayElementAtIndex(j);
            var d = deltasProp.GetArrayElementAtIndex(j);

            var keyProp = d.FindPropertyRelative("traitKey");
            var amountProp = d.FindPropertyRelative("amount");

            if (keyProp != null) keyProp.stringValue = keys.Length > 0 ? keys[0] : string.Empty;
            if (amountProp != null) amountProp.floatValue = 0f;

            var enumProp = d.FindPropertyRelative("trait");
            if (enumProp != null) enumProp.enumValueIndex = 0;
        }
    }
}
#endif
