using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MapView : MonoBehaviour
{
    [Header("Prefabs & Roots")]
    public Transform locationsRoot;            // container for location buttons
    public GameObject locationButtonPrefab;    // prefab that has LocationButton on it

    [Header("Events")]
    public StringEvent onLocationSelected;     // (string locationName)
    [Serializable] public class StringEvent : UnityEvent<string> {}

    public void RenderSnapshot(Dictionary<string, MapAvailability.Status> snapshot)
    {
        foreach (Transform child in locationsRoot) Destroy(child.gameObject);
        if (snapshot == null || snapshot.Count == 0) return;

        foreach (var st in snapshot.Values.OrderBy(s => s.displayName ?? s.locationName))
        {
            var go = Instantiate(locationButtonPrefab, locationsRoot);
            var lb = go.GetComponent<LocationButton>();
            if (!lb) { Debug.LogError("LocationButton missing"); continue; }

            // Route by sceneName (same wiring as big map via LocationRouter)
            System.Action onClick = () => HomeCutsceneController.NavigateOut(st.locationName, st.displayName);

            // Bind: use the FRIENDLY name for the label (not the route key)
            lb.Bind(st.displayName ?? st.locationName, st.friends ?? new List<Character>(), onClick);

            // Phone list only contains open items, so keep buttons enabled
            if (lb.button) lb.button.interactable = true;
        }
    }




}