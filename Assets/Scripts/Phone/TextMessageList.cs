using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System;
using System.Text.RegularExpressions;
using System.Linq;  // For LINQ methods
using VNEngine;

[System.Serializable]
public struct ProfilePicture
{
    public Character character;
    public Sprite pictureLarge;
    public Sprite pictureSmall;

}

[System.Serializable]
public struct CharacterReadMarker
{
    public Character character;
    public long lastReadUnixTime;
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
        // A message can now be sent by a NodeMessage running on a different
        // character's conversation (e.g. a game-event text from Breanna fired
        // while talking to Leilani), so the sender isn't guaranteed to be someone
        // the player has actually met yet. Don't surface a thread for a character
        // who isn't a contact - once they're added via NodeContact, the already-
        // stored messages become visible on the next read, no re-delivery needed.
        if (!Friend.IsFriend(other)) return new List<TextMessage>();

        // Show all messages addressed to/from this character once they've been
        // received. unlockWeek gates the quick-reply buttons (in TextThreadPanel),
        // not the message itself — the player should always be able to read a
        // message that was delivered to them.
        return GetAll()
            .Where(m => (m.from == other && !m.isPlayer) || (m.isPlayer && m.from == other))
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


    public static TextMessage SendPlayerResponse(Character to, TextMessage repliedTo, QuickReply reply)
    {
        var all = GetAll();

        // Player bubble uses the emoji + themed response text as the outgoing message.
        var playerMsg = new TextMessage(to, reply.ComposePlayerMessage(), location: null);
        // Anchored to the message being replied to, not wall-clock time -- guarantees this
        // reply (and, via AppendNpcReply's own anchor, its NPC follow-up) always sorts
        // immediately after repliedTo regardless of how much real time passes before the
        // player gets around to answering, or what other independent message gets delivered
        // elsewhere in the meantime. Stamping with the real current time here let an
        // unrelated later-delivered message sort BETWEEN repliedTo and this reply whenever it
        // was stamped (via its own real-time NodeMessage delivery) before the player actually
        // tapped this reply -- scrambling the thread order (see
        // TextThreadPanel.ReplyWithTypingDelay).
        playerMsg.unixTime = repliedTo.unixTime + 1;
        playerMsg.isPlayer = true;
        playerMsg.quickReplies = null;
        all.Add(playerMsg);

        // Clear quick replies on the EXACT NPC message the player just answered -- not
        // whichever NPC message happens to be last in the thread, which broke once two
        // independent NodeMessage beats could both have unanswered quick replies open at
        // once (see TextThreadPanel.RenderQuickReplies). `all` here is a fresh deserialize
        // from PlayerPrefs, so `repliedTo` (read earlier via GetThread) won't be
        // reference-equal to anything in it -- match by value instead (sender + timestamp
        // + body round-trip through PlayerPrefs reliably, unlike object identity).
        var stored = all.FirstOrDefault(m =>
            !m.isPlayer && m.from == to && m.unixTime == repliedTo.unixTime && m.body == repliedTo.body);
        if (stored != null) stored.quickReplies = null;

        SaveAll(all);
        return playerMsg;
    }

    // Appends an NPC follow-up message on its own, so callers can delay it
    // (e.g. a "typing..." pause) instead of writing it atomically with the
    // player's message. Stamped relative to anchorUnixTime (the reply it belongs to),
    // not wall-clock time -- this method only runs after a real typing-indicator delay,
    // so stamping with the real current time let an unrelated message delivered elsewhere
    // during that delay get an earlier timestamp than a follow-up that was logically
    // triggered first, sorting it out of order (see TextThreadPanel.ReplyWithTypingDelay).
    public static void AppendNpcReply(Character to, string body, long anchorUnixTime)
    {
        if (string.IsNullOrWhiteSpace(body)) return;

        var all = GetAll();
        var npcMsg = new TextMessage(to, body, location: null);
        npcMsg.unixTime = anchorUnixTime + 1;
        npcMsg.isPlayer = false;
        npcMsg.quickReplies = null;
        all.Add(npcMsg);
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

    const string ReadKey = "lastReadMessages";

    // Unread = unlocked NPC messages newer than this character's last-read marker.
    // If no marker exists yet, nothing has been read yet - everything in the thread
    // counts as unread until MarkRead() is actually called for this character.
    public static int GetUnreadCount(Character who)
    {
        var thread = GetThread(who);
        if (thread.Count == 0) return 0;

        var markers = PlayerPrefsExtra.GetList<CharacterReadMarker>(ReadKey, new List<CharacterReadMarker>());
        int idx = markers.FindIndex(m => m.character == who);
        long lastRead = idx >= 0 ? markers[idx].lastReadUnixTime : 0;

        int week = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));
        return thread.Count(m => !m.isPlayer && m.unixTime > lastRead && (m.unlockWeek <= 0 || week >= m.unlockWeek));
    }

    // visibleThread lets a caller that only rendered a truncated view of the thread (e.g.
    // TextThreadPanel holding back a newer message behind an older unanswered one) mark read
    // only up to what the player actually saw, instead of a message getting silently marked
    // read before it's ever shown - which would suppress its notification once it finally
    // renders. Omit it to mark the whole thread read (e.g. a simple "opened this thread" signal).
    public static void MarkRead(Character who, List<TextMessage> visibleThread = null)
    {
        var thread = visibleThread ?? GetThread(who);
        long upto = thread.Count > 0 ? thread[thread.Count - 1].unixTime : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var markers = PlayerPrefsExtra.GetList<CharacterReadMarker>(ReadKey, new List<CharacterReadMarker>());
        int idx = markers.FindIndex(m => m.character == who);
        if (idx >= 0) markers[idx] = new CharacterReadMarker { character = who, lastReadUnixTime = upto };
        else markers.Add(new CharacterReadMarker { character = who, lastReadUnixTime = upto });
        PlayerPrefsExtra.SetList(ReadKey, markers);
        PlayerPrefs.Save();
    }
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
