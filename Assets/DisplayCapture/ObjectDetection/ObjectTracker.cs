using Anaglyph.XRTemplate.DepthKit;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.DisplayCapture.ObjectDetection
{
    public class ObjectTracker : MonoBehaviour
    {
        [SerializeField] private ObjectDetector objectDetector;

        [SerializeField] private float horizontalFieldOfViewDegrees = 82f;
        public float Fov => horizontalFieldOfViewDegrees;
        private Matrix4x4 displayCaptureProjection;

        private List<TrackedObject> trackedObjects = new();
        public IEnumerable<TrackedObject> TrackedObjects => trackedObjects;

        public event Action<IEnumerable<TrackedObject>> OnTrackObjects = delegate { };

        // MLKit supported labels
        private readonly HashSet<string> supportedLabels = new HashSet<string>()
        {
            "Fashion good",
            "Food",
            "Home good",
            "Place",
            "Plant"
        };

        public struct TrackedObject
        {
            public int trackingId;
            public string text;
            public Vector3 center; // Now only storing the center
            public Pose pose;
            public float confidence;

            public TrackedObject(int trackingId, string text, float confidence)
            {
                this.trackingId = trackingId;
                this.text = text;
                this.center = Vector3.zero;
                this.pose = new Pose();
                this.confidence = confidence;
            }
        }

        private void Awake()
        {
            objectDetector.OnReadObjects += OnReadObjects;

            Vector2Int size = DisplayCaptureManager.Instance.Size;
            float aspect = size.x / (float)size.y;

            displayCaptureProjection = Matrix4x4.Perspective(Fov, aspect, 1, 100f);
        }

        private void OnDestroy()
        {
            if (objectDetector != null)
                objectDetector.OnReadObjects -= OnReadObjects;
        }

        private void OnReadObjects(IEnumerable<ObjectDetector.Result> objectResults)
        {
            trackedObjects.Clear();

            foreach (ObjectDetector.Result objectResult in objectResults)
            {
                // Filter out unknown labels
                string objectLabel = "Unknown";
                float objectConfidence = 0;
                if (objectResult.labels.Length > 0 && supportedLabels.Contains(objectResult.labels[0].text))
                {
                    objectLabel = objectResult.labels[0].text;
                    objectConfidence = objectResult.labels[0].confidence;
                }
                else
                {
                    continue; // Skip this object if the label is not supported
                }

                TrackedObject trackResult = new TrackedObject(objectResult.trackingId, objectLabel, objectConfidence);

                float timestampInSeconds = objectResult.timestamp * 0.000000001f;
                OVRPlugin.PoseStatef headPoseState = OVRPlugin.GetNodePoseStateAtTime(timestampInSeconds, OVRPlugin.Node.Head);
                OVRPose headPose = headPoseState.Pose.ToOVRPose();
                Matrix4x4 headTransform = Matrix4x4.TRS(headPose.position, headPose.orientation, Vector3.one);

                ObjectDetector.BoundingBox bbox = objectResult.boundingBox;
                Vector2Int size = DisplayCaptureManager.Instance.Size;

                // Calculate the center UV of the bounding box
                Vector2 centerUV = new Vector2(
                    (bbox.left + bbox.right) / (2f * size.x),
                    1f - (bbox.top + bbox.bottom) / (2f * size.y)
                );

                // Unproject the center UV to get a world position
                Vector3 centerWorldPos = Unproject(displayCaptureProjection, centerUV);
                centerWorldPos.z = -centerWorldPos.z;
                centerWorldPos = headTransform.MultiplyPoint(centerWorldPos);

                // Sample the depth of the center point only
                Vector3[] centerPointArray = new Vector3[] { centerWorldPos };
                DepthToWorld.SampleWorld(centerPointArray, out Vector3[] depthSampleResult);

                trackResult.center = depthSampleResult[0];

                // We no longer calculate a full pose with orientation, just store the center
                trackResult.pose = new Pose(trackResult.center, Quaternion.identity);

                trackedObjects.Add(trackResult);
            }

            OnTrackObjects.Invoke(trackedObjects);
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