// Phone.cs  (refactor – keep animator + public hooks)

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VNEngine;

public enum PhoneMode { Home, Conversation }

public class Phone : MonoBehaviour
{
    public TextMeshProUGUI title;
    [Header("Panels")]
    public GameObject mapPanel;
    public GameObject friendsPanel;
    public GameObject agendaPanel;

    [Header("Sub-Views")]
    public MapView mapView;
    public FriendsView friendsView;
    public AgendaView agendaView;
    [SerializeField] private GameObject[] overlayPanels;

    [Header("Mode")]
    [SerializeField] private GameObject mapNavButton;

    private Animator anim;
    private PhoneMode _mode = PhoneMode.Home;

    void Start()
    {
        anim = GetComponent<Animator>();
        RefreshNotificationBadge();
        ShowFriends(); // default
        friendsView.headerText = title;
    }

    // ---- Mode ----
    public void SetHomeMode()
    {
        _mode = PhoneMode.Home;
        if (mapNavButton) mapNavButton.SetActive(true);
        if (friendsView?.threadPanel != null)
            friendsView.threadPanel.allowReplies = true;
    }

    public void SetConversationMode()
    {
        _mode = PhoneMode.Conversation;
        if (mapNavButton) mapNavButton.SetActive(false);
        if (friendsView?.threadPanel != null)
            friendsView.threadPanel.allowReplies = false;
    }

    // ---- Public tab actions (UI buttons) ----
    public void ShowMap()
    {
        if (_mode == PhoneMode.Conversation) return;
        title.text = "Finder";
        HideOverlays();
        TogglePanels(map: true);

        int currentWeek = (int)VNEngine.StatsManager.Get_Numbered_Stat("Week");
        var pins = PhoneDataService.GetCharacterLocations();

        var ctx = new MapAvailability.Context {
            currentWeek = currentWeek,
            requireFriendToTrack = true,
            characterPins = pins,
            lockableLocationsScene = Array.Empty<Location>(),
            allSceneLocations      = Array.Empty<Location>(),
            npcRouteIndices        = Resources.LoadAll<StageRouteIndex>(""),
            getThisWeeksGame = FootballScheduler.GetThisWeeksGame,
            isHomeGame      = g => ((FootballGame)g).isHome,
            gamePlayed      = g => ((FootballGame)g).played
        };
        Debug.Log($"[PHONE] pins: {string.Join(", ", pins.Select(p => $"{p.character}@{p.location}"))}");
        var idxs = Resources.LoadAll<StageRouteIndex>("");
        Debug.Log($"[PHONE] RouteIndex assets: {idxs.Length}");
        var lds  = Resources.LoadAll<LocationData>("");
        Debug.Log($"[PHONE] LocationData assets: {lds.Length}");

        var snapshot = MapAvailability.Build(ctx);
        
        var visible = snapshot
            .Values
            .Where(st =>
                st != null &&
                st.interactable &&
                (st.friends?.Count ?? 0) > 0)                    // <-- require stage-valid friends
            .OrderBy(st => st.displayName ?? st.locationName)
            .ToDictionary(st => st.locationName, st => st, StringComparer.Ordinal);
        
        mapView.RenderSnapshot(visible);
    }

    public void ShowFriends()
    {
        title.text = "Friends";
        HideOverlays();
        TogglePanels(friends:true);
        friendsView.Render();
        ClearNotifications(); // opening inbox clears
    }

    public void ShowAgenda()
    {
        title.text = "Agenda";
        HideOverlays();
        TogglePanels(agenda:true);
        agendaView.Render();
    }

    // ---- Notifications ----
    public void RefreshNotificationBadge()
    {
        var threads = PhoneDataService.GetMessageThreads();
        bool hasNew = threads.Count > 0;
        if (anim != null)
            anim.SetTrigger(hasNew ? "notification" : "default"); // your existing triggers
    }

    public void ClearNotifications()
    {
        if (anim != null) anim.SetTrigger("default");
    }

    private void TogglePanels(bool map=false, bool friends=false, bool agenda=false)
    {
        friendsView.HideThread();
        if (mapPanel) mapPanel.SetActive(map);
        if (friendsPanel) friendsPanel.SetActive(friends);
        if (agendaPanel) agendaPanel.SetActive(agenda);

        HideOverlays();
    }
    private void HideOverlays()
    {
        if (overlayPanels == null) return;

        foreach (var go in overlayPanels)
        {
            if (!go) continue;

            // Prefer component-driven hide (handles CanvasGroup + child-root cases)
            var ttp = go.GetComponent<TextThreadPanel>() ?? go.GetComponentInChildren<TextThreadPanel>(true);
            if (ttp != null)
            {
                ttp.Hide();
                continue;
            }

            // Fallback: brute-force hide this GO
            var cg = go.GetComponent<CanvasGroup>();
            if (cg)
            {
                cg.alpha = 0;
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
            go.SetActive(false);
        }
    }

}