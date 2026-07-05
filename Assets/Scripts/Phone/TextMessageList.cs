using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System;
using System.Text.RegularExpressions;
using System.Linq;  // For LINQ methods

[System.Serializable]
public struct ProfilePicture
{
    public Character character;
    public Sprite pictureLarge;
    public Sprite pictureSmall;

}
public static class TextThreads
{
    const string Key = "messages";

    public static List<TextMessage> GetAll()
        => PlayerPrefsExtra.GetList<TextMessage>(Key, new List<TextMessage>());

    public static void SaveAll(List<TextMessage> all)
    {
        PlayerPrefsExtra.SetList(Key, all);
        PlayerPrefs.Save();
    }
    public static List<TextMessage> GetThread(Character other)
    {
        int week = UnityEngine.Mathf.RoundToInt(VNEngine.StatsManager.Get_Numbered_Stat("Week"));

        return GetAll()
            .Where(m =>
                ((m.from == other && !m.isPlayer) || (m.isPlayer && m.from == other)) &&
                (m.unlockWeek <= 0 || week >= m.unlockWeek))
            .OrderBy(m => m.unixTime)
            .ToList();
    }
    public static void AddNpcMessage(Character from, string body, string location, int unlockWeek = 0, List<QuickReply> replies = null)
    {
        var all = GetAll();
        var msg = new TextMessage(from, body, location);
        msg.unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        msg.isPlayer = false;
        msg.quickReplies = replies;
        msg.unlockWeek = unlockWeek;

        all.Add(msg);
        SaveAll(all);
    }


    public static void SendPlayerResponse(Character to, QuickReply reply)
    {
        var all = GetAll();

        // Player bubble uses the reply label as the outgoing text.
        var playerMsg = new TextMessage(to, reply.label, location: null);
        playerMsg.unixTime = Now();
        playerMsg.isPlayer = true;
        playerMsg.quickReplies = null;
        all.Add(playerMsg);

        // Clear quick replies on the most recent NPC message for this thread.
        var lastNpcMsg = all.LastOrDefault(m => !m.isPlayer && m.from == to && m.quickReplies != null && m.quickReplies.Count > 0);
        if (lastNpcMsg != null) lastNpcMsg.quickReplies = null;

        // Append NPC response if the reply option has one.
        if (!string.IsNullOrWhiteSpace(reply.npcResponse))
        {
            var npcMsg = new TextMessage(to, reply.npcResponse, location: null);
            npcMsg.unixTime = Now() + 1; // one second later to keep ordering deterministic
            npcMsg.isPlayer = false;
            npcMsg.quickReplies = null;
            all.Add(npcMsg);
        }

        SaveAll(all);
    }
    
    public static void ClearInviteRepliesForLocation(Character character, string location)
    {
        var all = GetAll();
        bool changed = false;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var m = all[i];
            if (!m.isPlayer && m.from == character && m.location == location
                && m.quickReplies != null && m.quickReplies.Count > 0)
            {
                m.quickReplies = null;
                changed = true;
                break;
            }
        }
        if (changed) SaveAll(all);
    }

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
public class TextMessageList : MonoBehaviour
{
    public GameObject listItemTemplate;
    public GameObject messageTemplate;
    public GameObject inbox;
    public TextMeshProUGUI inboxHeader;
    public Image inboxProfile;
    public Button likeMessageButton;
    public Phone phone;

    public ProfilePicture[] profiles;
    private List<TextMessage> messages;
    private Dictionary<Character, List<TextMessage>> groupedMessages;

    // Start is called before the first frame update
    void Start()
    {
        messages = PlayerPrefsExtra.GetList<TextMessage>("messages", new List<TextMessage>());

        // Group messages by character
        groupedMessages = messages
            .GroupBy(m => m.from)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var entry in groupedMessages)
        {
            Character from = entry.Key;
            GameObject go = Instantiate(listItemTemplate, transform);

            // Set the "from" text to display the sender's name
            go.GetComponent<ViewTextMessage>().from.text = from.ToString();

            // Assign profile picture if available
            for (int j = 0; j < profiles.Length; j++)
            {
                if (profiles[j].character == from)
                {
                    go.GetComponent<ViewTextMessage>().profile.sprite = profiles[j].pictureLarge;
                    break;
                }
            }

            // Button to load the message thread
            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(() => LoadThread(from, groupedMessages[from]));
        }
    }

    private void ClearMessages()
    {
        ViewTextMessage[] list = inbox.GetComponentsInChildren<ViewTextMessage>();
        Debug.Log("FOUND " + list.Length + " messages");
        for (int i = 0; i < list.Length; i++)
        {
            Destroy(list[i].gameObject);
        }
    }

    private void LoadThread(Character from, List<TextMessage> messages)
    {
        Debug.Log("LOADING THREAD FOR " + from.ToString());
        phone.ClearNotifications();
        ClearMessages();


        inboxHeader.text = from.ToString();
        inbox.transform.parent.parent.parent.gameObject.SetActive(true);

        for (int i = 0; i < messages.Count; i++)
        {
            // Set profile picture in the inbox
            for (int j = 0; j < profiles.Length; j++)
            {
                if (profiles[j].character == from)
                {
                    inboxProfile.sprite = profiles[j].pictureSmall;
                    break;
                }
            }

            // Create multiple message items (split by sentences)
            string[] sentences = Regex.Split(messages[i].body, @"(?<=[\.!\?])\s+");
            Debug.Log("FOUND " + sentences.Length + " SENTENCES.");
            for (int j = 0; j < sentences.Length; j++)
            {
                GameObject go = Instantiate(messageTemplate, inbox.transform);
                go.GetComponent<ViewTextMessage>().message.text = sentences[j];
            }

            // Correctly assign button action
            string loc = messages[i].location;
            likeMessageButton.onClick.RemoveAllListeners(); // Remove previous listeners
            likeMessageButton.onClick.AddListener(() => GoToLocation(loc));
        }

        // Hide the previous view
        transform.parent.parent.parent.gameObject.SetActive(false);
    }

    private void GoToLocation(string location)
    {
        /* REMOVE MESSAGES FROM CHARACTER */

            // Find the character whose messages are being viewed
            Character from = (Character)Enum.Parse(typeof(Character), inboxHeader.text);

            // Remove the messages from the groupedMessages dictionary
            if (groupedMessages.ContainsKey(from))
            {
                groupedMessages.Remove(from);
                Debug.Log($"Removed all messages from {from}");

                // Save the updated messages to PlayerPrefs
                messages = groupedMessages.Values.SelectMany(m => m).ToList();
                PlayerPrefsExtra.SetList("messages", messages);
                PlayerPrefs.Save();
            }


        Debug.Log("Going to " + location);
        HomeCutsceneController.NavigateOut(location);
    }
}
