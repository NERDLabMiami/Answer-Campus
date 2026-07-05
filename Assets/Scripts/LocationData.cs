using UnityEngine;

[CreateAssetMenu(fileName = "LocationData", menuName = "AnswerVerse/Location Data")]
public class LocationData : ScriptableObject
{
    public string sceneName;       // must match the .unity file name you load
    public string displayName;     // optional UI label
    public string buildingName;
    public Sprite mapIcon;         // optional for calendar/map preview
    public Sprite phoneIcon;
    public bool morningOnly;       // if true, only interactable during DayPhase 0 (morning)

    // Resolves the friendly label for a scene name via the matching LocationData asset,
    // falling back to the raw scene name when no asset exists or displayName is blank.
    public static string GetDisplayName(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return sceneName;

        foreach (var data in Resources.LoadAll<LocationData>(""))
        {
            if (data != null && data.sceneName == sceneName)
                return string.IsNullOrEmpty(data.displayName) ? sceneName : data.displayName;
        }
        return sceneName;
    }

    // Returns the LocationData asset for a scene, or null if none is registered.
    public static LocationData Find(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return null;
        foreach (var data in Resources.LoadAll<LocationData>(""))
            if (data != null && data.sceneName == sceneName) return data;
        return null;
    }
}