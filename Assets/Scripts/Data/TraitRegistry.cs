// TraitRegistry.cs
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName="AnswerVerse/Trait Registry")]
public class TraitRegistry : ScriptableObject
{
    public List<string> numberedTraitKeys = new() { "Humor","Charisma","Empathy","Grades" };
    // (Optionally) boolean/string trait buckets as well
    public List<string> booleanTraitKeys = new();
    public List<string> stringTraitKeys = new();

// TraitRegistry.cs
    public static TraitRegistry Load()
    {
        // Preferred: exact name under a Resources folder
        var reg = Resources.Load<TraitRegistry>("TraitRegistry");
        if (reg) return reg;

        // Fallback: first any TraitRegistry found in Resources (any name)
        var all = Resources.LoadAll<TraitRegistry>("");
        if (all != null && all.Length > 0) return all[0];

#if UNITY_EDITOR
        // Editor-only last resort: look anywhere in the project and warn if not under Resources
        var guids = UnityEditor.AssetDatabase.FindAssets("t:TraitRegistry");
        if (guids.Length > 0)
        {
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TraitRegistry>(path);
            if (asset)
            {
                Debug.LogWarning($"TraitRegistry found at '{path}' but not under a Resources folder. "
                                 + "At runtime, ShowChoiceNode will not auto-load it. "
                                 + "Consider moving it to Assets/Resources/.");
                return asset;
            }
        }
#endif

        Debug.LogWarning("TraitRegistry not found. Add one under Assets/Resources/ (name: TraitRegistry).");
        return null;
    }


}