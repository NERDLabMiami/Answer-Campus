using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class QuickReplyButton : MonoBehaviour
{
    [Header("Refs")]
    public Button button;                  // root button
    public TMP_Text label;                 // optional
    public Image icon;                     // optional

    /// <summary>Bind a reply's emoji text and click handler.</summary>
    public void Bind(string emoji, Action onClick)
    {
        // Label
        if (label)
        {
            if (string.IsNullOrEmpty(emoji))
            {
                label.text = "";
                label.gameObject.SetActive(false);
            }
            else
            {
                label.text = emoji;
                label.gameObject.SetActive(true);
            }
        }

        // Click
        if (button)
        {
            button.onClick.RemoveAllListeners();
            if (onClick != null) button.onClick.AddListener(() => onClick());
        }
    }

    /// <summary>Convenience for overflow chips (e.g., "+3").</summary>
    public void BindOverflow(int extraCount, Action onClick = null)
    {
        Bind("+" + extraCount, onClick);
    }
}