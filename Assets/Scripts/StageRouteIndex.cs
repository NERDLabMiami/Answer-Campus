using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "StageRouteIndex", menuName = "AnswerVerse/Stage Route Index")]
public class StageRouteIndex : ScriptableObject
{
    [Serializable]
    public class StageRouteMeta
    {
        public Character character;
        public LocationData location;
        [HideInInspector] public int stage; // auto-assigned by OnValidate; do not set manually
        [Min(0)] public int unlockWeek;
    }

    // Keep it serialized, give new assets a default list,
    // but don't nuke existing serialized data.
    [SerializeField]
    private List<StageRouteMeta> routes = new List<StageRouteMeta>();
    public IReadOnlyList<StageRouteMeta> Routes => routes;
    public StageRouteMeta FindRoute(Character character, string sceneName, int stage)
    {
        if (Routes == null) return null;

        foreach (var meta in Routes)
        {
            if (meta == null || meta.location == null) continue;

            // Adjust the property if your LocationData calls it something else
            var locName = meta.location.sceneName;
            if (meta.character == character &&
                meta.stage == stage &&
                locName == sceneName)
            {
                return meta;
            }
        }

        return null;
    }
    public IReadOnlyList<StageRouteMeta> GetRoutesForScene(string sceneName)
    {
        if (Routes == null || string.IsNullOrEmpty(sceneName))
            return Array.Empty<StageRouteMeta>();

        var list = new List<StageRouteMeta>();
        foreach (var meta in Routes)
        {
            if (meta == null || meta.location == null) continue;

            // Adjust this if your LocationData uses a different field name
            var locName = meta.location.sceneName;
            if (string.Equals(locName, sceneName, StringComparison.Ordinal))
            {
                list.Add(meta);
            }
        }
        return list;
    }

    public bool HasRoute(Character character, string sceneName, int stage)
    {
        return FindRoute(character, sceneName, stage) != null;
    }
    public StageRouteMeta GetNextRoute(Character character, string sceneName, int currentStage)
    {
        StageRouteMeta next = null;

        if (Routes == null) return null;

        foreach (var meta in Routes)
        {
            if (meta == null || meta.location == null) continue;

            var locName = meta.location.sceneName; // adjust if your field differs

            // Must match same character & same scene
            if (meta.character != character) continue;
            if (!string.Equals(locName, sceneName, System.StringComparison.Ordinal)) continue;

            // We’re looking only for stages *after* the current stage
            if (meta.stage <= currentStage) continue;

            // Choose the smallest later stage
            if (next == null || meta.stage < next.stage)
                next = meta;
        }

        return next;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (routes == null)
            routes = new List<StageRouteMeta>();

        // Auto-assign stage as the 1-based ordinal within each (character, location) group,
        // sorted by unlockWeek. Designers only need to set character, location, and unlockWeek.
        var groups = new Dictionary<(Character, string), List<StageRouteMeta>>();
        foreach (var meta in routes)
        {
            if (meta == null || meta.location == null) continue;
            var key = (meta.character, meta.location.sceneName ?? meta.location.name ?? "");
            if (!groups.ContainsKey(key)) groups[key] = new List<StageRouteMeta>();
            groups[key].Add(meta);
        }
        foreach (var group in groups.Values)
        {
            group.Sort((a, b) => a.unlockWeek.CompareTo(b.unlockWeek));
            for (int i = 0; i < group.Count; i++)
                group[i].stage = i + 1;
        }
        UnityEditor.EditorUtility.SetDirty(this);
    }
    public void EditorReplaceRoutes(List<StageRouteMeta> newRoutes)
    {
        routes.Clear();
        if (newRoutes != null)
            routes.AddRange(newRoutes);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}