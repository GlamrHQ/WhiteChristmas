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

        public async Task CreateAnchorAtPoint(Vector3 point)
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
                return;
            }

            if (enableDebugVisualization)
            {
                CreateDebugVisualization(anchorObject.transform);
            }

            SaveAnchor(spatialAnchor);
        }

        private void CreateDebugVisualization(Transform parent)
        {
            GameObject visual = Instantiate(anchorPrefab, parent);
            visual.name = "AnchorVisualization";
        }

        private async void SaveAnchor(OVRSpatialAnchor anchor)
        {
            // Save the anchor
            if ((await anchor.SaveAnchorAsync()).Success)
            {
                // Remember UUID so you can load the anchor later
                AddAnchorUuid(anchor.Uuid);
            }
        }

        public async Task LoadSavedAnchors()
        {
            HashSet<Guid> uuids = GetSavedAnchorUuids();
            if (uuids.Count == 0)
            {
                Debug.Log("No saved anchors to load.");
                return;
            }

            var unboundAnchors = new List<OVRSpatialAnchor.UnboundAnchor>();
            var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(uuids, unboundAnchors);

            if (!result.Success)
            {
                Debug.LogError("Loading Unbound Anchors failed.");
                return;
            }

            foreach (var unboundAnchor in unboundAnchors)
            {
                if (!unboundAnchor.Localized && !unboundAnchor.Localizing)
                {
                    await unboundAnchor.LocalizeAsync();
                }
            }

            foreach (var unboundAnchor in unboundAnchors.Where(a => a.Localized))
            {
                if (!unboundAnchor.TryGetPose(out var pose)) continue;
                GameObject anchorObject = new GameObject("SpatialAnchor");
                anchorObject.transform.SetPositionAndRotation(pose.position, pose.rotation);

                OVRSpatialAnchor spatialAnchor = anchorObject.AddComponent<OVRSpatialAnchor>();
                unboundAnchor.BindTo(spatialAnchor);

                _spatialAnchors.Add(spatialAnchor);

                if (enableDebugVisualization)
                {
                    CreateDebugVisualization(anchorObject.transform);
                }
            }
        }

        public async Task EraseAllSavedAnchors()
        {
            HashSet<Guid> uuids = GetSavedAnchorUuids();
            if (uuids.Count == 0)
            {
                Debug.Log("No saved anchors to erase.");
                return;
            }

            var result = await OVRSpatialAnchor.EraseAnchorsAsync(null, uuids);
            if (result.Success)
            {
                Debug.Log("All saved anchors erased.");
                PlayerPrefs.DeleteKey(AnchorUuidsKey);
            }
            else
            {
                Debug.LogError($"Failed to erase anchors: {result.Status}");
            }
        }

        private void AddAnchorUuid(Guid uuid)
        {
            HashSet<Guid> uuids = GetSavedAnchorUuids();
            uuids.Add(uuid);
            SaveAnchorUuids(uuids);
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