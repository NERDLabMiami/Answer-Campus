using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace VNEngine
{
    // Paired with ShowChoiceNode on the same GameObject. ShowChoiceNode calls
    // BeginCountdown() directly (from its own Run_Node()) once its buttons are painted,
    // since the ConversationManager sequencer never runs two sibling nodes concurrently.
    public class TimedChoiceNode : Node
    {
        public float timer = 30;
        public ConversationManager default_choice;

        private ShowChoiceNode owner;
        private List<Button> activeButtons;
        private List<ConversationManager> slotConversations;
        private List<int> slotOrigIndices;
        private Coroutine timerCoroutine;

        // The real work already ran synchronously via BeginCountdown(); if the sequencer
        // later reaches this node's own slot (the "continue current conversation" path),
        // just pass through to the next node.
        public override void Run_Node()
        {
            base.Finish_Node();
        }

        public void BeginCountdown(ShowChoiceNode owner, List<Button> activeButtons, List<ConversationManager> slotConversations, List<int> slotOrigIndices)
        {
            this.owner = owner;
            this.activeButtons = activeButtons;
            this.slotConversations = slotConversations;
            this.slotOrigIndices = slotOrigIndices;

            timerCoroutine = StartCoroutine(RunCountdown());
        }

        public void StopTimer()
        {
            StopAllCoroutines();
            timerCoroutine = null;
        }

        private IEnumerator RunCountdown()
        {
            int defaultSlot = default_choice != null ? slotConversations.IndexOf(default_choice) : -1;

            // Every slot slips away except the one protecting default_choice (if any).
            var eliminationOrder = new List<int>();
            for (int slot = 0; slot < activeButtons.Count; slot++)
                if (slot != defaultSlot) eliminationOrder.Add(slot);

            // Fisher-Yates shuffle so choices vanish in a random order.
            for (int i = 0; i < eliminationOrder.Count; i++)
            {
                int j = Random.Range(i, eliminationOrder.Count);
                (eliminationOrder[i], eliminationOrder[j]) = (eliminationOrder[j], eliminationOrder[i]);
            }

            // One slice per elimination. A protected default gets one extra slice reserved
            // at the end for its auto-selection; otherwise the last elimination itself
            // lands exactly at time's up (no dead trailing wait).
            int slices = defaultSlot >= 0 ? eliminationOrder.Count + 1 : Mathf.Max(1, eliminationOrder.Count);
            float interval = timer / slices;

            for (int i = 0; i < slices; i++)
            {
                yield return new WaitForSeconds(interval);
                if (i < eliminationOrder.Count)
                    yield return EliminateButton(activeButtons[eliminationOrder[i]]);
            }

            if (defaultSlot >= 0)
                owner.SelectChoiceExternally(slotOrigIndices[defaultSlot]);
            else if (default_choice != null)
                owner.ForceJumpExternally(default_choice);
            else
                Debug.LogWarning("TimedChoiceNode timed out with no matching or default choice set.");
        }

        private IEnumerator EliminateButton(Button btn)
        {
            if (btn == null || !btn.gameObject.activeSelf) yield break;

            btn.interactable = false;

            var cg = btn.GetComponent<CanvasGroup>();
            if (cg == null) cg = btn.gameObject.AddComponent<CanvasGroup>();

            const float duration = 0.25f;
            float elapsed = 0f;
            float startAlpha = cg.alpha;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                cg.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / duration);
                yield return null;
            }
            cg.alpha = 0f;
            btn.gameObject.SetActive(false);

            EnsureGamepadSelection();
        }

        // Keeps gamepad focus alive: whatever caused it (elimination, or the button's own
        // Selectable/EventSystem machinery clearing selection as it disables), if nothing
        // valid is selected after an elimination, hand focus to the next surviving button.
        private void EnsureGamepadSelection()
        {
            if (Gamepad.current == null || EventSystem.current == null) return;

            var current = EventSystem.current.currentSelectedGameObject;
            if (current != null && current.activeInHierarchy) return; // still a live selection

            SelectFirstRemainingButton();
        }

        private void SelectFirstRemainingButton()
        {
            foreach (var b in activeButtons)
            {
                if (b != null && b.gameObject.activeSelf)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                    EventSystem.current.SetSelectedGameObject(b.gameObject);
                    return;
                }
            }
        }
    }
}
