using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;          // Optional, only used if you wire phoneButtonImage
using VNEngine;

public class Home : MonoBehaviour
{
    [Header("Phone UI Root (inactive until opened)")]
    public GameObject phone;

    [Header("Home Objects")]
    public List<HomeObject> homeObjects;

    [Header("Menu Button Indicator")]
    public Animator phoneButtonAnimator;          // Animator on the MENU button, not the Phone GO

    // Optional: if you want the icon to swap too (you already have these sprites)
    public Image phoneButtonImage;                // Image on the MENU button (optional)
    public Sprite phoneNewMessages;               // Optional
    public Sprite phoneNoNewMessages;             // Optional

    private const string TRIG_NOTIFY  = "notification";
    private const string TRIG_DEFAULT = "default";
    private const string STAT_PHONE_HAS_NEW = "PhoneHasNewActivity";


    private void OnEnable()
    {
        RefreshPhoneButtonIndicator();
        RefreshHomeObjects();
    }

    public void RefreshHomeObjects()
    {
        if (homeObjects == null) return;
        foreach (var obj in homeObjects)
        {
            if (obj != null) obj.Refresh();
        }
    }

    public void RefreshPhoneButtonIndicator()
    {
        // Source of truth is the stat set by NodeMessage/NodeEvent/NodeContact (not Phone/PhoneDataService)
        bool hasNew = StatsManager.Get_Boolean_Stat(STAT_PHONE_HAS_NEW);

        if (phoneButtonImage != null)
            phoneButtonImage.sprite = hasNew ? phoneNewMessages : phoneNoNewMessages;

        if (phoneButtonAnimator != null)
        {
            Debug.Log($"[PHONE] Has New: {hasNew}");
            phoneButtonAnimator.SetTrigger(hasNew ? TRIG_NOTIFY : TRIG_DEFAULT);
            
        }
    }

    // Wire the menu button OnClick to this
    public void OpenPhone()
    {
        // Clear the indicator when the player opens the phone UI
        StatsManager.Set_Boolean_Stat(STAT_PHONE_HAS_NEW, false);
        ClearPhoneButtonIndicator();

        if (phone != null)
            phone.SetActive(true);
    }

    public void ClearPhoneButtonIndicator()
    {
        if (phoneButtonImage != null)
            phoneButtonImage.sprite = phoneNoNewMessages;

        if (phoneButtonAnimator != null)
            phoneButtonAnimator.SetTrigger(TRIG_DEFAULT);
    }

    public void Quit()
    {
        FMODAudioManager.Instance.StopMusic();
        SceneManager.LoadScene("Main");
    }
}
