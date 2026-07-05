// FriendsView.cs
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FriendsView : MonoBehaviour
{
    [Header("UI")]
    public Transform listRoot;               // Container for friend buttons
    public GameObject threadButtonPrefab;    // Prefab with ViewTextMessage (name+icon+optional "message" text)
    public TextThreadPanel threadPanel;      // Right-side thread panel to show history & quick replies
    public TextMeshProUGUI emptyLabel;       // Shown when the friends list is empty

    [Header("Data Mapping")]

    public ProfilePicture[] profiles;        // Character -> Sprite mapping (same struct used in TextMessageList)

    public Characters contacts;
    public TextMeshProUGUI headerText;
    void Awake()
    {
        if (threadPanel != null)
        {
            threadPanel.profiles = profiles; // share the same mapping
        }
    }
    // Call this whenever the phone opens Friends tab
    public void Render()
    {
        foreach (Transform c in listRoot) Destroy(c.gameObject);

        var allMsgs = TextThreads.GetAll();
        var threadsByChar = allMsgs
            .GroupBy(m => m.from)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.unixTime).ToList());

        // Union contacts added via NodeContact with characters who have messages.
        var roster = Friend.GetAllFriends()
            .Union(threadsByChar.Keys)
            .Distinct()
            .OrderBy(c => c.ToString())
            .ToList();

        var charLocs = PlayerPrefsExtra.GetList<CharacterLocation>("characterLocations", new List<CharacterLocation>());
        var locByChar = charLocs
            .GroupBy(cl => cl.character)
            .ToDictionary(g => g.Key, g => g.Last().location);

        if (emptyLabel != null)
        {
            emptyLabel.text = "Your friends list is currently empty.";
            emptyLabel.gameObject.SetActive(roster.Count == 0);
        }

        foreach (var who in roster)
        {
            var go = Instantiate(threadButtonPrefab, listRoot);
            var vm = go.GetComponent<ViewTextMessage>();

            bool hasMessages = threadsByChar.ContainsKey(who);

            if (vm.from) vm.from.text = who.ToString();

            if (vm.profile)
            {
                var pic = profiles.FirstOrDefault(p => p.character.Equals(who)).pictureLarge;
                var threadPic = profiles.FirstOrDefault(p => p.character.Equals(who)).pictureSmall;
                vm.threadProfileImage = threadPic;
                if (pic) vm.profile.sprite = pic;
            }

            if (vm.message)
            {
                vm.message.text = locByChar.TryGetValue(who, out var where)
                    && !string.IsNullOrWhiteSpace(where) ? where : "";
            }

            var btn = go.GetComponent<Button>();
            if (btn != null)
            {
                btn.interactable = hasMessages;
                if (hasMessages)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => ShowThread(who));
                }
            }
        }
        ShowList();
    }
    public void ShowList()
    {
        if (listRoot.gameObject) listRoot.gameObject.SetActive(true);
        if (threadPanel)   threadPanel.Hide();
    }

    private void ShowThread(Character who)
    {
        if (listRoot) listRoot.gameObject.SetActive(false);
        if (threadPanel)   threadPanel.Show(who);
        headerText.text = who.ToString();
    }
    public void HideThread() { if (threadPanel) threadPanel.Hide(); }
}
