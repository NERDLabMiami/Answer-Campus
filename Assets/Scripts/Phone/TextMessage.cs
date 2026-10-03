using System;
using System.Collections.Generic;

[Serializable]
public class QuickReply
{
    public string emoji;        // button text, e.g. "😎"
    public string responseText; // themed line sent alongside the emoji, e.g. "Sounds good, see u there!"
    public string payload;      // optional: use if you need branching keys, stat deltas, etc.
    public string npcResponse;  // NPC's reply after the player picks this option (leave blank for none)

    public string ComposePlayerMessage() =>
        string.IsNullOrEmpty(emoji) ? responseText
        : string.IsNullOrEmpty(responseText) ? emoji
        : $"{emoji} {responseText}";
}
[Serializable]
public class TextMessage : System.IEquatable<TextMessage>
{
    public Character from;
    public string body;
    public long unixTime;
    public bool isPlayer;
    public string location;

    // false = legacy/default behavior: if `location` is set, accepting a quick reply on this
    // message navigates there via HomeCutsceneController.NavigateOut.
    // true = `location` is used ONLY for NodeMessage/StageRouteIndex unlock-week timing -
    // TextThreadPanel must never treat it as a navigation target; replying stays in the phone.
    public bool locationForTimingOnly;

    [UnityEngine.HideInInspector]
    public int unlockWeek; // 0 = immediately visible; computed by NodeMessage, never hand-authored

    public List<QuickReply> quickReplies;
    [System.NonSerialized] public TextMessage positiveResponseBranch;
    [System.NonSerialized] public TextMessage negativeResponseBranch;

    public TextMessage(Character from, string message, string location)
    {
        this.from = from;
        this.body = message;
        this.location = location;
        this.unlockWeek = 0;
    }

    // Retrieve the next message based on the player's choice (positive or negative)
    public TextMessage GetNextMessage(bool isPositiveResponse)
    {
        return isPositiveResponse ? positiveResponseBranch : negativeResponseBranch;
    }

    // Equality checks for TextMessage
    public bool Equals(TextMessage other)
    {
        return from == other.from && body == other.body && location == other.location;
    }

    public override bool Equals(object obj)
    {
        if (obj is TextMessage otherMessage)
            return Equals(otherMessage);

        return false;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + from.GetHashCode();
            hash = hash * 23 + (body?.GetHashCode() ?? 0);
            hash = hash * 23 + (location?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
