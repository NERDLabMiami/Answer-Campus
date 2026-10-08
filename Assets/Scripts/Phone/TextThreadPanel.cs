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

    // Hard guard set for the entire duration of ReplyWithTypingDelay: no quick-reply button
    // can be shown while a reply exchange is actively resolving, regardless of what any render
    // pass's message list contains. Without this, quick-reply buttons for a different,
    // already-delivered-but-held-back message could surface mid-exchange (before its own
    // bubble ever renders) and be tapped, re-triggering a reply to an already-answered message.
    private bool _resolvingReply;

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
        var msgs = EffectiveThread(current);
        foreach (var m in msgs)
            renderedBubbles.Add(CreateBubble(m));
        FinishRenderPass(msgs);
    }

    // Instantiates only the messages in `msgs` not yet on screen, leaving existing bubbles
    // untouched so the ScrollRect's content doesn't shrink-then-regrow. Takes the list
    // explicitly (rather than recomputing it) so a caller mid-reply-exchange (see
    // ReplyWithTypingDelay) can cap exactly how far this reveals.
    private void AppendNewMessages(List<TextMessage> msgs)
    {
        for (int i = renderedBubbles.Count; i < msgs.Count; i++)
            renderedBubbles.Add(CreateBubble(msgs[i]));
        FinishRenderPass(msgs);
    }

    // Holds the rendered thread at the first unanswered, unlocked NPC quick-reply message.
    // Later messages from this character still exist in storage with their real timestamps
    // (nothing here touches TextThreads' data), they just aren't shown yet - so the player
    // always resolves replies in the order the conversations actually happened, instead of
    // a newer message (from an unrelated conversation elsewhere) ever being visible/
    // answerable before an older one still awaiting a reply.
    //
    // Callers mid-reply-exchange (see ReplyWithTypingDelay) must NOT re-derive their reveal
    // list by re-scanning this with a position count: a message delivered earlier in real
    // time at a different location, but still held back pending its own reply, can sort
    // chronologically BETWEEN repliedTo and the player's brand-new reply (its timestamp
    // predates this exchange even though it was discovered by the player after). A count-
    // based cap would then land on THAT message instead of this exchange's own player-reply/
    // follow-up. Build the reveal list explicitly from what's already known there instead.
    private static List<TextMessage> EffectiveThread(Character who)
    {
        var all = TextThreads.GetThread(who);
        int week = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));

        for (int i = 0; i < all.Count; i++)
        {
            var m = all[i];
            if (!m.isPlayer && m.quickReplies != null && m.quickReplies.Count > 0 &&
                (m.unlockWeek <= 0 || week >= m.unlockWeek))
                return all.GetRange(0, i + 1);
        }

        return all;
    }

    private void FinishRenderPass(List<TextMessage> msgs)
    {
        RenderQuickReplies(msgs);
        if (current != Character.NONE) TextThreads.MarkRead(current, msgs);
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

        // See _resolvingReply's own comment: no buttons while a reply exchange is in flight,
        // independent of what msgs contains.
        if (_resolvingReply)
        {
            quickReplyRoot.gameObject.SetActive(false);
            return;
        }

        TextMessage pendingMessage = null;
        QuickReply[] pending = null;
        string pendingTargetScene = null;

        foreach (var m in msgs)
        {
            // If this NPC message offers quick replies that are now unlocked, resolve it.
            // unlockWeek=0 means immediately available; otherwise wait until that week.
            if (!m.isPlayer && m.quickReplies != null && m.quickReplies.Count > 0)
            {
                int week = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));
                if (m.unlockWeek <= 0 || week >= m.unlockWeek)
                {
                    // Oldest unanswered message wins -- msgs is ordered ascending by
                    // unixTime (TextThreads.GetThread), so the FIRST match here is the
                    // earliest-arrived message still awaiting a reply. Two independent
                    // NodeMessage beats (e.g. one fired from an Apartment conversation,
                    // another from a Lecture Hall conversation) can each land in this
                    // shared thread with unanswered quick replies at the same time;
                    // always resolving the older one first keeps replies in the order
                    // messages actually arrived instead of whichever was most recent.
                    pendingMessage = m;
                    pending = m.quickReplies.ToArray();
                    pendingTargetScene = m.locationForTimingOnly ? null : m.location;
                    break;
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
                        StartCoroutine(ReplyWithTypingDelay(pendingMessage, qr, advanceDayAfter: true));
                        return;
                    }

                    // "decline": stay in phone thread. Anything else with a pending
                    // location: play the reply out, then navigate there.
                    bool isDecline = IsSpecialPayload(qr.payload, "decline");
                    string targetScene = (!isDecline && !string.IsNullOrWhiteSpace(pendingTargetScene))
                        ? pendingTargetScene
                        : null;

                    StartCoroutine(ReplyWithTypingDelay(pendingMessage, qr, targetSceneAfter: targetScene));
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

    private IEnumerator ReplyWithTypingDelay(TextMessage repliedTo, QuickReply qr, string targetSceneAfter = null, bool advanceDayAfter = false)
    {
        // Set for the ENTIRE exchange (through the optional leaving-thread fade/navigate at
        // the end) so RenderQuickReplies can never show a button -- for any message, for any
        // reason -- until this whole coroutine has finished and the player triggers a genuine
        // fresh render. try/finally (no catch) containing yield return is legal in an iterator
        // method; this guarantees the flag can't be left stuck true by an early return.
        _resolvingReply = true;
        try
        {
            // Snapshot exactly what's visible right now, by reference, before SendPlayerResponse
            // clears repliedTo's quickReplies. Everything revealed below is built by explicitly
            // extending THIS list with the exact messages this exchange adds -- not by re-scanning
            // the full thread with a position count, which breaks once another message exists
            // that was delivered earlier in real time (e.g. at a different location, visited
            // before the player got around to replying here) but is still held back pending its
            // own reply: that message can sort chronologically BETWEEN repliedTo and the player's
            // new reply, so a count-based cap can land on IT instead of this exchange's own
            // messages. See EffectiveThread's own comment.
            var visibleBeforeReply = EffectiveThread(current);

            TextMessage playerMsg = TextThreads.SendPlayerResponse(current, repliedTo, qr);
            var afterPlayerReply = new List<TextMessage>(visibleBeforeReply) { playerMsg };
            AppendNewMessages(afterPlayerReply); // shows the player's bubble; scrolls down once

            if (!string.IsNullOrWhiteSpace(qr.npcResponse))
            {
                var typing = ShowTypingIndicator();
                ScrollToBottom();

                yield return new WaitForSeconds(typingDelaySeconds);

                // Anchored to the player's reply, not wall-clock time when this delayed append
                // actually executes -- guarantees this follow-up always sorts immediately after
                // the reply it belongs to, even if an unrelated NodeMessage elsewhere gets
                // delivered (and timestamped) during the typing-indicator delay.
                TextThreads.AppendNpcReply(current, qr.npcResponse, playerMsg.unixTime);

                // Morph the "..." bubble into the real reply in place, instead of tearing
                // down and rebuilding the whole thread - avoids a second scroll jump and
                // matches how modern messaging apps resolve a typing indicator.
                ResolveTypingIndicator(typing, qr.npcResponse);
                renderedBubbles.Add(typing.go);

                // Mirrors exactly what AppendNpcReply just wrote to storage, for the same reason
                // afterPlayerReply was built explicitly above.
                var npcFollowup = new TextMessage(current, qr.npcResponse, location: null)
                {
                    isPlayer = false,
                    unixTime = playerMsg.unixTime + 1,
                };
                FinishRenderPass(new List<TextMessage>(afterPlayerReply) { npcFollowup });
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
            else
            {
                // This exchange is fully resolved and we're staying in the phone - continue
                // the conversation naturally instead of requiring a manual close/reopen to
                // see what's next. Must clear the guard BEFORE this render pass (the `finally`
                // below runs too late for this call) so RenderQuickReplies is actually allowed
                // to show the next message's buttons if EffectiveThread's natural, uncapped
                // scan now finds one newly eligible (guaranteed to sort correctly, since every
                // timestamp in this exchange is anchored to repliedTo, not wall-clock time).
                // If nothing new is eligible, this is a harmless no-op.
                _resolvingReply = false;
                AppendNewMessages(EffectiveThread(current));
            }
        }
        finally
        {
            _resolvingReply = false;
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
