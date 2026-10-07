using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNEngine;


public class SaveProgress : MonoBehaviour
{
    [System.Serializable]
    public struct CharacterRelationship
    {
        public string character;
        public Relationship relationship;

    }

    public string nextScene;

    [SerializeField]
    public CharacterRelationship[] relationships;


    public void StartNewGame(GameObject newGamePanelOverride)
    {
        
            Debug.Log("Reseting player prefs...");
            Reset();
            Debug.Log("Saving friendship player prefs...");
            Save();
            Debug.Log("Loading Cutscene...");
            GetComponent<MenuOptions>().LoadScene(GetComponent<MenuOptions>().sceneToLoad);
           
    }
    public void SetNextScene()
    {
        PlayerPrefs.SetString("Next Scene", nextScene);
        Debug.Log("Next Scene: " + nextScene);
    }


    public void Reset()
    {
        // VNEngine.SaveManager writes its own files straight to disk (save_slot_N.gd),
        // entirely separate from StatsManager/PlayerPrefs -- neither of the clears above
        // touches it. Without this, HomeCutsceneController.EnsureStatsPopulated() falls
        // back to whatever stale save is still on disk the moment Home.unity loads with no
        // Week stat and no PlayerPrefs checkpoint, silently resuming the previous
        // playthrough's end-of-game state instead of starting Move-In Day/Orientation.
        StatsManager.Clear_All_Stats();
        PlayerPrefs.DeleteAll();
        SaveManager.DeleteAllSaves();
    }

    public void Save()
    {
        //CALLLED WHEN CLICKING ON CHOICE
        Debug.Log("Saving Progress...");
        SetNextScene();
    }
}
