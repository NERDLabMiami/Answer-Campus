using System;
using System.Collections.Generic;

[Serializable]
public class QuickReply
{
    public string label;       // e.g., "Sounds good, see u there!"
    public string iconKey;     // e.g., "thumbs_up"  (map to sprite/emoji in UI)
    public string payload;     // optional: use if you need branching keys, stat deltas, etc.
    public string npcResponse; // NPC's reply after the player picks this option (leave blank for none)
}
[Serializable]
public class TextMessage : System.IEquatable<TextMessage>
{
    public Character from;
    public string body;
    public long unixTime;
    public bool isPlayer;
    public string location;

    public int unlockWeek; // 0 = immediately visible

    public List<QuickReply> quickReplies;
    public TextMessage positiveResponseBranch;
    public TextMessage negativeResponseBranch;

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
