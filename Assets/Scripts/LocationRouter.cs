using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LocationRouter
{
    /// <summary>
    /// Route to a scene. Clears character "invites" (pins) for that location only.
    /// Does NOT touch message history (threads persist).
    /// </summary>
    public static void Go(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[LocationRouter] Go called with empty sceneName");
            return;
        }

        Debug.Log($"[LocationRouter] Go -> '{sceneName}' (active='{SceneManager.GetActiveScene().name}')");

        if (FMODAudioManager.Instance != null)
        {
            FMODAudioManager.Instance.StopAllAudio(1f);
        }

        SceneManager.LoadScene(sceneName);
    }

    /// Optional overload: clear only a specific character’s invite at a location.
    public static void Go(string sceneName, Character character)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[LocationRouter] Go(char) called with empty sceneName");
            return;
        }

        // Record some context if you want, but DO NOT clear pins here.
        PlayerPrefs.SetString("LastRouteScene", sceneName);
        PlayerPrefs.SetString("LastRouteCharacter", character.ToString());

        SceneManager.LoadScene(sceneName);
    }
}
