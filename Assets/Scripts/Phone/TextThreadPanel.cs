// TextThreadPanel.cs

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VNEngine;

public class TextThreadPanel : MonoBehaviour
{
    [Header("Wiring")]
    public Transform contentRoot;           // vertical layout group for bubbles
    public GameObject npcBubblePrefab;      // has Text body
    public GameObject playerBubblePrefab;   // has Text body (right-aligned)
    public Transform quickReplyRoot;        // horizontal/vertical group for reply buttons
    public GameObject quickReplyButtonPrefab; // has Button + Text + optional Image
    public ProfilePicture[] profiles;
    public ScrollRect scrollRect;           // auto-scrolled to the bottom on every render

    [Header("Typing Delay")]
    [SerializeField] private float typingDelaySeconds = 1.2f;
    [SerializeField] private Sprite typingIndicatorSprite; // "..." dots bubble shown while waiting

    [Header("Leaving The Thread")]
    [SerializeField] private float postReplyHoldSeconds = 1f; // time to read the NPC's reply before leaving
    [SerializeField] private float fadeOutSeconds = 0.4f;     // fade the panel out before navigating/advancing

    Character current;
    [SerializeField] private GameObject root;         // the panel GameObject
    [SerializeField] private CanvasGroup canvasGroup; // optional, if present
    [HideInInspector] public bool allowReplies = true;

    // Bubbles currently on screen, in the same order as TextThreads.GetThread(current).
    // Tracking these lets a new message be appended without destroying and
    // reinstantiating the whole thread (which caused a visible scroll jump).
    private readonly List<GameObject> renderedBubbles = new List<GameObject>();

    public void Show(Character other)
    {
        current = other;
        if (root) root.SetActive(true);
        if (canvasGroup) { canvasGroup.alpha = 1; canvasGroup.interactable = true; canvasGroup.blocksRaycasts = true; }
        RebuildAll();
    }

    public void Hide()
    {
        current = default;
        ClearBubbles();
        if (quickReplyRoot)
            for (int i = quickReplyRoot.childCount - 1; i >= 0; i--) Destroy(quickReplyRoot.GetChild(i).gameObject);

        if (canvasGroup) { canvasGroup.alpha = 0; canvasGroup.interactable = false; canvasGroup.blocksRaycasts = false; }
        if (root) root.SetActive(false);
    }

    private void ClearBubbles()
    {
        foreach (var go in renderedBubbles)
            if (go) Destroy(go);
        renderedBubbles.Clear();
    }

    // Full teardown + rebuild - used when first opening a thread, where there's
    // nothing on screen yet to append to.
    private void RebuildAll()
    {
        ClearBubbles();
        var msgs = TextThreads.GetThread(current);
        foreach (var m in msgs)
            renderedBubbles.Add(CreateBubble(m));
        FinishRenderPass(msgs);
    }

    // Instantiates only the messages not yet on screen, leaving existing bubbles
    // untouched so the ScrollRect's content doesn't shrink-then-regrow.
    private void AppendNewMessages()
    {
        var msgs = TextThreads.GetThread(current);
        for (int i = renderedBubbles.Count; i < msgs.Count; i++)
            renderedBubbles.Add(CreateBubble(msgs[i]));
        FinishRenderPass(msgs);
    }

    private void FinishRenderPass(List<TextMessage> msgs)
    {
        RenderQuickReplies(msgs);
        if (current != Character.NONE) TextThreads.MarkRead(current);
        ScrollToBottom();
    }

    private GameObject CreateBubble(TextMessage m)
    {
        var prefab = m.isPlayer ? playerBubblePrefab : npcBubblePrefab;
        var go = Instantiate(prefab, contentRoot);

        if (!m.isPlayer)
        {
            var bubble = go.GetComponent<SpeechBubble>();
            if (bubble != null)
            {
                if (bubble.textContainer != null)
                    bubble.textContainer.text = m.body ?? "";
                SetNpcProfileSprite(bubble);
            }
        }
        else
        {
            var label = go.GetComponentInChildren<TMP_Text>(true);
            if (label) label.text = m.body ?? "";
        }

        return go;
    }

    private void SetNpcProfileSprite(SpeechBubble bubble)
    {
        if (bubble.image == null || profiles == null) return;
        for (int i = 0; i < profiles.Length; i++)
        {
            if (profiles[i].character.Equals(current))
            {
                bubble.image.sprite = profiles[i].pictureSmall;
                bubble.image.enabled = bubble.image.sprite != null;
                break;
            }
        }
    }

    private void RenderQuickReplies(List<TextMessage> msgs)
    {
        foreach (Transform c in quickReplyRoot) Destroy(c.gameObject);

        QuickReply[] pending = null;
        string pendingTargetScene = null;

        foreach (var m in msgs)
        {
            // If this NPC message offers quick replies that are now unlocked, remember them.
            // unlockWeek=0 means immediately available; otherwise wait until that week.
            if (!m.isPlayer && m.quickReplies != null && m.quickReplies.Count > 0)
            {
                int week = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));
                if (m.unlockWeek <= 0 || week >= m.unlockWeek)
                {
                    pending = m.quickReplies.ToArray();
                    pendingTargetScene = m.locationForTimingOnly ? null : m.location;
                }
            }
        }

        if (pending != null && pending.Length > 0 && allowReplies)
        {
            foreach (var qr in pending)
            {
                var btnGO = Instantiate(quickReplyButtonPrefab, quickReplyRoot);

                var qrButton = btnGO.GetComponent<QuickReplyButton>();
                if (qrButton == null) continue;

                qrButton.Bind(qr.emoji, () =>
                {
                    // "advance_day": play the reply out, then advance to next week morning and reload Home.
                    if (IsSpecialPayload(qr.payload, "advance_day"))
                    {
                        StartCoroutine(ReplyWithTypingDelay(qr, advanceDayAfter: true));
                        return;
                    }

                    // "decline": stay in phone thread. Anything else with a pending
                    // location: play the reply out, then navigate there.
                    bool isDecline = IsSpecialPayload(qr.payload, "decline");
                    string targetScene = (!isDecline && !string.IsNullOrWhiteSpace(pendingTargetScene))
                        ? pendingTargetScene
                        : null;

                    StartCoroutine(ReplyWithTypingDelay(qr, targetSceneAfter: targetScene));
                });
            }
            quickReplyRoot.gameObject.SetActive(true);
        }
        else
        {
            quickReplyRoot.gameObject.SetActive(false);
        }
    }

    private void ScrollToBottom()
    {
        if (scrollRect == null) return;
        // ForceUpdateCanvases alone doesn't reliably settle a freshly rebuilt
        // ContentSizeFitter/VerticalLayoutGroup before we read its height, which caused
        // a visible jump-to-top-then-back-down. Force-rebuilding content's own layout
        // first guarantees its size is correct before we set the scroll position.
        if (scrollRect.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    private class TypingBubbleHandle
    {
        public GameObject go;
        public SpeechBubble bubble;
        public Image messageImage;
        public Sprite originalSprite;
        public RectTransform messageRect;
        public Vector2 originalSizeDelta;
        public LayoutElement layoutElement;
        public float originalPreferredHeight;
    }

    // Instantiates an NPC bubble styled as a compact "..." pill, caching its normal
    // appearance so ResolveTypingIndicator can morph it back in place later instead
    // of destroying it and creating a separate reply bubble.
    private TypingBubbleHandle ShowTypingIndicator()
    {
        var handle = new TypingBubbleHandle { go = Instantiate(npcBubblePrefab, contentRoot) };

        handle.bubble = handle.go.GetComponent<SpeechBubble>();
        if (handle.bubble != null)
        {
            if (handle.bubble.textContainer != null) handle.bubble.textContainer.text = "";
            SetNpcProfileSprite(handle.bubble);
        }

        var messageTransform = handle.go.transform.Find("Message");
        if (messageTransform != null)
        {
            handle.messageImage = messageTransform.GetComponent<Image>();
            if (handle.messageImage != null)
            {
                handle.originalSprite = handle.messageImage.sprite;
                if (typingIndicatorSprite != null) handle.messageImage.sprite = typingIndicatorSprite;
            }

            handle.messageRect = messageTransform.GetComponent<RectTransform>();
            if (handle.messageRect != null)
            {
                handle.originalSizeDelta = handle.messageRect.sizeDelta;
                handle.messageRect.sizeDelta = new Vector2(70, 50);
            }
        }

        handle.layoutElement = handle.go.GetComponent<LayoutElement>();
        if (handle.layoutElement != null)
        {
            handle.originalPreferredHeight = handle.layoutElement.preferredHeight;
            handle.layoutElement.preferredHeight = 50;
        }

        return handle;
    }

    private void ResolveTypingIndicator(TypingBubbleHandle handle, string body)
    {
        if (handle.bubble != null && handle.bubble.textContainer != null)
            handle.bubble.textContainer.text = body ?? "";

        if (handle.messageImage != null) handle.messageImage.sprite = handle.originalSprite;
        if (handle.messageRect != null) handle.messageRect.sizeDelta = handle.originalSizeDelta;
        if (handle.layoutElement != null) handle.layoutElement.preferredHeight = handle.originalPreferredHeight;
    }

    private IEnumerator ReplyWithTypingDelay(QuickReply qr, string targetSceneAfter = null, bool advanceDayAfter = false)
    {
        TextThreads.SendPlayerResponse(current, qr);
        AppendNewMessages(); // shows the player's bubble; scrolls down once

        if (!string.IsNullOrWhiteSpace(qr.npcResponse))
        {
            var typing = ShowTypingIndicator();
            ScrollToBottom();

            yield return new WaitForSeconds(typingDelaySeconds);

            TextThreads.AppendNpcReply(current, qr.npcResponse);

            // Morph the "..." bubble into the real reply in place, instead of tearing
            // down and rebuilding the whole thread - avoids a second scroll jump and
            // matches how modern messaging apps resolve a typing indicator.
            ResolveTypingIndicator(typing, qr.npcResponse);
            renderedBubbles.Add(typing.go);
            FinishRenderPass(TextThreads.GetThread(current));
        }

        bool leavingThread = advanceDayAfter || !string.IsNullOrWhiteSpace(targetSceneAfter);
        if (leavingThread)
        {
            yield return new WaitForSeconds(postReplyHoldSeconds);

            // Inlined rather than a nested `yield return someCoroutine()` call: Hide()
            // deactivates `root`, and a GameObject going inactive kills any coroutine
            // waiting to *resume* on it - including an outer coroutine paused on a
            // nested yield. Keeping the fade inline (and calling Hide() only after
            // everything else below has already run) avoids that trap.
            if (canvasGroup != null)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;

                float startAlpha = canvasGroup.alpha;
                float t = 0f;
                while (t < fadeOutSeconds)
                {
                    t += Time.deltaTime;
                    canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t / fadeOutSeconds);
                    yield return null;
                }
                canvasGroup.alpha = 0f;
            }

            // Fire the transition before Hide() deactivates this GameObject.
            if (advanceDayAfter)
                AdvanceToNextMorning();
            else
                HomeCutsceneController.NavigateOut(targetSceneAfter);

            Hide();
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
