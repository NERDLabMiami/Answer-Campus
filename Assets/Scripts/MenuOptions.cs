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
        PlayerPrefs.DeleteAll();
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
