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
        [SerializeField] private float panelOffset = 0.3f; // Distance from shoe to display panel
        [SerializeField] private float panelLerpSpeed = 5f; // Speed of panel movement smoothing
        [SerializeField] private float panelRotationLerpSpeed = 8f; // Speed of panel rotation smoothing
        [SerializeField] private float maxAnchorDistance = 3f; // Maximum distance for anchor usage

        private Dictionary<int, GameObject> activePanels = new();
        private Dictionary<int, ShoeInfoData> shoeInfoCache = new();
        private Dictionary<int, OVRSpatialAnchor> activeAnchors = new();

        private class ShoeInfoData
        {
            public string Name;
            public string Description;
            public string Price;
            public string Size;
            public string DocumentId;
        }

        private void Start()
        {
            if (interactionManager == null)
            {
                interactionManager = FindFirstObjectByType<HandShoeInteractionManager>();
            }

            if (objectTracker == null)
            {
                objectTracker = FindFirstObjectByType<ObjectTracker>();
            }

            interactionManager.OnShoePickedUp += HandleShoePickedUp;
            interactionManager.OnShoeDropped += HandleShoeDropped;
            interactionManager.OnShoePositionUpdated += UpdatePanelPosition;

            // Load any existing anchors when the scene starts
            _ = LoadExistingAnchors();
        }

        private async void OnDestroy()
        {
            if (interactionManager != null)
            {
                interactionManager.OnShoePickedUp -= HandleShoePickedUp;
                interactionManager.OnShoeDropped -= HandleShoeDropped;
                interactionManager.OnShoePositionUpdated -= UpdatePanelPosition;
            }

            // Clean up active anchors
            foreach (var anchor in activeAnchors.Values)
            {
                if (anchor != null)
                {
                    // First erase from persistent storage
                    await anchor.EraseAnchorAsync();
                    // Then destroy the GameObject
                    Destroy(anchor.gameObject);
                }
            }
            activeAnchors.Clear();
        }

        private async Task LoadExistingAnchors()
        {
            await SpatialAnchorManager.Instance.LoadSavedAnchors();
        }

        private async void HandleShoePickedUp(int trackingId, Vector3 position)
        {
            if (activePanels.ContainsKey(trackingId)) return;

            // Get shoe info from cache or fetch from Firebase
            var shoeInfo = await GetShoeInfo(trackingId);
            if (shoeInfo == null) return;

            // Create and setup info panel
            var panel = Instantiate(infoPanelPrefab);
            SetupInfoPanel(panel, shoeInfo);
            activePanels[trackingId] = panel;

            // Position panel relative to shoe
            UpdatePanelPosition(trackingId, position);
        }

        private async void HandleShoeDropped(int trackingId, Vector3 position)
        {
            if (activePanels.TryGetValue(trackingId, out var panel))
            {
                Destroy(panel);
                activePanels.Remove(trackingId);
            }

            // Clean up the anchor if it exists
            if (activeAnchors.TryGetValue(trackingId, out var anchor))
            {
                // Save the anchor's UUID before destroying it
                var anchorUuid = anchor.Uuid.ToString();
                var shoeInfo = shoeInfoCache[trackingId];

                // Update the anchor mapping
                await AnchorMappingManager.Instance.AddAnchorMapping(
                    anchorUuid,
                    shoeInfo.DocumentId
                );

                activeAnchors.Remove(trackingId);
            }
        }

        private void UpdatePanelPosition(int trackingId, Vector3 shoePosition)
        {
            if (!activePanels.TryGetValue(trackingId, out var panel)) return;

            // Calculate desired panel position
            var cameraTransform = Camera.main.transform;
            var directionToCamera = (cameraTransform.position - shoePosition).normalized;
            var targetPosition = shoePosition + directionToCamera * panelOffset;

            // Calculate desired panel rotation (facing the user)
            var targetRotation = Quaternion.LookRotation(-directionToCamera);

            // Smoothly move and rotate panel
            panel.transform.position = Vector3.Lerp(panel.transform.position, targetPosition, Time.deltaTime * panelLerpSpeed);
            panel.transform.rotation = Quaternion.Lerp(panel.transform.rotation, targetRotation, Time.deltaTime * panelRotationLerpSpeed);
        }

        private void SetupInfoPanel(GameObject panel, ShoeInfoData shoeInfo)
        {
            var infoPanel = panel.GetComponent<ShoeInfoPanel>();
            if (infoPanel != null)
            {
                infoPanel.SetShoeInfo(shoeInfo.Name, shoeInfo.Description, shoeInfo.Price, shoeInfo.Size);
            }
        }

        private async Task<ShoeInfoData> GetShoeInfo(int trackingId)
        {
            // Return cached info if available
            if (shoeInfoCache.TryGetValue(trackingId, out var cachedInfo))
            {
                return cachedInfo;
            }

            try
            {
                // First try to find an existing anchor near the tracked object
                var trackedObject = FindTrackedObject(trackingId);
                if (trackedObject.trackingId == 0) return null;

                var nearestAnchor = FindNearestAnchor(trackedObject.center);
                string shoeDocId;

                if (nearestAnchor != null)
                {
                    // Get shoe document ID from existing anchor mapping
                    shoeDocId = AnchorMappingManager.Instance.GetShoeDocumentId(nearestAnchor.Uuid.ToString());
                }
                else
                {
                    // Create a new anchor and get shoe info from object detection
                    var newAnchor = await SpatialAnchorManager.Instance.CreateAnchorAtPoint(trackedObject.center);
                    if (newAnchor == null) return null;

                    // Get the current screen texture and process image for shoe detection
                    var screenTexture = DisplayCaptureManager.Instance.ScreenCaptureTexture;
                    var bbox = objectTracker.GetBoundingBoxForTrackedObject(trackingId);

                    var imageData = ImageUtils.CropAndEncodeImage(
                        screenTexture,
                        (int)bbox.left,
                        (int)(DisplayCaptureManager.Instance.Size.y - bbox.bottom),
                        (int)(bbox.right - bbox.left),
                        (int)(bbox.bottom - bbox.top)
                    );

                    (string detectedName, string detectedShoeDocId) = await FirebaseService.Instance.DetectShoe(imageData);
                    shoeDocId = detectedShoeDocId;

                    if (shoeDocId != "0" && shoeDocId != "-1")
                    {
                        await AnchorMappingManager.Instance.AddAnchorMapping(
                            newAnchor.Uuid.ToString(),
                            shoeDocId
                        );
                    }

                    activeAnchors[trackingId] = newAnchor;
                }

                if (string.IsNullOrEmpty(shoeDocId)) return null;

                // Fetch shoe info from Firestore
                var shoeData = await FirebaseService.Instance.Firestore
                    .Collection("shoes")
                    .Document(shoeDocId)
                    .GetSnapshotAsync();

                if (!shoeData.Exists) return null;

                // Create and cache shoe info
                var shoeInfo = new ShoeInfoData
                {
                    Name = shoeData.GetValue<string>("name"),
                    Description = shoeData.GetValue<string>("description"),
                    Price = shoeData.GetValue<string>("price"),
                    Size = shoeData.GetValue<string>("size"),
                    DocumentId = shoeDocId
                };

                shoeInfoCache[trackingId] = shoeInfo;
                return shoeInfo;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to fetch shoe info: {ex.Message}");
                return null;
            }
        }

        private ObjectTracker.TrackedObject FindTrackedObject(int trackingId)
        {
            var trackedObjects = FindFirstObjectByType<ObjectTracker>().TrackedObjects;
            return trackedObjects.FirstOrDefault(obj => obj.trackingId == trackingId);
        }

        private OVRSpatialAnchor FindNearestAnchor(Vector3 position)
        {
            return SpatialAnchorManager.Instance.SpatialAnchors
                .Where(a => Vector3.Distance(a.transform.position, position) < maxAnchorDistance)
                .OrderBy(a => Vector3.Distance(a.transform.position, position))
                .FirstOrDefault();
        }
    }
}