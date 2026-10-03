// Editor/QuickReplyDrawer.cs
using System;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(QuickReply))]
public class QuickReplyDrawer : PropertyDrawer
{
    // Starting set — limited to glyphs with verified, non-empty atlas data.
    // 😂 comes from EmojiOne (Assets/Fonts/TextMesh Pro/Resources/Sprite Assets/EmojiOne.asset),
    // confirmed to have a real (non-zero) sprite rect. ❤️/😬 exist in NotoColorEmoji-Regular
    // Color.asset (wired as a fallback on Komika Display SDF) but that asset's bake is
    // incomplete - both glyphs currently have zero-size rects and won't actually draw
    // anything. Once NotoColorEmoji-Regular Color.asset is regenerated via Unity's
    // Font Asset Creator with real atlas data, swap ❤️/😬 back in here (and consider
    // adding 👍, which isn't in either asset yet).
    static readonly string[] EmojiOptions = { "😎", "😍", "😂", "😅" };
    static readonly string[] EmojiPopupChoices = { "😎 (approve)", "😍 (love)", "😂 (laugh)", "😅 (awkward)", "Custom..." };

    static readonly string[] PayloadLabels = { "None", "Accept", "Decline", "Advance Day" };
    static readonly string[] PayloadValues = { "", "accept", "decline", "advance_day" };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return 4 * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var emojiProp = property.FindPropertyRelative("emoji");
        var responseTextProp = property.FindPropertyRelative("responseText");
        var payloadProp = property.FindPropertyRelative("payload");
        var npcResponseProp = property.FindPropertyRelative("npcResponse");

        float lineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        Rect row = new Rect(position.x, position.y, position.width, lineHeight);

        DrawEmojiField(row, emojiProp);
        row.y += lineHeight + spacing;

        EditorGUI.PropertyField(row, responseTextProp);
        row.y += lineHeight + spacing;

        DrawPayloadField(row, payloadProp);
        row.y += lineHeight + spacing;

        EditorGUI.PropertyField(row, npcResponseProp);
    }

    static void DrawEmojiField(Rect rect, SerializedProperty emojiProp)
    {
        int matchIndex = Array.IndexOf(EmojiOptions, emojiProp.stringValue);
        bool isCustom = matchIndex < 0;
        int selected = isCustom ? EmojiOptions.Length : matchIndex;

        Rect popupRect = rect;
        Rect customFieldRect = default;
        if (isCustom)
        {
            popupRect.width = rect.width * 0.55f;
            customFieldRect = new Rect(popupRect.xMax + 4f, rect.y, rect.width - popupRect.width - 4f, rect.height);
        }

        EditorGUI.BeginChangeCheck();
        int newSelected = EditorGUI.Popup(popupRect, "Emoji", selected, EmojiPopupChoices);
        if (EditorGUI.EndChangeCheck())
        {
            // Picking a listed emoji sets it directly; picking "Custom..." clears the
            // field so the freeform box starts blank instead of showing a stale value.
            emojiProp.stringValue = newSelected < EmojiOptions.Length ? EmojiOptions[newSelected] : "";
        }

        if (isCustom)
            emojiProp.stringValue = EditorGUI.TextField(customFieldRect, GUIContent.none, emojiProp.stringValue);
    }

    static void DrawPayloadField(Rect rect, SerializedProperty payloadProp)
    {
        // An unrecognized stored value (e.g. legacy data) displays as "None" without
        // being overwritten until the designer actively changes the dropdown - matches
        // TextThreadPanel's IsSpecialPayload fallback, which treats anything unrecognized
        // the same as an empty payload.
        int currentIndex = Array.IndexOf(PayloadValues, payloadProp.stringValue);
        if (currentIndex < 0) currentIndex = 0;

        EditorGUI.BeginChangeCheck();
        int newIndex = EditorGUI.Popup(rect, "Payload", currentIndex, PayloadLabels);
        if (EditorGUI.EndChangeCheck())
            payloadProp.stringValue = PayloadValues[newIndex];
    }
}
