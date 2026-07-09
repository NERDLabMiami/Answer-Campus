using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace VNEngine
{
    public class SaveSlot : MonoBehaviour
    {
        public GameObject emptyPanel;
        public GameObject filledPanel;
        public TextMeshProUGUI infoText;
        public Button loadButton;
        public Button deleteButton;
        public Button newGameButton;

        public void ShowEmpty(Action onNewGame)
        {
            emptyPanel?.SetActive(true);
            filledPanel?.SetActive(false);

            if (newGameButton != null)
            {
                newGameButton.onClick.RemoveAllListeners();
                newGameButton.onClick.AddListener(() => onNewGame());
            }
        }

        public void ShowSave(SaveFile save, Action onLoad, Action onDelete)
        {
            emptyPanel?.SetActive(false);
            filledPanel?.SetActive(true);

            if (infoText != null)
                infoText.text = BuildInfoText(save);

            if (loadButton != null)
            {
                loadButton.onClick.RemoveAllListeners();
                loadButton.onClick.AddListener(() => onLoad());
            }

            if (deleteButton != null)
            {
                deleteButton.onClick.RemoveAllListeners();
                deleteButton.onClick.AddListener(() => onDelete());
            }
        }

        string BuildInfoText(SaveFile save)
        {
            string playerName = "Unknown";
            if (save.saved_string_stats != null && save.saved_string_stats.ContainsKey("Player Name"))
                playerName = save.saved_string_stats["Player Name"];

            string weekLabel = "Orientation";
            if (save.saved_numbered_stats != null && save.saved_numbered_stats.ContainsKey("Week"))
            {
                int week = (int)save.saved_numbered_stats["Week"];
                weekLabel = week == 0 ? "Orientation" : "Week " + week;
            }

            return playerName + " — " + weekLabel + "\n" + save.time_saved.ToShortDateString();
        }
    }
}
