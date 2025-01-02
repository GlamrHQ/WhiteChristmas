using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Anaglyph.DisplayCapture.ObjectDetection;
using Anaglyph.DisplayCapture;

namespace Anaglyph.ShoeInteraction
{
    public class HandShoeInteractionManager : MonoBehaviour
    {
        [SerializeField] private ObjectTracker objectTracker;
        [SerializeField] private Transform leftIndexTipTransform;  // Reference to left index finger tip transform
        [SerializeField] private Transform rightIndexTipTransform; // Reference to right index finger tip transform
        [SerializeField] private float holdingThreshold = 0.15f; // Distance in meters to consider shoe being held
        [SerializeField] private float stableHoldDuration = 0.5f; // Time in seconds before considering hold stable
        [SerializeField] private int minStableFrames = 15; // Minimum frames to consider position stable
        [SerializeField] private float maxAnchorDistance = 3f; // Maximum distance for anchor creation/update
        [SerializeField] private float shelfStabilityThreshold = 0.005f; // 5mm threshold for shelf stability
        [SerializeField] private int shelfStableFramesRequired = 30; // Frames required to consider shoe stable on shelf

        private Dictionary<int, ShoeHoldingState> shoeHoldingStates = new();
        private Dictionary<int, Vector3> lastKnownPositions = new();
        private Dictionary<int, int> stableFrameCount = new();
        private Dictionary<int, int> shelfStableFrameCount = new(); // Track stability after dropping

        public event Action<int, Vector3> OnShoePickedUp;
        public event Action<int, Vector3> OnShoeDropped;
        public event Action<int, Vector3> OnShoePositionUpdated;
        public event Action<int, Vector3> OnShoePlacedOnShelf;

        private class ShoeHoldingState
        {
            public bool IsHeld;
            public float HoldStartTime;
            public bool IsStable;
            public bool IsLeftHand;
            public OVRSpatialAnchor CurrentAnchor;
            public bool IsBeingPlaced; // Track if we're in the process of placing on shelf
            public Vector3 LastStablePosition;
        }

        private void Start()
        {
            if (objectTracker == null)
            {
                objectTracker = FindObjectOfType<ObjectTracker>();
            }

            objectTracker.OnTrackObjects += OnObjectsTracked;
        }

        private void OnDestroy()
        {
            if (objectTracker != null)
            {
                objectTracker.OnTrackObjects -= OnObjectsTracked;
            }
        }

        private void OnObjectsTracked(IEnumerable<ObjectTracker.TrackedObject> trackedObjects)
        {
            foreach (var trackedObject in trackedObjects)
            {
                if (!IsShoe(trackedObject)) continue;

                ProcessShoeObject(trackedObject);
            }

            // Clean up states for objects no longer being tracked
            var currentIds = trackedObjects.Select(o => o.trackingId).ToHashSet();
            var stateIdsToRemove = shoeHoldingStates.Keys.Where(id => !currentIds.Contains(id)).ToList();

            foreach (var id in stateIdsToRemove)
            {
                if (shoeHoldingStates[id].IsHeld)
                {
                    OnShoeDropped?.Invoke(id, lastKnownPositions[id]);
                }
                shoeHoldingStates.Remove(id);
                lastKnownPositions.Remove(id);
                stableFrameCount.Remove(id);
                shelfStableFrameCount.Remove(id);
            }
        }

        private bool IsShoe(ObjectTracker.TrackedObject obj)
        {
            return obj.text.ToLower().Contains("shoe") && obj.confidence > 0.7f;
        }

        private async void ProcessShoeObject(ObjectTracker.TrackedObject shoe)
        {
            var trackingId = shoe.trackingId;
            var currentPosition = shoe.center;

            // Initialize state if needed
            if (!shoeHoldingStates.ContainsKey(trackingId))
            {
                shoeHoldingStates[trackingId] = new ShoeHoldingState
                {
                    IsHeld = false,
                    HoldStartTime = 0,
                    IsStable = false,
                    IsLeftHand = false,
                    CurrentAnchor = null,
                    IsBeingPlaced = false,
                    LastStablePosition = currentPosition
                };
                lastKnownPositions[trackingId] = currentPosition;
                stableFrameCount[trackingId] = 0;
                shelfStableFrameCount[trackingId] = 0;
            }

            var state = shoeHoldingStates[trackingId];
            var lastPosition = lastKnownPositions[trackingId];

            // Check hand proximity
            var leftHandPos = leftIndexTipTransform.position;
            var rightHandPos = rightIndexTipTransform.position;
            var isNearLeftHand = Vector3.Distance(currentPosition, leftHandPos) < holdingThreshold;
            var isNearRightHand = Vector3.Distance(currentPosition, rightHandPos) < holdingThreshold;
            var isCurrentlyHeld = isNearLeftHand || isNearRightHand;

            // Check position stability
            var positionDelta = Vector3.Distance(currentPosition, lastPosition);
            var isPositionStable = positionDelta < 0.01f;

            if (isPositionStable)
            {
                stableFrameCount[trackingId]++;
            }
            else
            {
                stableFrameCount[trackingId] = 0;
            }

            // Handle shoe being picked up
            if (isCurrentlyHeld && !state.IsHeld)
            {
                state.IsHeld = true;
                state.HoldStartTime = Time.time;
                state.IsLeftHand = isNearLeftHand;
                state.IsBeingPlaced = false;
                shelfStableFrameCount[trackingId] = 0;
                OnShoePickedUp?.Invoke(trackingId, currentPosition);

                // Create or update anchor when shoe is picked up
                await UpdateAnchorForShoe(trackingId, currentPosition);
            }
            // Handle shoe being dropped
            else if (!isCurrentlyHeld && state.IsHeld)
            {
                state.IsHeld = false;
                state.IsStable = false;
                state.IsBeingPlaced = true;
                state.LastStablePosition = currentPosition;
                OnShoeDropped?.Invoke(trackingId, currentPosition);
            }
            // Handle shoe being held
            else if (state.IsHeld)
            {
                var holdDuration = Time.time - state.HoldStartTime;
                var hasEnoughStableFrames = stableFrameCount[trackingId] >= minStableFrames;

                if (holdDuration >= stableHoldDuration && hasEnoughStableFrames && !state.IsStable)
                {
                    state.IsStable = true;
                    await UpdateAnchorForShoe(trackingId, currentPosition);
                }

                if (positionDelta > 0.1f)
                {
                    await UpdateAnchorForShoe(trackingId, currentPosition);
                }

                OnShoePositionUpdated?.Invoke(trackingId, currentPosition);
            }
            // Handle shoe being placed on shelf
            else if (state.IsBeingPlaced)
            {
                // Check if the shoe has stabilized on the shelf
                var shelfDelta = Vector3.Distance(currentPosition, state.LastStablePosition);

                if (shelfDelta < shelfStabilityThreshold)
                {
                    shelfStableFrameCount[trackingId]++;

                    if (shelfStableFrameCount[trackingId] >= shelfStableFramesRequired)
                    {
                        state.IsBeingPlaced = false;
                        await CreateShelfAnchor(trackingId, currentPosition);
                        OnShoePlacedOnShelf?.Invoke(trackingId, currentPosition);
                    }
                }
                else
                {
                    shelfStableFrameCount[trackingId] = 0;
                    state.LastStablePosition = currentPosition;
                }
            }

            lastKnownPositions[trackingId] = currentPosition;
        }

        private async Task UpdateAnchorForShoe(int trackingId, Vector3 position)
        {
            var state = shoeHoldingStates[trackingId];

            // Check if there's an existing anchor nearby
            var nearbyAnchor = SpatialAnchorManager.Instance.SpatialAnchors
                .FirstOrDefault(a => Vector3.Distance(a.transform.position, position) < maxAnchorDistance);

            if (nearbyAnchor != null)
            {
                // Delete existing anchor and create a new one at the current position
                // This is because Meta's SDK doesn't support updating anchor positions
                if (state.CurrentAnchor != null)
                {
                    // First erase the anchor from persistent storage
                    await state.CurrentAnchor.EraseAnchorAsync();
                    // Then destroy the GameObject
                    Destroy(state.CurrentAnchor.gameObject);
                }

                // Also erase the nearby anchor if it's different from our current anchor
                if (nearbyAnchor != state.CurrentAnchor)
                {
                    await nearbyAnchor.EraseAnchorAsync();
                    Destroy(nearbyAnchor.gameObject);
                }

                var newAnchor = await SpatialAnchorManager.Instance.CreateAnchorAtPoint(position);
                if (newAnchor != null)
                {
                    state.CurrentAnchor = newAnchor;
                }
            }
            else
            {
                // If we have a current anchor that's too far away, clean it up
                if (state.CurrentAnchor != null &&
                    Vector3.Distance(state.CurrentAnchor.transform.position, position) >= maxAnchorDistance)
                {
                    await state.CurrentAnchor.EraseAnchorAsync();
                    Destroy(state.CurrentAnchor.gameObject);
                    state.CurrentAnchor = null;
                }

                // Create a new anchor if we don't have one
                if (state.CurrentAnchor == null)
                {
                    var newAnchor = await SpatialAnchorManager.Instance.CreateAnchorAtPoint(position);
                    if (newAnchor != null)
                    {
                        state.CurrentAnchor = newAnchor;
                    }
                }
            }
        }

        private async Task CreateShelfAnchor(int trackingId, Vector3 position)
        {
            var state = shoeHoldingStates[trackingId];

            // Clean up any existing anchors
            if (state.CurrentAnchor != null)
            {
                await state.CurrentAnchor.EraseAnchorAsync();
                Destroy(state.CurrentAnchor.gameObject);
                state.CurrentAnchor = null;
            }

            // Create new anchor at shelf position
            var newAnchor = await SpatialAnchorManager.Instance.CreateAnchorAtPoint(position);
            if (newAnchor != null)
            {
                state.CurrentAnchor = newAnchor;
                // Save the anchor
                var saveResult = await newAnchor.SaveAnchorAsync();
                if (!saveResult.Success)
                {
                    Debug.LogError($"Failed to save shelf anchor. Status: {saveResult.Status}");
                }
            }
        }

        public bool IsObjectHeld(int trackingId)
        {
            return shoeHoldingStates.TryGetValue(trackingId, out var state) && state.IsHeld;
        }

        public bool IsObjectStable(int trackingId)
        {
            return shoeHoldingStates.TryGetValue(trackingId, out var state) && state.IsStable;
        }

        public bool IsHeldByLeftHand(int trackingId)
        {
            return shoeHoldingStates.TryGetValue(trackingId, out var state) && state.IsHeld && state.IsLeftHand;
        }
    }
}