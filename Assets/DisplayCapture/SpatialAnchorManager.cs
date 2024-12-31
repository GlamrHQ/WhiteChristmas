using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Anaglyph.DisplayCapture.ObjectDetection;
using UnityEngine;

namespace Anaglyph.DisplayCapture
{
    public class SpatialAnchorManager : MonoBehaviour
    {
        public static SpatialAnchorManager Instance { get; private set; }

        [SerializeField] private GameObject anchorPrefab; // Assign a simple sphere prefab in the Inspector
        [SerializeField] private bool enableDebugVisualization = true;

        private const string AnchorUuidsKey = "SpatialAnchorUuids";
        private const int MaxBatchSize = 32;
        private List<OVRSpatialAnchor> _spatialAnchors = new();

        public List<OVRSpatialAnchor> SpatialAnchors => _spatialAnchors;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public async Task<OVRSpatialAnchor> CreateAnchorAtPoint(Vector3 point)
        {
            GameObject anchorObject = new GameObject("SpatialAnchor");
            anchorObject.transform.position = point;

            OVRSpatialAnchor spatialAnchor = anchorObject.AddComponent<OVRSpatialAnchor>();
            _spatialAnchors.Add(spatialAnchor);

            // Wait for the anchor to be created and localized
            if (!await spatialAnchor.WhenLocalizedAsync())
            {
                Debug.LogError($"Unable to create anchor.");
                _spatialAnchors.Remove(spatialAnchor);
                Destroy(anchorObject);
                return null;
            }

            if (enableDebugVisualization)
            {
                CreateDebugVisualization(anchorObject.transform);
            }

            SaveAnchor(spatialAnchor);
            return spatialAnchor;
        }

        private void CreateDebugVisualization(Transform parent)
        {
            GameObject visual = Instantiate(anchorPrefab, parent);
            visual.name = "AnchorVisualization";
        }

        private async void SaveAnchor(OVRSpatialAnchor anchor)
        {
            Debug.Log($"Attempting to save anchor with UUID: {anchor.Uuid}");
            // Save the anchor
            var saveResult = await anchor.SaveAnchorAsync();
            if (saveResult.Success)
            {
                // Remember UUID so you can load the anchor later
                AddAnchorUuid(anchor.Uuid);
                Debug.Log($"Successfully saved anchor with UUID: {anchor.Uuid}");
            }
            else
            {
                Debug.LogError($"Failed to save anchor. Status: {saveResult.Status}");
            }
        }

        private IEnumerable<HashSet<Guid>> BatchUuids(HashSet<Guid> uuids)
        {
            return uuids.Select((uuid, index) => new { uuid, index })
                .GroupBy(x => x.index / MaxBatchSize)
                .Select(g => new HashSet<Guid>(g.Select(x => x.uuid)));
        }

        public async Task LoadSavedAnchors()
        {
            HashSet<Guid> uuids = GetSavedAnchorUuids();
            if (uuids.Count == 0)
            {
                Debug.Log("No saved anchors to load.");
                return;
            }

            Debug.Log($"Attempting to load {uuids.Count} saved anchors...");
            int totalSuccessfullyBound = 0;

            foreach (var uuidBatch in BatchUuids(uuids))
            {
                var unboundAnchors = new List<OVRSpatialAnchor.UnboundAnchor>();
                var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(uuidBatch, unboundAnchors);

                if (!result.Success)
                {
                    Debug.LogError($"Loading batch of Unbound Anchors failed with status: {result.Status}");
                    continue;
                }

                Debug.Log($"Successfully loaded {unboundAnchors.Count} unbound anchors, attempting to localize...");

                // Try to localize each unbound anchor
                foreach (var unboundAnchor in unboundAnchors)
                {
                    try
                    {
                        if (!unboundAnchor.Localized && !unboundAnchor.Localizing)
                        {
                            Debug.Log($"Attempting to localize anchor {unboundAnchor.Uuid}...");
                            await unboundAnchor.LocalizeAsync();
                            if (!unboundAnchor.Localized)
                            {
                                Debug.LogWarning($"Failed to localize anchor {unboundAnchor.Uuid}");
                                continue;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error localizing anchor {unboundAnchor.Uuid}: {e.Message}");
                        continue;
                    }
                }

                int successfullyBound = 0;
                foreach (var unboundAnchor in unboundAnchors)
                {
                    try
                    {
                        if (!unboundAnchor.Localized)
                        {
                            Debug.LogWarning($"Skipping unlocalized anchor {unboundAnchor.Uuid}");
                            continue;
                        }

                        if (!unboundAnchor.TryGetPose(out var pose))
                        {
                            Debug.LogWarning($"Failed to get pose for anchor {unboundAnchor.Uuid}");
                            continue;
                        }

                        GameObject anchorObject = new GameObject($"SpatialAnchor_{unboundAnchor.Uuid}");
                        anchorObject.transform.SetPositionAndRotation(pose.position, pose.rotation);

                        OVRSpatialAnchor spatialAnchor = anchorObject.AddComponent<OVRSpatialAnchor>();
                        unboundAnchor.BindTo(spatialAnchor);
                        if (!spatialAnchor.Localized)
                        {
                            Debug.LogError($"Failed to bind anchor {unboundAnchor.Uuid} - anchor not localized after binding");
                            Destroy(anchorObject);
                            continue;
                        }

                        _spatialAnchors.Add(spatialAnchor);
                        successfullyBound++;

                        if (enableDebugVisualization)
                        {
                            CreateDebugVisualization(anchorObject.transform);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error processing anchor: {e.Message}");
                    }
                }

                totalSuccessfullyBound += successfullyBound;
                Debug.Log($"Successfully loaded and bound {successfullyBound} out of {unboundAnchors.Count} anchors in current batch");
            }
            Debug.Log($"Completed loading all batches. Total successfully bound: {totalSuccessfullyBound} out of {uuids.Count} anchors");
        }

        public async Task EraseAllSavedAnchors()
        {
            HashSet<Guid> uuids = GetSavedAnchorUuids();
            if (uuids.Count == 0)
            {
                Debug.Log("No saved anchors to erase.");
                return;
            }

            bool allBatchesSucceeded = true;
            foreach (var uuidBatch in BatchUuids(uuids))
            {
                Debug.Log($"Erasing batch of {uuidBatch.Count} anchors...");
                var result = await OVRSpatialAnchor.EraseAnchorsAsync(null, uuidBatch);
                if (!result.Success)
                {
                    Debug.LogError($"Failed to erase anchor batch: {result.Status}");
                    allBatchesSucceeded = false;
                }
            }

            if (allBatchesSucceeded)
            {
                Debug.Log("All saved anchors erased successfully.");
                PlayerPrefs.DeleteKey(AnchorUuidsKey);

                // Clean up all anchor GameObjects and references
                foreach (var anchor in _spatialAnchors)
                {
                    if (anchor != null && anchor.gameObject != null)
                    {
                        Destroy(anchor.gameObject);
                    }
                }
                _spatialAnchors.Clear();
            }
            else if (!allBatchesSucceeded)
            {
                Debug.LogError("Some anchor batches failed to erase. Some anchors may remain.");
            }
        }

        private void AddAnchorUuid(Guid uuid)
        {
            HashSet<Guid> uuids = GetSavedAnchorUuids();
            uuids.Add(uuid);
            SaveAnchorUuids(uuids);
            Debug.Log($"Added UUID to saved anchors. Total saved anchors: {uuids.Count}");
        }

        private HashSet<Guid> GetSavedAnchorUuids()
        {
            string serializedUuids = PlayerPrefs.GetString(AnchorUuidsKey, "");
            if (string.IsNullOrEmpty(serializedUuids))
            {
                return new HashSet<Guid>();
            }
            return new HashSet<Guid>(serializedUuids.Split(',').Select(Guid.Parse));
        }

        private void SaveAnchorUuids(HashSet<Guid> uuids)
        {
            string serializedUuids = string.Join(",", uuids.Select(uuid => uuid.ToString()));
            PlayerPrefs.SetString(AnchorUuidsKey, serializedUuids);
        }
    }
}