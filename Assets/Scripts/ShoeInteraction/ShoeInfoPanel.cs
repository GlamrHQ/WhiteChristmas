using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Anaglyph.Firebase;

namespace Anaglyph.ShoeInteraction
{
    public class ShoeInfoPanel : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI descriptionText;
        [SerializeField] private TextMeshProUGUI priceText;
        [SerializeField] private TextMeshProUGUI sizeText;
        [SerializeField] private TextMeshProUGUI hypeScoreText;
        [SerializeField] private TextMeshProUGUI resaleValueText;
        [SerializeField] private Image mainImage;
        [SerializeField] private ToggleGroup colorToggles;
        [SerializeField] private ToggleGroup sizeToggles;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float fadeInSpeed = 2f;
        [SerializeField] private float fadeOutSpeed = 3f;
        [SerializeField] private Animator panelAnimator;

        private string currentShoeId;
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

        public void SetShoeInfo(string shoeId)
        {
            currentShoeId = shoeId;
            var shoeData = ShoeDataCache.Instance.GetShoeData(shoeId);
            if (shoeData == null) return;

            nameText.text = shoeData.Name;
            descriptionText.text = shoeData.Story;
            priceText.text = $"${shoeData.BasePrice:F2}";
            hypeScoreText.text = $"Hype Score: {shoeData.HypeScore:F1}";
            resaleValueText.text = $"Resale Value: +{shoeData.ResaleValueIncrease}%";

            // Update color toggles
            UpdateColorToggles(shoeData);

            // Update size toggles
            UpdateSizeToggles(shoeData);

            // Load main image
            if (!string.IsNullOrEmpty(shoeData.MainImageUrl))
            {
                // Implement image loading here using your preferred method
                // For example, using UnityWebRequestTexture
            }

            Show();
        }

        private void UpdateColorToggles(ShoeDataCache.ShoeData shoeData)
        {
            // Clear existing toggles
            foreach (Transform child in colorToggles.transform)
            {
                Destroy(child.gameObject);
            }

            // Create new toggles for each color
            foreach (var color in shoeData.AvailableColors)
            {
                var toggle = CreateToggle(colorToggles.transform, color);
                toggle.group = colorToggles;
                toggle.isOn = color == shoeData.DefaultColor;
                toggle.onValueChanged.AddListener((isOn) =>
                {
                    if (isOn) OnColorSelected(color);
                });
            }
        }

        private void UpdateSizeToggles(ShoeDataCache.ShoeData shoeData)
        {
            // Clear existing toggles
            foreach (Transform child in sizeToggles.transform)
            {
                Destroy(child.gameObject);
            }

            // Create new toggles for each size
            foreach (var size in shoeData.AvailableSizes)
            {
                var toggle = CreateToggle(sizeToggles.transform, size.ToString());
                toggle.group = sizeToggles;
                toggle.onValueChanged.AddListener((isOn) =>
                {
                    if (isOn) OnSizeSelected(size);
                });
            }
        }

        private Toggle CreateToggle(Transform parent, string label)
        {
            var toggleObj = new GameObject(label);
            toggleObj.transform.SetParent(parent, false);

            var toggle = toggleObj.AddComponent<Toggle>();
            var text = new GameObject("Label").AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(toggle.transform, false);
            text.text = label;

            return toggle;
        }

        private void OnColorSelected(string color)
        {
            // var variants = ShoeDataCache.Instance.GetShoeVariants(currentShoeId);
            // var selectedVariant = variants.FirstOrDefault(v => v.Color == color);
            // if (selectedVariant != null)
            // {
            //     priceText.text = $"${selectedVariant.Price:F2}";
            //     // Update 3D model if needed
            //     // UpdateShoeModel(selectedVariant.Model3dUrl);
            // }
        }

        private void OnSizeSelected(float size)
        {
            sizeText.text = $"Size: {size}";
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