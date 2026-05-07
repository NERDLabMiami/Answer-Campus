using UnityEngine;
using System.Collections;
using UnityEngine.UI;

namespace VNEngine
{
    // Not used in real code. Merely a template to copy and paste from when creating new nodes.
    public class NodeAchievement : Node
    {
        public string achievement;
        private bool achievementCompleted;
        // Called initially when the node is run, put most of your logic here
        public override void Run_Node()
        {
            string achievementKey = "Achievement_" + achievement;
            if (!StatsManager.Get_Boolean_Stat(achievementKey))
            {
                StatsManager.Set_Boolean_Stat(achievementKey, true);
                Debug.Log("Achievement Complete: " + achievementKey);
            }
            else
            {
                Debug.LogWarning("Achievement " + achievementKey + " already completed");
            }

            
            /*            
            Steamworks.SteamUserStats.GetAchievement(achievement, out achievementCompleted);

            if(achievementCompleted == false)
            {
                Steamworks.SteamUserStats.SetAchievement(achievement);
            }
*/
            // if there's no need to  wait for other operations/coroutines, call finish node at the end of this method
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