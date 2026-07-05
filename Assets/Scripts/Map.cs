using System;                // for Array.Find/Exists
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VNEngine;
using System;
using System.Linq;
using System.Collections.Generic;
using TMPro.SpriteAssetUtilities;
using UnityEngine;

// A single-pass "truth" about what the phone/fullscreen map should show right now.
public static class MapAvailability
{
    public enum LockReason { None, InviteOnly, TimeGated }

    public sealed class Status
    {
        public string locationName;                // label shown on phone map
        public string displayName;
        public bool interactable;                  // can navigate?
        public LockReason lockReason;             // if not interactable, why
        public bool hasFriendChip;                 // any character physically there now
        public List<Character> friends = new();    // for FriendChips
        public bool hasAvailableConversation;      // your "AvailableBadge" concept
        public Sprite mapIcon;
        public Sprite phoneIcon;
    }

    public sealed class Context
    {
        public int currentWeek;
        public int dayPhase;           // 0 = morning, 1 = afternoon
        public bool requireFriendToTrack;

        public IEnumerable<Location> lockableLocationsScene = Array.Empty<Location>();
        public IEnumerable<string>   lockableLocationNames  = Array.Empty<string>(); // ← ADD

        public IEnumerable<CharacterLocation> characterPins = Array.Empty<CharacterLocation>();
        public IEnumerable<Location> allSceneLocations = Array.Empty<Location>();
        public IEnumerable<StageRouteIndex> npcRouteIndices = Array.Empty<StageRouteIndex>();

        public Func<int, object> getThisWeeksGame;
        public Func<object, bool> isHomeGame;
        public Func<object, bool> gamePlayed;

        public IEnumerable<Character> footballInvitees = Array.Empty<Character>();
    }
    // Main entry. Builds a per-location status table using the same logic your Map.Start() applies.
public static Dictionary<string, Status> Build(Context ctx)
{
    var result = new Dictionary<string, Status>(StringComparer.Ordinal);

    // 0) Football weekly gating — only in afternoon (DayPhase=1)
    bool footballInteractable = false;
    if (ctx.getThisWeeksGame != null && ctx.isHomeGame != null && ctx.gamePlayed != null)
    {
        var g = ctx.getThisWeeksGame(ctx.currentWeek);
        footballInteractable = (g != null && ctx.isHomeGame(g) && !ctx.gamePlayed(g) && ctx.dayPhase == 1);
    }

    // --- Build a map from scene key -> displayName/icon from LocationData assets ---
    var displayNameByKey = new Dictionary<string, string>(StringComparer.Ordinal);
    var iconByKey        = new Dictionary<string, Sprite>(StringComparer.Ordinal);

    // (a) From LocationData assets (authority)
    var allLocationAssets = Resources.LoadAll<LocationData>("");
    var ldBySceneName = new Dictionary<string, LocationData>(StringComparer.Ordinal);
    foreach (var ld in allLocationAssets)
    {
        if (ld == null) continue;
        var key = !string.IsNullOrWhiteSpace(ld.sceneName) ? ld.sceneName : ld.name;
        if (string.IsNullOrWhiteSpace(key)) continue;

        if (!displayNameByKey.ContainsKey(key))
            displayNameByKey[key] = string.IsNullOrWhiteSpace(ld.displayName) ? key : ld.displayName;

        if (ld.mapIcon && !iconByKey.ContainsKey(key))
            iconByKey[key] = ld.mapIcon;

        if (!ldBySceneName.ContainsKey(key))
            ldBySceneName[key] = ld;
    }

    // 1) Authoritative universe of keys (sceneName preferred)
    var allNames = new HashSet<string>(displayNameByKey.Keys, StringComparer.Ordinal); // seed with LocationData

    // (b) From StageRouteIndex (routes reference LocationData)
    foreach (var idx in ctx.npcRouteIndices ?? Array.Empty<StageRouteIndex>())
    {
        if (idx?.Routes == null) continue;
        foreach (var r in idx.Routes)
        {
            if (r?.location == null) continue;
            var key = !string.IsNullOrWhiteSpace(r.location.sceneName) ? r.location.sceneName : r.location.name;
            if (string.IsNullOrWhiteSpace(key)) continue;
            allNames.Add(key);

            // If the route points to an LD that has a displayName/icon we haven’t captured yet, capture it.
            if (!displayNameByKey.ContainsKey(key))
                displayNameByKey[key] = string.IsNullOrWhiteSpace(r.location.displayName) ? key : r.location.displayName;
            if (r.location.mapIcon && !iconByKey.ContainsKey(key))
                iconByKey[key] = r.location.mapIcon;
        }
    }

    // (c) From live scene Locations (big map)
    foreach (var loc in ctx.allSceneLocations ?? Array.Empty<Location>())
    {
        string key = null;
        if (!string.IsNullOrWhiteSpace(loc.scene)) key = loc.scene;
        else if (loc.data && !string.IsNullOrWhiteSpace(loc.data.sceneName)) key = loc.data.sceneName;
        else if (loc.data && !string.IsNullOrWhiteSpace(loc.data.name)) key = loc.data.name;
        if (string.IsNullOrWhiteSpace(key)) continue;

        allNames.Add(key);

        // Prefer LocationData.displayName if available; otherwise use key
        if (!displayNameByKey.ContainsKey(key))
        {
            var friendly = (loc.data && !string.IsNullOrWhiteSpace(loc.data.displayName))
                ? loc.data.displayName
                : key;
            displayNameByKey[key] = friendly;
        }
        if (loc.data && loc.data.mapIcon && !iconByKey.ContainsKey(key))
            iconByKey[key] = loc.data.mapIcon;
    }

    // (d) From pins as last resort (routing key only; no display info here)
    foreach (var cl in (ctx.characterPins ?? Array.Empty<CharacterLocation>()))
        if (!string.IsNullOrWhiteSpace(cl.location))
            allNames.Add(cl.location);

    // Persist the universe once (optional)
    try
    {
        PlayerPrefsExtra.SetList("registryLocationNames", allNames.ToList());
        PlayerPrefs.Save();
    }
    catch (Exception e)
    {
        Debug.LogWarning($"[MapAvailability] persist registryLocationNames failed: {e.Message}");
    }

    // 2) Lookups

    // Pins: location -> distinct characters
    var pinsByLoc = (ctx.characterPins ?? Array.Empty<CharacterLocation>())
        .Where(p => !string.IsNullOrWhiteSpace(p.location))
        .GroupBy(p => p.location)
        .ToDictionary(
            g => g.Key,
            g => g.Select(p => p.character).Distinct().ToList(),
            StringComparer.Ordinal
        );

    // Lockables (big map writes, phone reads)
    var lockableNames = new HashSet<string>(StringComparer.Ordinal);
    foreach (var l in ctx.lockableLocationsScene ?? Array.Empty<Location>())
    {
        string n = null;
        if (!string.IsNullOrWhiteSpace(l.scene)) n = l.scene;
        else if (l.data && !string.IsNullOrWhiteSpace(l.data.sceneName)) n = l.data.sceneName;
        else if (l.data) n = l.data.name;
        if (!string.IsNullOrWhiteSpace(n)) lockableNames.Add(n);

        // Also capture friendly name/icon from this scene object if we don’t have them yet
        if (l.data)
        {
            if (!displayNameByKey.ContainsKey(n))
                displayNameByKey[n] = string.IsNullOrWhiteSpace(l.data.displayName) ? n : l.data.displayName;
            if (l.data.mapIcon && !iconByKey.ContainsKey(n))
                iconByKey[n] = l.data.mapIcon;
        }
    }
    if (lockableNames.Count == 0)
    {
        try
        {
            var persisted = PlayerPrefsExtra.GetList<string>("lockableLocationNames", new List<string>());
            foreach (var n in persisted)
                if (!string.IsNullOrWhiteSpace(n)) lockableNames.Add(n);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MapAvailability] load lockables failed: {e.Message}");
        }
    }
    else
    {
        try
        {
            PlayerPrefsExtra.SetList("lockableLocationNames", lockableNames.ToList());
            PlayerPrefs.Save();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MapAvailability] persist lockables failed: {e.Message}");
        }
    }

    // 2b) Per-character, per-location route lookup (all stage+unlockWeek pairs).
    // Mirrors CharacterStageRouterNode's exact matching logic so map and router stay in sync.
    var routesByCharLoc = new Dictionary<Tuple<Character, string>, List<(int stage, int unlockWeek)>>();

    foreach (var idx in ctx.npcRouteIndices ?? Array.Empty<StageRouteIndex>())
    {
        if (idx == null || idx.Routes == null) continue;

        foreach (var r in idx.Routes)
        {
            if (r?.location == null) continue;
            var who = r.character;
            if (who == Character.NONE) continue;
            var key = !string.IsNullOrWhiteSpace(r.location.sceneName)
                ? r.location.sceneName
                : r.location.name;
            if (string.IsNullOrWhiteSpace(key)) continue;

            var tuple = Tuple.Create(who, key);
            if (!routesByCharLoc.TryGetValue(tuple, out var list))
            {
                list = new List<(int stage, int unlockWeek)>();
                routesByCharLoc[tuple] = list;
            }
            list.Add((r.stage, r.unlockWeek));
        }
    }

    // 3) Route window helper (location-level gating for lockables)
    bool IsInRouteWindow(string locName)
    {
        if (ctx.npcRouteIndices == null || !ctx.npcRouteIndices.Any())
            return true; // phone fallback: lockable+invite handles gating
        foreach (var idx in ctx.npcRouteIndices)
        {
            if (idx?.Routes == null) continue;
            foreach (var r in idx.Routes)
            {
                if (r?.location == null) continue;
                var key = !string.IsNullOrWhiteSpace(r.location.sceneName) ? r.location.sceneName : r.location.name;
                if (!string.Equals(key, locName, StringComparison.Ordinal)) continue;
                if (r.unlockWeek <= 0 || ctx.currentWeek >= r.unlockWeek) return true;
            }
        }
        return false;
    }

    // 4) Build Status objects (now with displayName/icon)
    foreach (var name in allNames.OrderBy(n => n))
    {
        var st = new Status
        {
            locationName = name,
            displayName  = displayNameByKey.TryGetValue(name, out var dn) ? dn : name,
            mapIcon      = iconByKey.TryGetValue(name, out var ic) ? ic : null
        };

        // Base interactability
        if (!lockableNames.Contains(name))
        {
            st.interactable = true;
            st.lockReason   = LockReason.None;
        }
        else
        {
            bool hasInvite = pinsByLoc.TryGetValue(name, out var inviteFriends) && inviteFriends.Count > 0;
            bool inWindow  = IsInRouteWindow(name);
            st.interactable = hasInvite && inWindow;
            st.lockReason   = st.interactable ? LockReason.None : LockReason.InviteOnly;
        }

        // Football & Shuttle gating override
        // "Cheer" is included because Shuttle.asset has sceneName = "Cheer"
        if (name.Equals("Football Game", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Shuttle", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Cheer", StringComparison.OrdinalIgnoreCase))
        {
            st.interactable = footballInteractable;
            st.lockReason   = st.interactable ? LockReason.None : LockReason.TimeGated;

            if (footballInteractable && ctx.footballInvitees != null)
            {
                foreach (var c in ctx.footballInvitees)
                {
                    if (!st.friends.Contains(c))
                        st.friends.Add(c);
                }
                st.hasFriendChip = st.friends.Count > 0;
            }
        }

        // Morning-only location gate (e.g. Lecture Hall — set morningOnly on its LocationData)
        if (ldBySceneName.TryGetValue(name, out var locData) && locData.morningOnly && ctx.dayPhase != 0)
        {
            st.interactable = false;
            st.lockReason   = LockReason.TimeGated;
        }

        // Presence from pins, stage-and-week-gated per character.
        // A character is shown only if the router would actually fire a route for them right now.
        if (pinsByLoc.TryGetValue(name, out var occupants))
        {
            var tracked = new List<Character>();

            foreach (var c in occupants)
            {
                // Respect requireFriendToTrack if requested (phone map)
                if (ctx.requireFriendToTrack && !Friend.IsFriend(c))
                    continue;

                var tuple = Tuple.Create(c, name);

                if (routesByCharLoc.TryGetValue(tuple, out var charRoutes) && charRoutes.Count > 0)
                {
                    // Mirror the router: character's current stage stat must exactly match
                    // a route, and that route's week must be unlocked.
                    string statKey = $"{c} - {name} - Stage";
                    int currentStage = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat(statKey));

                    bool hasEligibleRoute = false;
                    foreach (var (routeStage, unlockWeek) in charRoutes)
                    {
                        if (routeStage == currentStage &&
                            (unlockWeek <= 0 || ctx.currentWeek >= unlockWeek))
                        {
                            hasEligibleRoute = true;
                            break;
                        }
                    }

                    if (!hasEligibleRoute)
                        continue; // No live route → hide portrait
                }
                // No entries in routesByCharLoc for (c, name): npcRouteIndices empty or
                // this location has no route data → allow through (graceful degradation).

                tracked.Add(c);
            }

            st.friends       = tracked;
            st.hasFriendChip = tracked.Count > 0;
        }

        result[name] = st;
    }

    Debug.Log($"[BUILD] allNames ({allNames.Count}): {string.Join(", ", allNames)}");
    return result;
}

}

public class Map : MonoBehaviour
{
    public Location[] lockableLocations;
    private List<CharacterLocation> characterLocations;
    public Characters characters;
    public Location footballGame;

    [Header("Conversation Availability")]
    public List<StageRouteIndex> npcRouteIndices = new(); // ← list, not single
    public bool requireFriendToTrack = false;              // Sentinel gate

    void Start()
{
    int currentWeek = (int)StatsManager.Get_Numbered_Stat("Week");

    characterLocations = PlayerPrefsExtra.GetList<CharacterLocation>("characterLocations", new List<CharacterLocation>());
    var allLocations = GetComponentsInChildren<Location>(true);

    int dayPhase = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("DayPhase"));

    var invitees = new List<Character>();
    foreach (Character c in System.Enum.GetValues(typeof(Character)))
    {
        if (StatsManager.Get_Boolean_Stat(c.ToString().ToLower() + "_football_invite_accepted"))
            invitees.Add(c);
    }

    var ctx = new MapAvailability.Context {
        currentWeek = currentWeek,
        dayPhase = dayPhase,
        requireFriendToTrack = requireFriendToTrack,
        lockableLocationsScene = lockableLocations,          // your array from the Inspector
        characterPins = characterLocations,
        allSceneLocations = allLocations,
        npcRouteIndices = npcRouteIndices,
        getThisWeeksGame = FootballScheduler.GetThisWeeksGame,
        isHomeGame = g => ((FootballGame)g).isHome,
        gamePlayed = g => ((FootballGame)g).played,
        footballInvitees = invitees
    };

    var snapshot = MapAvailability.Build(ctx);

foreach (var loc in allLocations)
{
    // Resolve key: must match what MapAvailability.Build used
    string name = null;
    if (!string.IsNullOrWhiteSpace(loc.scene))
        name = loc.scene;
    else if (loc.data && !string.IsNullOrWhiteSpace(loc.data.sceneName))
        name = loc.data.sceneName;
    else if (loc.data && !string.IsNullOrWhiteSpace(loc.data.name))
        name = loc.data.name;
    else
        name = loc.name;

    if (!snapshot.TryGetValue(name, out var st))
    {
        Debug.Log($"[MAP] No snapshot status for {name}");
        continue;
    }

    // --- DEBUG: what does the map think is here? ---
    var friendsList = (st.friends == null || st.friends.Count == 0)
        ? "none"
        : string.Join(", ", st.friends.Select(c => c.ToString()));

    // Button interactable
    var btn = loc.GetComponent<Button>();
    if (btn) btn.interactable = st.interactable;

    // Portraits for presence (independent of availability)
    if (loc.characterWaiting)
    {
        if (st.friends == null || st.friends.Count == 0)
        {
            // Nothing to show here
            loc.characterWaiting.gameObject.SetActive(false);
        }
        else
        {
            Character who = Character.NONE;

            // 1) Prefer a friend who actually has a pictureLarge
            foreach (var c in st.friends)
            {
                var p = Array.Find(characters.profiles, prof => prof.character == c);
                bool isFriend = Friend.IsFriend(c);
                bool hasPic   = (p.pictureLarge != null);
                Debug.Log($"[MAP] Candidate for {name}: {c}, isFriend={isFriend}, pictureLarge={hasPic}");

                if (isFriend && hasPic)
                {
                    who = c;
                    break;
                }
            }

            // 2) Fallback: any occupant with a usable portrait
            if (who == Character.NONE)
            {
                foreach (var c in st.friends)
                {
                    var p = Array.Find(characters.profiles, prof => prof.character == c);
                    if (p.pictureLarge != null)
                    {
                        who = c;
                        break;
                    }
                }
            }

            if (who != Character.NONE)
            {
                var profile = Array.Find(characters.profiles, p => p.character == who);
                loc.characterWaiting.sprite = profile.pictureLarge;
                loc.characterWaiting.gameObject.SetActive(st.hasFriendChip);
            }
            else
            {
                loc.characterWaiting.gameObject.SetActive(false);
            }
        }
    }

    // "Available" badge
    var badge = loc.transform.Find("AvailableBadge");
    if (badge) badge.gameObject.SetActive(st.hasAvailableConversation);
}

}

}
