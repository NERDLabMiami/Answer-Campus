using UnityEngine;
using UnityEngine.UI;


[System.Serializable]
public struct ComboSprite
{
    public CheerCombo combo;
    public Sprite sprite;
}

public class Cheer : MonoBehaviour
{
    [Header("Scene UI Image")]
    [Tooltip("Drag in your UI Image component here (the one that will display the cheer pose).")]
    [SerializeField] private Image characterImage;

    [Header("Assign one sprite per combo below")]
    [Tooltip("Make sure your assets are imported as 'Sprite (2D and UI)'")]
    [SerializeField] private ComboSprite[] comboSprites;

    /// <summary>
    /// Call this at runtime to pick which combo to show.
    /// </summary>
    public void SetCombo(CheerCombo combo)
    {
        // linear search is fine here—only 7 entries
        for (int i = 0; i < comboSprites.Length; i++)
        {
            if (comboSprites[i].combo == combo)
            {
                characterImage.sprite = comboSprites[i].sprite;
                characterImage.SetNativeSize();
                return;
            }
        }
        Debug.LogWarning($"[Cheer] No sprite assigned for combo {combo}");
    }
    
    
}