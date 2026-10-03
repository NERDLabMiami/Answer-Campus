using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VNEngine;

public class ClickOutsideToClose : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IEndDragHandler
{
    [Header("Behavior")]
    public bool ContinueConversationOnClick = false;

    public GameObject rootObjectToClose;
    public GameObject defaultSelectionOnClose;

    // internal state
    private bool pointerDownInside = false;
    private bool dragged = false;
    private int activePointerId = -10; // -1 for mouse, >=0 for touch

    void Update()
    {
        // Allow Escape to close (desktop)
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        activePointerId = eventData.pointerId;
        dragged = false;
        pointerDownInside = IsPointerOverThisModal(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.pointerId == activePointerId) dragged = true;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.pointerId == activePointerId) dragged = false; // drag finished; wait for Up
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointerId) return;

        // We only close if: press started outside, we did not drag, and we are not currently over the modal
        bool upOverModal = IsPointerOverThisModal(eventData);
        if (!pointerDownInside && !dragged && !upOverModal)
        {
            Close();
        }

        // reset
        activePointerId = -10;
        dragged = false;
        pointerDownInside = false;
    }

    private void Close()
    {
        // Deactivate the modal root
        rootObjectToClose.SetActive(false);

        // Optionally resume VNEngine waiting nodes
        if (ContinueConversationOnClick)
        {
            VNSceneManager.Waiting_till_true = true;
        }

        // Optional UI selection handoff
        if (EventSystem.current != null && defaultSelectionOnClose != null)
        {
            EventSystem.current.SetSelectedGameObject(defaultSelectionOnClose);
        }
    }

    private bool IsPointerOverThisModal(PointerEventData eventData)
    {
        if (EventSystem.current == null) return false;

        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);

        Transform root = transform;
        foreach (var r in results)
        {
            if (r.gameObject != null && r.gameObject.transform != null)
            {
                if (r.gameObject.transform == root || r.gameObject.transform.IsChildOf(root))
                    return true; // hit anywhere inside the modal hierarchy (buttons, images, scrollviews, etc.)
            }
        }
        return false;
    }
}
