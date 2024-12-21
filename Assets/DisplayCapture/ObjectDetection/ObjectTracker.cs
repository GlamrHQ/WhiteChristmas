using Anaglyph.XRTemplate.DepthKit;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Anaglyph.DisplayCapture.ObjectDetection
{
    public class ObjectTracker : MonoBehaviour
    {
        [SerializeField] private ObjectDetector objectDetector;
        [SerializeField] private float horizontalFieldOfViewDegrees = 82f;
        [SerializeField] private int positionHistorySize = 30; // Number of positions to keep track of
        [SerializeField] private float positionStabilityThreshold = 0.1f; // Meters
        [SerializeField] private int minPositionsForAnchor = 15; // Minimum positions needed before creating anchor
        [SerializeField] private float outlierThreshold = 0.5f; // Meters - distance from median to be considered outlier
        [SerializeField] private float objectTimeoutSeconds = 5f; // Time after which to remove tracked objects

        public float Fov => horizontalFieldOfViewDegrees;
        private Matrix4x4 displayCaptureProjection;

        private Dictionary<int, TrackedObjectHistory> trackedObjectHistories = new();
        private List<TrackedObject> trackedObjects = new();
        public IEnumerable<TrackedObject> TrackedObjects => trackedObjects;

        public event Action<IEnumerable<TrackedObject>> OnTrackObjects = delegate { };

        private class TrackedObjectHistory
        {
            private readonly Queue<Vector3> positions;
            private readonly int maxPositions;
            private readonly float outlierThreshold;
            private readonly int minPositionsForAnchor;
            private readonly float positionStabilityThreshold;

            public string label;
            public float confidence;
            public bool hasAnchor;
            public DateTime lastSeen = DateTime.Now;

            public TrackedObjectHistory(string label, float confidence, int maxPositions, float outlierThreshold,
                int minPositionsForAnchor, float positionStabilityThreshold)
            {
                this.label = label;
                this.confidence = confidence;
                this.hasAnchor = false;
                this.maxPositions = maxPositions;
                this.outlierThreshold = outlierThreshold;
                this.minPositionsForAnchor = minPositionsForAnchor;
                this.positionStabilityThreshold = positionStabilityThreshold;
                this.positions = new Queue<Vector3>(maxPositions);
            }

            public void AddPosition(Vector3 position)
            {
                positions.Enqueue(position);
                if (positions.Count > maxPositions)
                    positions.Dequeue();
                lastSeen = DateTime.Now;
            }

            public Vector3 GetStablePosition()
            {
                if (positions.Count < 3) return positions.Last();

                var positionsList = positions.ToList();
                positionsList.Sort((a, b) =>
                    (a.x + a.y + a.z).CompareTo(b.x + b.y + b.z)); // Simple sorting for median

                // Get median position
                Vector3 median = positionsList[positionsList.Count / 2];

                // Filter outliers
                var filteredPositions = positionsList.Where(p =>
                    Vector3.Distance(p, median) <= outlierThreshold).ToList();

                if (filteredPositions.Count == 0) return median;

                // Return average of non-outlier positions
                Vector3 sum = Vector3.zero;
                foreach (var pos in filteredPositions)
                    sum += pos;
                return sum / filteredPositions.Count;
            }

            public bool IsStable()
            {
                if (positions.Count < minPositionsForAnchor) return false;

                Vector3 stablePos = GetStablePosition();
                return positions.All(p => Vector3.Distance(p, stablePos) < positionStabilityThreshold);
            }
        }

        public struct TrackedObject
        {
            public int trackingId;
            public string text;
            public Vector3 center;
            public Pose pose;
            public float confidence;

            public TrackedObject(int trackingId, string text, Vector3 center, float confidence)
            {
                this.trackingId = trackingId;
                this.text = text;
                this.center = center;
                this.pose = new Pose(center, Quaternion.identity);
                this.confidence = confidence;
            }
        }

        private void Awake()
        {
            objectDetector.OnReadObjects += OnReadObjects;

            Vector2Int size = DisplayCaptureManager.Instance.Size;
            float aspect = size.x / (float)size.y;

            displayCaptureProjection = Matrix4x4.Perspective(Fov, aspect, 1, 100f);

            // Start cleanup coroutine
            StartCoroutine(CleanupOldTrackedObjects());
        }

        private System.Collections.IEnumerator CleanupOldTrackedObjects()
        {
            while (true)
            {
                var now = DateTime.Now;
                var keysToRemove = trackedObjectHistories
                    .Where(kvp => (now - kvp.Value.lastSeen).TotalSeconds > objectTimeoutSeconds)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    trackedObjectHistories.Remove(key);
                }

                yield return new WaitForSeconds(objectTimeoutSeconds);
            }
        }

        private void OnDestroy()
        {
            if (objectDetector != null)
                objectDetector.OnReadObjects -= OnReadObjects;
        }

        private async void OnReadObjects(IEnumerable<ObjectDetector.Result> objectResults)
        {
            trackedObjects.Clear();

            foreach (ObjectDetector.Result objectResult in objectResults)
            {
                if (objectResult.labels.Length == 0 || objectResult.labels[0].text == "Unknown")
                    continue;

                string objectLabel = objectResult.labels[0].text;
                float objectConfidence = objectResult.labels[0].confidence;

                // Get world position for the object
                Vector3 worldPosition = GetWorldPositionFromResult(objectResult);

                // Update tracking history
                if (!trackedObjectHistories.TryGetValue(objectResult.trackingId, out var history))
                {
                    history = new TrackedObjectHistory(
                        objectLabel,
                        objectConfidence,
                        positionHistorySize,
                        outlierThreshold,
                        minPositionsForAnchor,
                        positionStabilityThreshold
                    );
                    trackedObjectHistories[objectResult.trackingId] = history;
                }

                history.AddPosition(worldPosition);
                Vector3 stablePosition = history.GetStablePosition();

                // Create tracked object for visualization
                TrackedObject trackResult = new TrackedObject(
                    objectResult.trackingId,
                    objectLabel,
                    stablePosition,
                    objectConfidence
                );

                trackedObjects.Add(trackResult);

                // Check if we should create an anchor
                if (!history.hasAnchor && history.IsStable())
                {
                    await CreateAnchorForTrackedObject(trackResult);
                    history.hasAnchor = true;
                }
            }

            OnTrackObjects.Invoke(trackedObjects);
        }

        private Vector3 GetWorldPositionFromResult(ObjectDetector.Result objectResult)
        {
            float timestampInSeconds = objectResult.timestamp * 0.000000001f;
            OVRPlugin.PoseStatef headPoseState = OVRPlugin.GetNodePoseStateAtTime(timestampInSeconds, OVRPlugin.Node.Head);
            OVRPose headPose = headPoseState.Pose.ToOVRPose();
            Matrix4x4 headTransform = Matrix4x4.TRS(headPose.position, headPose.orientation, Vector3.one);

            ObjectDetector.BoundingBox bbox = objectResult.boundingBox;
            Vector2Int size = DisplayCaptureManager.Instance.Size;

            Vector2 centerUV = new Vector2(
                (bbox.left + bbox.right) / (2f * size.x),
                1f - (bbox.top + bbox.bottom) / (2f * size.y)
            );

            Vector3 centerWorldPos = Unproject(displayCaptureProjection, centerUV);
            centerWorldPos.z = -centerWorldPos.z;
            centerWorldPos = headTransform.MultiplyPoint(centerWorldPos);

            Vector3[] centerPointArray = new Vector3[] { centerWorldPos };
            DepthToWorld.SampleWorld(centerPointArray, out Vector3[] depthSampleResult);

            return depthSampleResult[0];
        }

        private async System.Threading.Tasks.Task CreateAnchorForTrackedObject(TrackedObject trackedObject)
        {
            if (SpatialAnchorManager.Instance != null)
            {
                await SpatialAnchorManager.Instance.CreateAnchorAtPoint(trackedObject.center);
            }
        }

        private static Vector3 Unproject(Matrix4x4 projection, Vector2 uv)
        {
            Vector2 v = 2f * uv - Vector2.one;
            var p = new Vector4(v.x, v.y, 0.1f, 1f);
            p = projection.inverse * p;
            return new Vector3(p.x, p.y, p.z) / p.w;
        }
    }
}