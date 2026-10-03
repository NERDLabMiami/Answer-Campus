using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

namespace VNEngine
{
    // Shows an image (with an optional caption) full-screen over the conversation, dimmed behind it.
    // Waits for the player to click anywhere (on the image or outside it) or press the submit key
    // before continuing - replaces the old Closer Look scene-prefab + Object Close Up + Wait Node combo.
    public class CloserLookNode : Node
    {
        public Sprite image;
        [TextArea]
        public string caption_text;   // Optional. Left blank, no caption is shown
        public TMP_FontAsset font;
        public bool fade_in = true;
        public float fade_in_time = 0.3f;
        public bool hide_dialogue_ui = true;

        private static readonly Color backdrop_color = new Color(0.13207549f, 0.13207549f, 0.13207549f, 0.9019608f);
        private const float max_image_fraction = 0.7f;   // Image is scaled to fit within this much of the canvas, preserving aspect

        private GameObject overlay;
        private CanvasGroup overlay_canvas_group;

        public override void Run_Node()
        {
            if (image == null)
            {
                Debug.LogError("CloserLookNode has no image assigned.", this.gameObject);
                Finish_Node();
                return;
            }

            if (hide_dialogue_ui)
                VNSceneManager.scene_manager.Show_UI(false);

            Build_Overlay();

            if (fade_in)
            {
                overlay_canvas_group.alpha = 0;
                StartCoroutine(Fade_In(fade_in_time));
            }
        }


        private void Build_Overlay()
        {
            RectTransform canvas_rect = UIManager.ui_manager.canvas.GetComponent<RectTransform>();

            overlay = new GameObject("Closer Look Overlay");
            RectTransform overlay_rect = overlay.AddComponent<RectTransform>();
            overlay_rect.SetParent(canvas_rect, false);
            overlay_rect.anchorMin = Vector2.zero;
            overlay_rect.anchorMax = Vector2.one;
            overlay_rect.offsetMin = Vector2.zero;
            overlay_rect.offsetMax = Vector2.zero;
            overlay.transform.SetAsLastSibling();

            overlay_canvas_group = overlay.AddComponent<CanvasGroup>();

            Image backdrop = overlay.AddComponent<Image>();
            backdrop.color = backdrop_color;

            Button dismiss_button = overlay.AddComponent<Button>();
            dismiss_button.transition = Selectable.Transition.None;
            dismiss_button.onClick.AddListener(() => VNSceneManager.scene_manager.Button_Pressed());

            // The displayed image. raycastTarget is off so clicks fall through to the backdrop's Button,
            // giving "click on the image or click outside it" the same dismiss behaviour with one listener.
            GameObject image_go = new GameObject("Image");
            RectTransform image_rect = image_go.AddComponent<RectTransform>();
            image_rect.SetParent(overlay_rect, false);
            image_rect.anchorMin = new Vector2(0.5f, 0.5f);
            image_rect.anchorMax = new Vector2(0.5f, 0.5f);
            image_rect.pivot = new Vector2(0.5f, 0.5f);

            Image image_component = image_go.AddComponent<Image>();
            image_component.sprite = image;
            image_component.preserveAspect = true;
            image_component.raycastTarget = false;

            Vector2 native_size = new Vector2(image.rect.width, image.rect.height);
            Vector2 max_size = new Vector2(UIManager.ui_manager.canvas_width, UIManager.ui_manager.canvas_height) * max_image_fraction;
            float scale = Mathf.Min(1f, Mathf.Min(max_size.x / native_size.x, max_size.y / native_size.y));
            image_rect.sizeDelta = native_size * scale;

            if (!string.IsNullOrEmpty(caption_text))
            {
                GameObject caption_go = new GameObject("Caption");
                RectTransform caption_rect = caption_go.AddComponent<RectTransform>();
                caption_rect.SetParent(overlay_rect, false);
                caption_rect.anchorMin = new Vector2(0.1f, 0.08f);
                caption_rect.anchorMax = new Vector2(0.9f, 0.2f);
                caption_rect.offsetMin = Vector2.zero;
                caption_rect.offsetMax = Vector2.zero;

                TextMeshProUGUI caption = caption_go.AddComponent<TextMeshProUGUI>(); 
                caption.text = caption_text;
                caption.font = font;
                caption.alignment = TextAlignmentOptions.Center;
                caption.fontSize = 42;
                caption.color = Color.white;
                caption.raycastTarget = false;
            }
        }


        IEnumerator Fade_In(float over_time)
        {
            float value = 0;
            while (value < over_time)
            {
                value += Time.deltaTime;
                overlay_canvas_group.alpha = Mathf.Lerp(0, 1, value / over_time);
                yield return null;
            }
            overlay_canvas_group.alpha = 1;
        }


        // User clicked (the overlay's own Button forwards here) or pressed the submit key. Dismiss and continue.
        public override void Button_Pressed()
        {
            if (overlay == null)
                return;

            Destroy(overlay);
            overlay = null;

            if (hide_dialogue_ui)
                VNSceneManager.scene_manager.Show_UI(true);

            Finish_Node();
        }


        public override void Finish_Node()
        {
            StopAllCoroutines();

            base.Finish_Node();
        }
    }
}
