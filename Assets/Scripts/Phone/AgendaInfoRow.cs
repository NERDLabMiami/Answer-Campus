using TMPro;
using UnityEngine;

public class AgendaInfoRow : MonoBehaviour
{
    public TMP_Text labelText;
    public TMP_Text valueText;

    public void Bind(string label, string value = null)
    {
        if (labelText) labelText.text = label ?? string.Empty;
        if (valueText)
        {
            valueText.text = value ?? string.Empty;
            valueText.gameObject.SetActive(!string.IsNullOrEmpty(value));
        }
    }
}
