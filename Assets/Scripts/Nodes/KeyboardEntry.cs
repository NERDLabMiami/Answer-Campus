using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;
#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

namespace VNEngine
{
    // Used for the "Player Name" entry in Student Center.unity (Conversation Pieces/KeyboardEntry.prefab).
    public class KeyboardEntry : Node
    {
        public GameObject textEntryPrefab;
        private GameObject textEntry;
        private TMP_InputField textInput;
        public string statName;

#if !DISABLESTEAMWORKS
        private bool steamDeckKeyboardActive = false;
#endif

        // Called initially when the node is run, put most of your logic here
        public override void Run_Node()
        {

            Canvas c = FindAnyObjectByType<Canvas>();
            textEntry = Instantiate(textEntryPrefab, c.transform);
            textInput = textEntry.GetComponentInChildren<TMP_InputField>();
            textInput.onEndEdit.AddListener(delegate { EndInput(textInput.text); });
            textInput.Select();
            textInput.ActivateInputField();

#if !DISABLESTEAMWORKS
            // ActivateInputField() only helps if a physical keyboard is attached. On Steam Deck
            // there isn't one, so nothing prompts the player for text unless we explicitly ask
            // SteamOS to raise its floating virtual keyboard over the field.
            if (SteamManager.Initialized && SteamUtils.IsSteamRunningOnSteamDeck())
            {
                ShowSteamDeckKeyboard();
            }
#endif
            // if there's no need to  wait for other operations/coroutines, call finish node at the end of this method
//            Finish_Node();
        }

#if !DISABLESTEAMWORKS
        private void ShowSteamDeckKeyboard()
        {
            RectTransform rt = textInput.GetComponent<RectTransform>();
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners); // Screen Space - Overlay canvas: world coords == screen pixel coords, origin bottom-left

            int x = Mathf.RoundToInt(corners[0].x);
            int y = Mathf.RoundToInt(Screen.height - corners[2].y); // flip to Steam's top-left-origin convention
            int w = Mathf.RoundToInt(corners[2].x - corners[0].x);
            int h = Mathf.RoundToInt(corners[2].y - corners[0].y);

            steamDeckKeyboardActive = SteamUtils.ShowFloatingGamepadTextInput(
                EFloatingGamepadTextInputMode.k_EFloatingGamepadTextInputModeModeSingleLine, x, y, w, h);
        }

        private void DismissSteamDeckKeyboard()
        {
            if (steamDeckKeyboardActive)
            {
                SteamUtils.DismissFloatingGamepadTextInput();
                steamDeckKeyboardActive = false;
            }
        }
#endif

        void EndInput(string input)
        {
            if (input.Length > 0)
            {
              StatsManager.Set_String_Stat(statName, input);
                textEntry.SetActive(false);
#if !DISABLESTEAMWORKS
                DismissSteamDeckKeyboard();
#endif
                Finish_Node();
            }
            else
            {

                Debug.Log("No input text");
            }
            Debug.Log("Set stat to " + input);
        }

        // What happens when the user clicks on the dialogue text or presses spacebar? Either nothing should happen, or you call Finish_Node to move onto the next node
        public override void Button_Pressed()
        {
            //Finish_Node();
        }


        // Do any necessary cleanup here, like stopping coroutines that could still be running and interfere with future nodes
        public override void Finish_Node()
        {
            StopAllCoroutines();
#if !DISABLESTEAMWORKS
            DismissSteamDeckKeyboard();
#endif

            base.Finish_Node();
        }
    }
}