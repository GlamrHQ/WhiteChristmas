using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Anaglyph.ShoeInteraction
{
    public class ShoeInfoPanel : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI descriptionText;
        [SerializeField] private TextMeshProUGUI priceText;
        [SerializeField] private TextMeshProUGUI sizeText;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float fadeInSpeed = 2f;
        [SerializeField] private float fadeOutSpeed = 3f;
        [SerializeField] private Animator panelAnimator;

        private bool isVisible = false;
        private float targetAlpha = 0f;

        private void Awake()
        {
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            if (panelAnimator == null)
                panelAnimator = GetComponent<Animator>();

            // Initialize invisible
            canvasGroup.alpha = 0f;
            isVisible = false;
        }

        private void Update()
        {
            // Smooth fade in/out
            float currentAlpha = canvasGroup.alpha;
            float speed = targetAlpha > currentAlpha ? fadeInSpeed : fadeOutSpeed;
            canvasGroup.alpha = Mathf.Lerp(currentAlpha, targetAlpha, Time.deltaTime * speed);
        }

        public void SetShoeInfo(string name, string description, string price, string size)
        {
            nameText.text = name;
            descriptionText.text = description;
            priceText.text = price;
            sizeText.text = $"Size: {size}";

            Show();
        }

        public void Show()
        {
            isVisible = true;
            targetAlpha = 1f;
            if (panelAnimator != null)
                panelAnimator.SetTrigger("Show");
        }

        public void Hide()
        {
            isVisible = false;
            targetAlpha = 0f;
            if (panelAnimator != null)
                panelAnimator.SetTrigger("Hide");
        }

        public void Toggle()
        {
            if (isVisible)
                Hide();
            else
                Show();
        }
    }
}