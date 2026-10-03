using System;
using UnityEngine;

namespace VNEngine
{
    // NOTE: TraitRequirement, Trait, NumberCompare currently live in GateTraitsNode.cs.
    // Keep them there for now; this file focuses on Events + Game requirements shared by nodes.

    public enum EventCheckType
    {
        Completed,
        NotCompleted
    }

    [Serializable]
    public class EventRequirement
    {
        [Tooltip("Custom event id or name. Prefer id (e.g., Custom_1_SyllyParty).")]
        public string key;

        public EventCheckType check = EventCheckType.Completed;
    }
    
    
    [Serializable]
    public class AffinityRequirement
    {
        public Character character;
        public NumberCompare compare;
        public float value;
    }

    [Serializable]
    public class AffinityDelta
    {
        public Character character;
        public float amount;
    }
}