using UnityEngine;
using System.Collections.Generic;
using FMODUnity;


namespace VNEngine
{
    public class CharacterStageRouterNode : Node
    {
        [System.Serializable]
        public class StageConversation
        {
            public Character character;
            [HideInInspector] public int stage; // auto-synced from StageRouteIndex via OnValidate
            [Min(0)] public int unlockWeek = 0;
            public ConversationManager conversation;
        }
        public StageRouteIndex stageRouteIndex;
        public List<StageConversation> routes;
        public string currentSceneName; // Match PlayerPrefsExtra location value (e.g., "Library")
        
        
        public EventReference ambientFMODEventName;
        public EventReference musicFMODEventName;
        public List<ConversationManager> fallbackConversations;
        public ConversationManager repeatableFallback;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Only do this in the editor to keep runtime clean
            if (stageRouteIndex == null) return;
            if (string.IsNullOrEmpty(currentSceneName)) return;

            // 1) Look up all index routes for this scene
            var sceneRoutes = stageRouteIndex.GetRoutesForScene(currentSceneName);
            if (sceneRoutes == null) return;

            // 2) Build a new list, preserving existing conversation refs
            var newRoutes = new List<StageConversation>();

            foreach (var meta in sceneRoutes)
            {
                if (meta == null) continue;

                // Match by character + unlockWeek (stage is auto-computed, unlockWeek is stable)
                StageConversation existing = null;
                if (routes != null)
                {
                    existing = routes.Find(r =>
                        r.character == meta.character &&
                        r.unlockWeek == meta.unlockWeek);
                }

                var sc = existing ?? new StageConversation();
                sc.character   = meta.character;
                sc.stage       = meta.stage;
                sc.unlockWeek  = meta.unlockWeek;
                // sc.conversation stays whatever you previously assigned
                newRoutes.Add(sc);
            }

            routes = newRoutes;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

public override void Run_Node()
{
    // --- Audio bootstrap (unchanged) ---
    string audioKey = $"AudioStarted - {currentSceneName}";
    if (!StatsManager.Get_Boolean_Stat(audioKey))
    {
        StatsManager.Set_Boolean_Stat(audioKey, true);

        if (FMODAudioManager.Instance != null)
        {
            if (!musicFMODEventName.IsNull)
                FMODAudioManager.Instance.PlayMusic(musicFMODEventName);

            if (!ambientFMODEventName.IsNull)
                FMODAudioManager.Instance.PlayAmbient(ambientFMODEventName);
        }
    }

    int currentWeek = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));

    List<CharacterLocation> characterLocations =
        PlayerPrefsExtra.GetList<CharacterLocation>("characterLocations", new List<CharacterLocation>());

    Debug.Log($"[ROUTER] {characterLocations.Count} Character Locations (scene='{currentSceneName}', week={currentWeek})");

    // --- NEW: collect all pins for this scene, newest-first ---
    var pinsHere = new List<CharacterLocation>();
    for (int i = characterLocations.Count - 1; i >= 0; i--)
    {
        var loc = characterLocations[i];
        if (loc.location == currentSceneName)
            pinsHere.Add(loc);
    }
    
    // Try each pin (newest first) until we successfully route
    foreach (var loc in pinsHere)
    {
        string statKey = $"{loc.character} - {currentSceneName} - Stage";
        float rawStage = StatsManager.Get_Numbered_Stat(statKey);
        int stage = Mathf.RoundToInt(rawStage);

        Debug.Log($"[Router] trying pin: char={loc.character} statKey='{statKey}' rawStage={rawStage} stageInt={stage}");

        foreach (StageConversation route in routes)
        {
            Debug.Log(
                $"[Router] cand: char={route.character} stage={route.stage} " +
                $"unlockWeek={route.unlockWeek} conv={(route.conversation ? route.conversation.name : "NULL")}");

            if (route.character != loc.character || route.stage != stage)
                continue;

            if (currentWeek < route.unlockWeek)
            {
                Debug.Log(
                    $"[Router] Matched {loc.character} stage {stage}, " +
                    $"but locked until week {route.unlockWeek}. Current week: {currentWeek}. Skipping.");
                continue;
            }

            if (route.conversation == null)
            {
                Debug.LogWarning($"[Router] Matched {loc.character} stage {stage} but conversation is NULL. Skipping.");
                continue;
            }

            Debug.Log($"Routing {loc.character} at stage {stage} (unlockWeek {route.unlockWeek}) to: {route.conversation.name}");

            // Drums
            if (FMODAudioManager.Instance != null)
            {
                if (loc.character == Character.CHARLI)
                    FMODAudioManager.Instance.SetDrums(0);
                else if (loc.character == Character.LEILANI)
                    FMODAudioManager.Instance.SetDrums(1);
                else if (loc.character == Character.DEEPAK)
                    FMODAudioManager.Instance.SetDrums(2);
                else
                    FMODAudioManager.Instance.SetDrums(3);
            }

            // Clear *this* pin (character+scene)
            var pins = PlayerPrefsExtra.GetList<CharacterLocation>("characterLocations", new List<CharacterLocation>());
            int removed = pins.RemoveAll(p =>
                string.Equals(p.location, currentSceneName, System.StringComparison.Ordinal) &&
                EqualityComparer<Character>.Default.Equals(p.character, loc.character));

            PlayerPrefsExtra.SetList("characterLocations", pins);
            if (removed > 0)
                Debug.Log($"[LocationRouter] Cleared invite for {loc.character} @ {currentSceneName} after routing.");

            TextThreads.ClearInviteRepliesForLocation(loc.character, currentSceneName);

            route.conversation.Start_Conversation();
            go_to_next_node = false;

            Finish_Node();
            return;
        }

        Debug.Log($"[Router] No eligible route found for pin char={loc.character} stage={stage}. Trying next pin (if any).");
    }

    // --- Fallbacks (unchanged) ---
    for (int i = 0; i < fallbackConversations.Count; i++)
    {
        var fallback = fallbackConversations[i];
        if (fallback == null) continue;

        string fallbackKey = $"Seen - {currentSceneName} - Fallback {i}";

        if (!StatsManager.Get_Boolean_Stat(fallbackKey))
        {
            Debug.Log($"Routing unseen fallback {i}: {fallback.name}");
            StatsManager.Set_Boolean_Stat(fallbackKey, true);
            fallback.Start_Conversation();
            go_to_next_node = false;
            Finish_Node();
            return;
        }
    }

    // Repeatable fallback
    if (repeatableFallback != null)
    {
        Debug.Log($"All fallbacks seen. Routing to repeatable fallback: {repeatableFallback.name}");
        repeatableFallback.Start_Conversation();
        go_to_next_node = false;
        Finish_Node();
        return;
    }
    else
    {
        Debug.Log("No matching character/stage found, and no repeatable fallback.");
    }

    go_to_next_node = false;
    Finish_Node();
}

        public override void Button_Pressed() { }
        public override void Finish_Node()
        {
            StopAllCoroutines();
            base.Finish_Node();
        }
    }

}
