using UnityEngine;
using UnityEngine.SceneManagement;

public class MiniGameMenuManager : MonoBehaviour
{
    public void LoadCheerGame()
    {
        SceneManager.LoadScene("Cheer");
    }
    
    public void LoadDanceGame()
    {
        SceneManager.LoadScene("Dance");
    }
    
    public void LoadStudyGame()
    {
        SceneManager.LoadScene("Study");
    }
    
    public void ReurnToMainMenu()
    {
        SceneManager.LoadScene("Mini Game Menu");
    }
    
}
