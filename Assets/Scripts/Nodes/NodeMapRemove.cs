using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;

namespace VNEngine
{
    // Not used in real code. Merely a template to copy and paste from when creating new nodes.
    public class NodeMapRemove : Node
    {
        public Character character;
        public string locationScene;

        public override void Run_Node()
        {
            var pins = PlayerPrefsExtra.GetList<CharacterLocation>("characterLocations", new List<CharacterLocation>());
            int removed = pins.RemoveAll(p =>
                (character == Character.NONE || EqualityComparer<Character>.Default.Equals(p.character, character)) &&
                (string.IsNullOrWhiteSpace(locationScene) || string.Equals(p.location, locationScene, System.StringComparison.Ordinal)));

            PlayerPrefsExtra.SetList("characterLocations", pins);
            PlayerPrefs.Save();

            if (removed > 0) Debug.Log($"[NodeMapRemove] Cleared {removed} pin(s).");
            Finish_Node();
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