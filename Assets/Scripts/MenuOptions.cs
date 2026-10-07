using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VNEngine;

public class MenuOptions : MonoBehaviour
{
    public string sceneToLoad;
    public bool loadSceneOnStart = false;
    private void Start()
    {
        if(loadSceneOnStart)
        {
            if(PlayerPrefs.HasKey("Game in Progress"))
            {

            }
            else
            {
                LoadScene(sceneToLoad);
                print("here");
                this.gameObject.SetActive(false);
                PlayerPrefs.SetInt("Game in Progress", 1);
            }
        }
    }

    public void ResetProgress()
    {
        // PlayerPrefs alone isn't enough: StatsManager is a static in-memory class, so any
        // boolean stat set during a previous playthrough this session (e.g. a "seen this
        // fallback already" flag) survives a PlayerPrefs wipe and leaks into the "new"
        // game -- confirmed: Student Center then skips Orientation straight to its
        // repeatable fallback, since whatever gates Orientation still reads as "seen".
        // VNEngine.SaveManager also writes its own files straight to disk, entirely
        // separate from PlayerPrefs, so those need clearing too for a genuinely fresh start.
        StatsManager.Clear_All_Stats();
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        SaveManager.DeleteAllSaves();
    }

    public void SaveCurrentScene()
    {
        PlayerPrefs.SetString("Next Scene", SceneManager.GetActiveScene().name);
    }
    public void LoadNextScene()
    {
        Debug.Log("loading next scene");

        sceneToLoad = PlayerPrefs.GetString("Next Scene", sceneToLoad);
        LoadScene(sceneToLoad);
    }
    // Abandons the current conversation and returns to Home; stats revert to when the player left.
    public void QuitToHome()
    {
        HomeCutsceneController.RestoreDepartureSnapshot();
        LocationRouter.Go("Home");
    }

    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
}
