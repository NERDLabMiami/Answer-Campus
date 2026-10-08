#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VNEngine;

// Shared drawing helpers for VNEngine node Inspectors, factored out of the many
// per-node-type editors that used to copy-paste this GUI logic independently.
public static class VNNodeEditorGUI
{
    // Call once per OnInspectorGUI when fields were written directly onto the target
    // (not via SerializedProperty), which doesn't dirty the object/scene on its own.
    // Unity's Save Scene silently skips scenes it doesn't think are dirty, so without
    // this, edits made through these editors can be "saved" and still lost on next load.
    public static void MarkDirtyIfChanged(Node node, bool changed)
    {
        if (!changed) return;
        EditorUtility.SetDirty(node);
        EditorSceneManager.MarkSceneDirty(node.gameObject.scene);
    }

    // ---- Label widths -----------------------------------------------------
    // A small, named set replacing the ad-hoc EditorGUIUtility.labelWidth magic
    // numbers that used to be scattered (and inconsistent) across node editors.
    public const float NarrowLabelWidth = 45f;
    public const float ShortLabelWidth = 95f;
    public const float StandardLabelWidth = 150f;
    public const float WideLabelWidth = 200f;

    // ---- Actor source field -------------------------------------------------
    private static readonly string[] ActorSourceLabels = { "Direct Reference", "CSV Key", "String Stat" };

    // Draws the "Actor from" popup, the conditional Direct-Reference ObjectField / CSV-Key-or-Stat
    // PropertyField that follows it, and a legacy-actor-name migration warning. This block used to be
    // copy-pasted near-verbatim across every node editor that targets an Actor.
    public static void DrawActorSourceField(
        SerializedProperty actorNameFromProp,
        SerializedProperty actorDataProp,
        SerializedProperty actorNameProp,
        Dialogue_Source currentSource,
        string legacyActorName,
        string popupTooltip,
        string objectFieldLabel,
        string objectFieldTooltip,
        string statFieldLabel,
        string statFieldTooltip,
        string legacyWarningExtra = "Drag the matching Actor asset in above.")
    {
        EditorGUI.BeginChangeCheck();
        int newSource = EditorGUILayout.Popup(new GUIContent("Actor from", popupTooltip),
            actorNameFromProp.enumValueIndex, ActorSourceLabels);
        if (EditorGUI.EndChangeCheck())
            actorNameFromProp.enumValueIndex = newSource;

        if (currentSource == Dialogue_Source.Text_From_Editor)
        {
            EditorGUI.BeginChangeCheck();
            Actor newActorRef = (Actor)EditorGUILayout.ObjectField(
                new GUIContent(objectFieldLabel, objectFieldTooltip),
                actorDataProp.objectReferenceValue, typeof(Actor), false);
            if (EditorGUI.EndChangeCheck())
                actorDataProp.objectReferenceValue = newActorRef;

            if (actorDataProp.objectReferenceValue == null && !string.IsNullOrEmpty(legacyActorName))
            {
                EditorGUILayout.HelpBox(
                    "This node still has a legacy Actor name (\"" + legacyActorName + "\") but no Actor reference assigned. " + legacyWarningExtra,
                    MessageType.Warning);
            }
        }
        else
        {
            EditorGUILayout.PropertyField(actorNameProp, new GUIContent(statFieldLabel, statFieldTooltip));
        }
    }

    // ---- Expression / outfit pickers -----------------------------------------
    // A dropdown of the distinct expression names available for an Actor within a given outfit. Scoping
    // the list to one outfit (rather than every raw ActorExpression name) is what keeps this popup from
    // mixing emotions together with outfit/style variants - see DrawOutfitPicker for the separate control
    // that switches outfit.
  
    // Draws the actual sprite region straight from its source texture (full resolution, correctly cropped)
    // rather than Unity's small cached AssetPreview thumbnail - at inspector size that cached preview is
    // too blurry/tiny to actually judge a facial expression by.
    public static void DrawSpritePreview(Sprite sprite, float maxSize = 260f)
    {
        if (sprite == null || sprite.texture == null)
        {
            EditorGUILayout.HelpBox("This expression has no sprite assigned.", MessageType.Warning);
            return;
        }

        Rect spriteRect = sprite.rect;
        Texture2D tex = sprite.texture;
        float aspect = spriteRect.width / spriteRect.height;

        float height = maxSize;
        float width = height * aspect;
        if (width > maxSize)
        {
            width = maxSize;
            height = width / aspect;
        }

        Rect layoutRect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
        layoutRect.x = EditorGUIUtility.labelWidth;
        layoutRect.width = width;
        layoutRect.height = height;

        Rect texCoords = new Rect(
            spriteRect.x / tex.width,
            spriteRect.y / tex.height,
            spriteRect.width / tex.width,
            spriteRect.height / tex.height);

        GUI.DrawTextureWithTexCoords(layoutRect, tex, texCoords);
    }

    // ---- Trait requirements -------------------------------------------------
    // Resolves the TraitRegistry a node should use: its own override if assigned, else the project default.
    public static TraitRegistry ResolveTraitRegistry(SerializedProperty traitRegistryProp)
    {
        var regObj = traitRegistryProp != null ? traitRegistryProp.objectReferenceValue as TraitRegistry : null;
        if (regObj != null) return regObj;
        return TraitRegistry.Load();
    }

    // Resolves the list of selectable trait keys: from the registry if it has any, else the legacy enum fallback.
    public static string[] ResolveTraitKeys(SerializedProperty traitRegistryProp)
    {
        var reg = ResolveTraitRegistry(traitRegistryProp);
        return (reg != null && reg.numberedTraitKeys != null && reg.numberedTraitKeys.Count > 0)
            ? reg.numberedTraitKeys.ToArray()
            : GateTraitsNode.AllTraitKeys().ToArray();
    }

    // A single trait-key dropdown backed by a string field, used both standalone (trait deltas) and
    // as part of DrawTraitRequirements below.
    public static void DrawTraitKeyPopup(SerializedProperty keyProp, string[] keys)
    {
        if (keyProp == null)
        {
            EditorGUILayout.HelpBox("Missing traitKey field.", MessageType.Info);
            return;
        }

        int sel = Mathf.Max(0, System.Array.IndexOf(keys, keyProp.stringValue));
        int newSel = EditorGUILayout.Popup(sel, keys, GUILayout.MaxWidth(200));
        keyProp.stringValue = (newSel >= 0 && newSel < keys.Length) ? keys[newSel] : keyProp.stringValue;
    }

    // Draws a "Requirements (All must pass)" list of trait-key/compare/value rows against a
    // List<FlexibleTraitRequirement>-shaped SerializedProperty, with add/remove controls. Shared between
    // ShowChoiceNodeEditor and GateTraitsNodeEditor, which used to duplicate this line-for-line.
    public static void DrawTraitRequirements(SerializedProperty reqsProp, string[] keys, string headerLabel = "Requirements (All must pass)")
    {
        if (reqsProp == null) return;

        EditorGUILayout.LabelField(headerLabel, EditorStyles.boldLabel);

        for (int j = 0; j < reqsProp.arraySize; j++)
        {
            var r = reqsProp.GetArrayElementAtIndex(j);
            using (new EditorGUILayout.HorizontalScope())
            {
                var keyProp = r.FindPropertyRelative("traitKey");
                var compProp = r.FindPropertyRelative("compare");
                var valProp = r.FindPropertyRelative("value");

                if (keyProp == null)
                {
                    EditorGUILayout.HelpBox("This node uses the legacy requirement type. Convert to FlexibleTraitRequirement.", MessageType.Info);
                    break;
                }

                DrawTraitKeyPopup(keyProp, keys);
                if (compProp != null) EditorGUILayout.PropertyField(compProp, GUIContent.none, GUILayout.MaxWidth(140));
                if (valProp != null) EditorGUILayout.PropertyField(valProp, GUIContent.none, GUILayout.MaxWidth(80));

                if (GUILayout.Button("-", GUILayout.Width(22)))
                    reqsProp.DeleteArrayElementAtIndex(j);
            }
        }

        if (GUILayout.Button("Add Requirement", GUILayout.MaxWidth(160)))
        {
            int j = reqsProp.arraySize;
            reqsProp.InsertArrayElementAtIndex(j);
            var r = reqsProp.GetArrayElementAtIndex(j);

            var keyProp = r.FindPropertyRelative("traitKey");
            var compProp = r.FindPropertyRelative("compare");
            var valProp = r.FindPropertyRelative("value");

            if (keyProp != null) keyProp.stringValue = keys.Length > 0 ? keys[0] : string.Empty;
            if (compProp != null) compProp.enumValueIndex = (int)NumberCompare.GreaterOrEqual;
            if (valProp != null) valProp.floatValue = 1f;

            var enumProp = r.FindPropertyRelative("enumTrait");
            if (enumProp != null) enumProp.enumValueIndex = 0;
        }
    }

    // ---- Category banner -----------------------------------------------------
    // Lightweight at-a-glance visual grouping for the Inspector, given this project has no
    // node-graph canvas to color/icon nodes on (see Assets/Scripts/Nodes/README.md).
    public enum NodeCategory { Dialogue, Actor, Background, Branching, SceneSystem, DataStats }

    private static readonly System.Collections.Generic.Dictionary<NodeCategory, (string label, Color color)> CategoryStyle = new()
    {
        { NodeCategory.Dialogue,    ("Dialogue",      new Color(0.55f, 0.75f, 1.00f)) },
        { NodeCategory.Actor,       ("Actor",         new Color(0.65f, 0.85f, 0.65f)) },
        { NodeCategory.Background,  ("Background",    new Color(0.85f, 0.75f, 0.55f)) },
        { NodeCategory.Branching,   ("Branching",     new Color(0.85f, 0.60f, 0.85f)) },
        { NodeCategory.SceneSystem, ("Scene / System",new Color(0.75f, 0.75f, 0.75f)) },
        { NodeCategory.DataStats,   ("Data / Stats",  new Color(0.90f, 0.70f, 0.50f)) },
    };

    public static void DrawCategoryBanner(NodeCategory category)
    {
        var (label, color) = CategoryStyle[category];
        var prevColor = GUI.backgroundColor;
        GUI.backgroundColor = color;
        EditorGUILayout.LabelField(label, EditorStyles.toolbarButton);
        GUI.backgroundColor = prevColor;
        EditorGUILayout.Space(2);
    }
}
#endif
