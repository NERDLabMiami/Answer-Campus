// TextThreadPanel.cs

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VNEngine;
public static class QuickReplyIconLibrary
{
    static Dictionary<string, Sprite> cache;
    public static Sprite Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        cache ??= Build();
        return cache.TryGetValue(key, out var s) ? s : null;
    }

    static Dictionary<string, Sprite> Build()
    {
        // TODO: load from Resources, Addressables, or assign via inspector
        return new Dictionary<string, Sprite>
        {
            // { "thumbs_up", Resources.Load<Sprite>("Icons/thumbs_up") }
        };
    }
}

public class TextThreadPanel : MonoBehaviour
{
    [Header("Wiring")]
    public Transform contentRoot;           // vertical layout group for bubbles
    public GameObject npcBubblePrefab;      // has Text body
    public GameObject playerBubblePrefab;   // has Text body (right-aligned)
    public Transform quickReplyRoot;        // horizontal/vertical group for reply buttons
    public GameObject quickReplyButtonPrefab; // has Button + Text + optional Image
    public ProfilePicture[] profiles; 
    Character current;
    [SerializeField] private GameObject root;         // the panel GameObject
    [SerializeField] private CanvasGroup canvasGroup; // optional, if present
    [HideInInspector] public bool allowReplies = true;

    public void Show(Character other)
    {
        current = other;
        if (root) root.SetActive(true);
        if (canvasGroup) { canvasGroup.alpha = 1; canvasGroup.interactable = true; canvasGroup.blocksRaycasts = true; }
        Render();
    }

    public void Hide()
    {
        current = default;
        // optional: clear children so it’s fresh next time
        if (contentRoot)
            for (int i = contentRoot.childCount - 1; i >= 0; i--) Destroy(contentRoot.GetChild(i).gameObject);
        if (quickReplyRoot)
            for (int i = quickReplyRoot.childCount - 1; i >= 0; i--) Destroy(quickReplyRoot.GetChild(i).gameObject);
            
        if (canvasGroup) { canvasGroup.alpha = 0; canvasGroup.interactable = false; canvasGroup.blocksRaycasts = false; }
        if (root) root.SetActive(false);
    }
    
public void Render()
{
    // Clear
    foreach (Transform c in contentRoot) Destroy(c.gameObject);
    foreach (Transform c in quickReplyRoot) Destroy(c.gameObject);

    var msgs = TextThreads.GetThread(current);
    QuickReply[] pending = null;
    string pendingTargetScene = null;

    foreach (var m in msgs)
    {
        var prefab = m.isPlayer ? playerBubblePrefab : npcBubblePrefab;
        var go     = Instantiate(prefab, contentRoot);

        if (!m.isPlayer)
        {
            // Use the instantiated object, not the prefab
            var bubble = go.GetComponent<SpeechBubble>();
            if (bubble != null)
            {
                // Set the body text
                if (bubble.textContainer != null)
                    bubble.textContainer.text = m.body ?? "";

                // Push the NPC profile image down into the bubble
                if (bubble.image != null && profiles != null)
                {
                    for (int i = 0; i < profiles.Length; i++)
                    {
                        if (profiles[i].character.Equals(current))
                        {
                            bubble.image.sprite  = profiles[i].pictureSmall;
                            bubble.image.enabled = bubble.image.sprite != null;
                            break;
                        }
                    }
                }
            }
        }
        else
        {
            // Player bubble: just text
            var label = go.GetComponentInChildren<TMP_Text>(true);
            if (label) label.text = m.body ?? "";
        }

        // If this NPC message offers quick replies that are now unlocked, remember them.
        // unlockWeek=0 means immediately available; otherwise wait until that week.
        if (!m.isPlayer && m.quickReplies != null && m.quickReplies.Count > 0)
        {
            int week = UnityEngine.Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));
            if (m.unlockWeek <= 0 || week >= m.unlockWeek)
            {
                pending = m.quickReplies.ToArray();
                pendingTargetScene = m.location;
            }
        }
    }

    // Build quick replies below...
    if (pending != null && pending.Length > 0 && allowReplies)
    {
        foreach (var qr in pending)
        {
            var btnGO = Instantiate(quickReplyButtonPrefab, quickReplyRoot);

            var txt = btnGO.GetComponentInChildren<TMP_Text>(true);
            if (txt) txt.text = qr.label ?? "";

            var img = btnGO.GetComponentInChildren<Image>(true);
            if (img != null) img.sprite = QuickReplyIconLibrary.Get(qr.iconKey);

            var button = btnGO.GetComponent<Button>();
            if (button == null) continue;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                TextThreads.SendPlayerResponse(current, qr);

                // "advance_day": advance to next week morning and reload Home.
                if (IsSpecialPayload(qr.payload, "advance_day"))
                {
                    Hide();
                    AdvanceToNextMorning();
                    return;
                }

                // "decline": stay in phone thread.
                bool isDecline = IsSpecialPayload(qr.payload, "decline");

                if (!isDecline && !string.IsNullOrWhiteSpace(pendingTargetScene))
                {
                    Hide();
                    HomeCutsceneController.NavigateOut(pendingTargetScene);
                }
                else
                {
                    Render();
                }
            });

        }
        quickReplyRoot.gameObject.SetActive(true);

    }
    else
    {
        quickReplyRoot.gameObject.SetActive(false);
    }
}

private static bool IsSpecialPayload(string payload, string value) =>
    !string.IsNullOrWhiteSpace(payload) &&
    string.Equals(payload.Trim(), value, StringComparison.OrdinalIgnoreCase);

private static void AdvanceToNextMorning()
{
    int week = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));
    int next = Mathf.Min(week + 1, SemesterHelper.FinalsWeek);
    StatsManager.Set_Numbered_Stat("Week",      (float)next);
    StatsManager.Set_Numbered_Stat("DayPhase",  0f);
    StatsManager.Set_Numbered_Stat("DayOffset", 0f);
    LocationRouter.Go("Home");
}

}
