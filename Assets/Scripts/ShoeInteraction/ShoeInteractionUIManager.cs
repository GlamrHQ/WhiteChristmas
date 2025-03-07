using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Anaglyph.Firebase;
using Anaglyph.DisplayCapture.ObjectDetection;
using Anaglyph.DisplayCapture;
using Anaglyph.Utilities;

namespace Anaglyph.ShoeInteraction
{
    public class ShoeInteractionUIManager : MonoBehaviour
    {
        [SerializeField] private HandShoeInteractionManager interactionManager;
        [SerializeField] private ObjectTracker objectTracker;
        [SerializeField] private GameObject infoPanelPrefab;
        [SerializeField] private float panelOffset = 0.3f;
        [SerializeField] private float panelLerpSpeed = 5f;
        [SerializeField] private float panelRotationLerpSpeed = 8f;
        [SerializeField] private float maxAnchorDistance = 3f;

        private Dictionary<int, GameObject> activePanels = new();
        private Dictionary<int, string> trackingIdToShoeId = new();
        private bool isInitialized = false;

        private async void Start()
        {
            if (interactionManager == null)
                interactionManager = FindFirstObjectByType<HandShoeInteractionManager>();

            if (objectTracker == null)
                objectTracker = FindFirstObjectByType<ObjectTracker>();

            // Wait for ShoeDataCache to be initialized
            if (!ShoeDataCache.Instance.IsInitialized)
            {
                ShoeDataCache.Instance.OnCacheInitialized += HandleCacheInitialized;
                await ShoeDataCache.Instance.Initialize();
            }
            else
            {
                HandleCacheInitialized();
            }
        }

        private void HandleCacheInitialized()
        {
            // Subscribe to shoe data updates
            ShoeDataCache.Instance.OnShoeDataUpdated += HandleShoeDataUpdated;

            // Subscribe to interaction events
            interactionManager.OnShoePickedUp += HandleShoePickedUp;
            interactionManager.OnShoeDropped += HandleShoeDropped;
            interactionManager.OnShoePositionUpdated += UpdatePanelPosition;
            interactionManager.OnShoePlacedOnShelf += HandleShoePlacedOnShelf;

            isInitialized = true;
        }

        private void HandleShoeDataUpdated(string shoeId)
        {
            // Update any active panels showing this shoe
            foreach (var kvp in trackingIdToShoeId)
            {
                if (kvp.Value == shoeId && activePanels.TryGetValue(kvp.Key, out var panel))
                {
                    var infoPanel = panel.GetComponent<ShoeInfoPanel>();
                    infoPanel.SetShoeInfo(shoeId);
                }
            }
        }

        private void OnDestroy()
        {
            if (ShoeDataCache.Instance != null)
            {
                ShoeDataCache.Instance.OnCacheInitialized -= HandleCacheInitialized;
                ShoeDataCache.Instance.OnShoeDataUpdated -= HandleShoeDataUpdated;
            }

            if (interactionManager != null)
            {
                interactionManager.OnShoePickedUp -= HandleShoePickedUp;
                interactionManager.OnShoeDropped -= HandleShoeDropped;
                interactionManager.OnShoePositionUpdated -= UpdatePanelPosition;
                interactionManager.OnShoePlacedOnShelf -= HandleShoePlacedOnShelf;
            }

            // Clean up active panels
            foreach (var panel in activePanels.Values)
            {
                if (panel != null)
                {
                    Destroy(panel);
                }
            }
            activePanels.Clear();
            trackingIdToShoeId.Clear();
        }

        private async void HandleShoePickedUp(int trackingId, Vector3 position)
        {
            if (!isInitialized || activePanels.ContainsKey(trackingId)) return;

            string shoeId = await GetShoeId(trackingId, position);
            if (string.IsNullOrEmpty(shoeId)) return;

            // Verify the shoe exists in cache
            var shoeData = ShoeDataCache.Instance.GetShoeData(shoeId);
            if (shoeData == null)
            {
                Debug.LogWarning($"Shoe data not found in cache for ID: {shoeId}");
                return;
            }

            // Create and setup info panel
            var panel = Instantiate(infoPanelPrefab);
            var infoPanel = panel.GetComponent<ShoeInfoPanel>();
            infoPanel.SetShoeInfo(shoeId);

            activePanels[trackingId] = panel;
            trackingIdToShoeId[trackingId] = shoeId;

            // Position panel relative to shoe
            UpdatePanelPosition(trackingId, position);
        }

        private async Task<string> GetShoeId(int trackingId, Vector3 position)
        {
            // First check if we already know this shoe's ID
            if (trackingIdToShoeId.TryGetValue(trackingId, out string cachedShoeId))
            {
                return cachedShoeId;
            }

            // Try to find an existing anchor nearby
            var nearestAnchor = FindNearestAnchor(position);
            if (nearestAnchor != null)
            {
                string shoeId = AnchorMappingManager.Instance.GetShoeDocumentId(nearestAnchor.Uuid.ToString());
                if (!string.IsNullOrEmpty(shoeId))
                {
                    return shoeId;
                }
            }

            // If no existing anchor found, detect the shoe
            try
            {
                var screenTexture = DisplayCaptureManager.Instance.ScreenCaptureTexture;
                var bbox = objectTracker.GetBoundingBoxForTrackedObject(trackingId);

                var imageData = ImageUtils.CropAndEncodeImage(
                    screenTexture,
                    (int)bbox.left,
                    (int)(DisplayCaptureManager.Instance.Size.y - bbox.bottom),
                    (int)(bbox.right - bbox.left),
                    (int)(bbox.bottom - bbox.top)
                );

                var (_, detectedShoeId) = await FirebaseService.Instance.DetectShoe(imageData);

                if (detectedShoeId != "0" && detectedShoeId != "-1")
                {
                    // Create new anchor and map it
                    var newAnchor = await SpatialAnchorManager.Instance.CreateAnchorAtPoint(position);
                    if (newAnchor != null)
                    {
                        await AnchorMappingManager.Instance.AddAnchorMapping(
                            newAnchor.Uuid.ToString(),
                            detectedShoeId
                        );
                    }
                    return detectedShoeId;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to detect shoe: {ex.Message}");
            }

            return null;
        }

        private void HandleShoeDropped(int trackingId, Vector3 position)
        {
            if (activePanels.TryGetValue(trackingId, out var panel))
            {
                var infoPanel = panel.GetComponent<ShoeInfoPanel>();
                infoPanel.Hide(); // Animate out

                // Destroy after animation
                StartCoroutine(DestroyPanelAfterDelay(panel, trackingId, 0.5f));
            }
        }

        private System.Collections.IEnumerator DestroyPanelAfterDelay(GameObject panel, int trackingId, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (panel != null)
            {
                Destroy(panel);
            }
            activePanels.Remove(trackingId);
            trackingIdToShoeId.Remove(trackingId);
        }

        private void UpdatePanelPosition(int trackingId, Vector3 shoePosition)
        {
            if (!activePanels.TryGetValue(trackingId, out var panel)) return;

            var cameraTransform = Camera.main.transform;
            var directionToCamera = (cameraTransform.position - shoePosition).normalized;
            var targetPosition = shoePosition + directionToCamera * panelOffset;
            var targetRotation = Quaternion.LookRotation(-directionToCamera);

            panel.transform.position = Vector3.Lerp(panel.transform.position, targetPosition, Time.deltaTime * panelLerpSpeed);
            panel.transform.rotation = Quaternion.Lerp(panel.transform.rotation, targetRotation, Time.deltaTime * panelRotationLerpSpeed);
        }

        private OVRSpatialAnchor FindNearestAnchor(Vector3 position)
        {
            return SpatialAnchorManager.Instance.SpatialAnchors
                .Where(a => Vector3.Distance(a.transform.position, position) < maxAnchorDistance)
                .OrderBy(a => Vector3.Distance(a.transform.position, position))
                .FirstOrDefault();
        }

        private async void HandleShoePlacedOnShelf(int trackingId, Vector3 position)
        {
            if (!trackingIdToShoeId.TryGetValue(trackingId, out var shoeId)) return;

            var nearestAnchor = FindNearestAnchor(position);
            if (nearestAnchor == null)
            {
                var newAnchor = await SpatialAnchorManager.Instance.CreateAnchorAtPoint(position);
                if (newAnchor != null)
                {
                    await AnchorMappingManager.Instance.AddAnchorMapping(
                        newAnchor.Uuid.ToString(),
                        shoeId
                    );

                    var saveResult = await newAnchor.SaveAnchorAsync();
                    if (!saveResult.Success)
                    {
                        Debug.LogError($"Failed to save shelf anchor. Status: {saveResult.Status}");
                    }
                }
            }
        }
    }
}