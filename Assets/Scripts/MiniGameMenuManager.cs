using UnityEngine;
using UnityEngine.SceneManagement;

public class MiniGameMenuManager : MonoBehaviour
{

    [SerializeField] private GameObject buttonContainer;
    [SerializeField] private GameObject titleImage;
    [SerializeField] private GameObject bgImage;
    [SerializeField] private GameObject returnToMenuButton;
    
    private void Start()
    {
        DontDestroyOnLoad(this);
    }

    public void LoadCheerGame()
    {
        ToggleGameButtons(false);
        SceneManager.LoadScene("Cheer");
        returnToMenuButton.SetActive(true);
    }
    
    public void LoadDanceGame()
    {
        ToggleGameButtons(false);
        SceneManager.LoadScene("Dance");
        returnToMenuButton.SetActive(true);
    }
    
    public void LoadStudyGame()
    {
        ToggleGameButtons(false);
        SceneManager.LoadScene("Study");
        returnToMenuButton.SetActive(true);
    }
    
    public void ReurnToMainMenu()
    {
        SceneManager.LoadScene("Mini Game Menu");
        ToggleGameButtons(true);
        bgImage.SetActive(true);
    }
    
    public void ToggleGameButtons(bool isEnabled)
    {
        buttonContainer.SetActive(isEnabled);
        titleImage.SetActive(isEnabled);
        bgImage.SetActive(isEnabled);
    }

}
