using UnityEngine;
using UnityEngine.SceneManagement;

namespace VNEngine
{
    public class SaveSlotController : MonoBehaviour
    {
        public SaveSlot[] slots = new SaveSlot[3];
        public string newGameScene = "Home";

        void Start()
        {
            RefreshUI();
        }

        void RefreshUI()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;

                int slotIndex = i;
                SaveFile save = SaveManager.GetSaveForSlot(i);

                if (save != null)
                    slots[i].ShowSave(save, () => LoadSlot(slotIndex), () => DeleteSlot(slotIndex));
                else
                    slots[i].ShowEmpty(() => StartNewGame(slotIndex));
            }
        }

        void LoadSlot(int slotIndex)
        {
            SaveManager.current_slot = slotIndex;
            SaveFile save = SaveManager.GetSaveForSlot(slotIndex);
            if (save == null)
            {
                Debug.LogError("[SaveSlotController] LoadSlot: GetSaveForSlot returned null for slot " + slotIndex);
                return;
            }
            Debug.Log("[SaveSlotController] Loading slot " + slotIndex + " scene=" + save.current_scene);
            StartCoroutine(save.Load());
        }

        void StartNewGame(int slotIndex)
        {
            SaveManager.current_slot = slotIndex;
            StatsManager.Clear_All_Stats();
            PlayerPrefs.DeleteAll();
            SceneManager.LoadScene(newGameScene);
        }

        void DeleteSlot(int slotIndex)
        {
            SaveFile save = SaveManager.GetSaveForSlot(slotIndex);
            if (save != null)
                SaveManager.DeleteSave(save);
            RefreshUI();
        }
    }
}
