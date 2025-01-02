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

        private Dictionary<int, ShoeHoldingState> shoeHoldingStates = new();
        private Dictionary<int, Vector3> lastKnownPositions = new();
        private Dictionary<int, int> stableFrameCount = new();

        public event Action<int, Vector3> OnShoePickedUp;
        public event Action<int, Vector3> OnShoeDropped;
        public event Action<int, Vector3> OnShoePositionUpdated;

        private class ShoeHoldingState
        {
            public bool IsHeld;
            public float HoldStartTime;
            public bool IsStable;
            public bool IsLeftHand;
            public OVRSpatialAnchor CurrentAnchor;
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
                    CurrentAnchor = null
                };
                lastKnownPositions[trackingId] = currentPosition;
                stableFrameCount[trackingId] = 0;
            }

            var state = shoeHoldingStates[trackingId];
            var lastPosition = lastKnownPositions[trackingId];

            // Check if either hand is close to the shoe using index finger tips
            var leftHandPos = leftIndexTipTransform.position;
            var rightHandPos = rightIndexTipTransform.position;

            var isNearLeftHand = Vector3.Distance(currentPosition, leftHandPos) < holdingThreshold;
            var isNearRightHand = Vector3.Distance(currentPosition, rightHandPos) < holdingThreshold;

            var isCurrentlyHeld = isNearLeftHand || isNearRightHand;
            var isLeftHand = isNearLeftHand;

            // Check position stability
            var positionDelta = Vector3.Distance(currentPosition, lastPosition);
            var isPositionStable = positionDelta < 0.01f; // 1cm threshold for stability

            if (isPositionStable)
            {
                stableFrameCount[trackingId]++;
            }
            else
            {
                stableFrameCount[trackingId] = 0;
            }

            // Update holding state
            if (isCurrentlyHeld && !state.IsHeld)
            {
                // Start holding
                state.IsHeld = true;
                state.HoldStartTime = Time.time;
                state.IsLeftHand = isLeftHand;
                OnShoePickedUp?.Invoke(trackingId, currentPosition);

                // Create or update anchor when shoe is picked up
                await UpdateAnchorForShoe(trackingId, currentPosition);
            }
            else if (!isCurrentlyHeld && state.IsHeld)
            {
                // Stop holding
                state.IsHeld = false;
                state.IsStable = false;
                OnShoeDropped?.Invoke(trackingId, currentPosition);
            }
            else if (state.IsHeld)
            {
                // Update stability state
                var holdDuration = Time.time - state.HoldStartTime;
                var hasEnoughStableFrames = stableFrameCount[trackingId] >= minStableFrames;

                if (holdDuration >= stableHoldDuration && hasEnoughStableFrames && !state.IsStable)
                {
                    state.IsStable = true;
                    // Update anchor when shoe becomes stable
                    await UpdateAnchorForShoe(trackingId, currentPosition);
                }

                // Update anchor if shoe has moved significantly
                if (positionDelta > 0.1f) // 10cm threshold for significant movement
                {
                    await UpdateAnchorForShoe(trackingId, currentPosition);
                }

                OnShoePositionUpdated?.Invoke(trackingId, currentPosition);
            }

            // Update last known position
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