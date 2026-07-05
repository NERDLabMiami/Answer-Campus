using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace VNEngine
{
    public class TimedChoiceNode : Node
    {
        public float timer = 30;
        public ConversationManager default_choice;
        private ChoiceNode choiceNode;
        private Coroutine timerCoroutine;
        private Button[] buttons;

        public override void Run_Node()
        {
            Debug.Log("Timed Choice: " + timer);
            VNSceneManager.scene_manager.Show_UI(false);
            choiceNode = GetComponent<ChoiceNode>();
            if (choiceNode == null)
            {
                Debug.LogError("Timed Choices must include a ChoiceNode Component");
                return;
            }
            if (VNSceneManager.scene_manager.GetComponent<UIManager>())
            {
                buttons = VNSceneManager.scene_manager.GetComponent<UIManager>().choice_panel.GetComponentsInChildren<Button>();

            }

            timerCoroutine = StartCoroutine(Timer());
        }

        public void StopTimer()
        {
            if (timerCoroutine != null)
            {
                StopCoroutine(timerCoroutine);
                timerCoroutine = null;
            }
        }

        private IEnumerator Timer()
        {
            StartCoroutine(DesaturateOverTime(timer));
            yield return new WaitForSeconds(timer);
            Finish_Node();
        }

        private IEnumerator DesaturateOverTime(float time)
        {
            float elapsedTime = 0f;
            while (elapsedTime < time)
            {
                elapsedTime += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedTime / time);

                for (int i = 0; i < buttons.Length; i++)
                {
                    Color initialColor = buttons[i].targetGraphic.color;
                    float grayscale = initialColor.r * 0.299f + initialColor.g * 0.587f + initialColor.b * 0.114f;
                    Color desaturatedColor = Color.Lerp(initialColor, new Color(grayscale, grayscale, grayscale, initialColor.a), t);
                    if(buttons[i].GetComponent<Image>())
                    {
                        buttons[i].GetComponent<Image>().color = desaturatedColor;
                    }

                }
                yield return null;
            }

            for (int i = 0; i < buttons.Length; i++)
            {
                Color initialColor = buttons[i].targetGraphic.color;
                // Ensure the final color is fully desaturated
                float finalGrayscale = initialColor.r * 0.299f + initialColor.g * 0.587f + initialColor.b * 0.114f;
                if(buttons[i].GetComponent<Image>())
                {
                    buttons[i].GetComponent<Image>().color = new Color(finalGrayscale, finalGrayscale, finalGrayscale, initialColor.a);
                }
            }
        }

        public override void Finish_Node()
        {
            StopAllCoroutines();
            if (choiceNode != null) choiceNode.Clear_Choices();
            if (default_choice != null)
            {
                VNSceneManager.current_conversation.Finish_Conversation();
                default_choice.Start_Conversation();
            }
            else
            {
                base.Finish_Node();
            }
        }

    }
}
