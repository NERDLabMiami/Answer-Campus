using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;

namespace VNEngine
{
    // Not used in real code. Merely a template to copy and paste from when creating new nodes.
    public class KeyboardEntry : Node
    {
        public GameObject textEntryPrefab;
        private GameObject textEntry;
        private TMP_InputField textInput;
        public string statName;
        // Called initially when the node is run, put most of your logic here
        public override void Run_Node()
        {

            Canvas c = FindAnyObjectByType<Canvas>();
            textEntry = Instantiate(textEntryPrefab, c.transform);
            textInput = textEntry.GetComponentInChildren<TMP_InputField>();
            textInput.onEndEdit.AddListener(delegate { EndInput(textInput.text); });
            textInput.Select();
            textInput.ActivateInputField();
            // if there's no need to  wait for other operations/coroutines, call finish node at the end of this method
//            Finish_Node();
        }

        void EndInput(string input)
        {
            if (input.Length > 0)
            {
              StatsManager.Set_String_Stat(statName, input);
                textEntry.SetActive(false);
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

            base.Finish_Node();
        }
    }
}