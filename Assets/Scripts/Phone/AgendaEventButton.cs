using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AgendaEventButton : MonoBehaviour
{
    [Header("UI")]
    public Image icon;
    public TMP_Text titleText;         // main label (uses EventInfo.label)
    public TMP_Text timeText;          // secondary label (e.g., "Week 6")

    [Header("Attendees (optional)")]
    public Transform attendeesRoot;    // container for chips (can be null)
    public FriendChip attendeeChipPrefab; // optional
    public int maxAttendeeChips = 6;

public void Bind(
        string title,
        EventType eventType,
        bool isPast,
        Action onClick)
    {
        // Title: strike-through and grey when in the past
        if (titleText)
        {
            // TMP supports rich text <s> for strike-through
            titleText.text = isPast ? $"<s>{title}</s>" : title ?? "";
            titleText.alpha = isPast ? 0.6f : 1f;
        }

        // Optional: tint icon or choose different sprites per type
        switch (eventType)
        {
            case EventType.FootballHomeGame:
                // set football icon if you have one
                break;
            case EventType.Custom:
                // optional: custom event icon
                break;
            case EventType.Finals:
                break;
            case EventType.Midterms:
                break;
        }

        // Disable clicking on past events
        var btn = GetComponent<UnityEngine.UI.Button>();
        if (btn)
        {
            btn.onClick.RemoveAllListeners();
            if (!isPast && onClick != null)
                btn.onClick.AddListener(() => onClick());

            btn.interactable = !isPast;
        }
    }

    public void BindInfo(string label, string value = null)
    {
        if (titleText) titleText.text = label ?? string.Empty;
        if (timeText)
        {
            timeText.text = value ?? string.Empty;
            timeText.gameObject.SetActive(!string.IsNullOrEmpty(value));
        }

        var btn = GetComponent<UnityEngine.UI.Button>();
        if (btn)
        {
            btn.onClick.RemoveAllListeners();
            btn.interactable = false;
        }
    }
}