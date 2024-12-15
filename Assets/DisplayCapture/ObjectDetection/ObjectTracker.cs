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

        public struct TrackedObject
        {
            public int trackingId;
            public string text;
            public Vector3[] corners; // 4 points
            public Pose pose;
            public float confidence;

            public TrackedObject(int trackingId, string text, float confidence)
            {
                this.trackingId = trackingId;
                this.text = text;
                corners = new Vector3[4];
                pose = new Pose();
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
                // For simplicity, we'll only use the label with the highest confidence
                string objectLabel = "Unknown";
                float objectConfidence = 0;
                if (objectResult.labels.Length > 0)
                {
                    objectLabel = objectResult.labels[0].text;
                    objectConfidence = objectResult.labels[0].confidence;
                }

                TrackedObject trackResult = new TrackedObject(objectResult.trackingId, objectLabel, objectConfidence);

                float timestampInSeconds = objectResult.timestamp * 0.000000001f;
                OVRPlugin.PoseStatef headPoseState = OVRPlugin.GetNodePoseStateAtTime(timestampInSeconds, OVRPlugin.Node.Head);
                OVRPose headPose = headPoseState.Pose.ToOVRPose();
                Matrix4x4 headTransform = Matrix4x4.TRS(headPose.position, headPose.orientation, Vector3.one);

                Vector3[] worldPoints = new Vector3[4];
                ObjectDetector.BoundingBox bbox = objectResult.boundingBox;

                Vector2Int size = DisplayCaptureManager.Instance.Size;

                // Convert bounding box to corner points
                Vector2[] uvs = new Vector2[] {
                    new Vector2(bbox.left / size.x, 1f - bbox.top / size.y),
                    new Vector2(bbox.left / size.x, 1f - bbox.bottom / size.y),
                    new Vector2(bbox.right / size.x, 1f - bbox.bottom / size.y),
                    new Vector2(bbox.right / size.x, 1f - bbox.top / size.y)
                };

                for (int i = 0; i < 4; i++)
                {
                    Vector3 worldPos = Unproject(displayCaptureProjection, uvs[i]);
                    worldPos.z = -worldPos.z;
                    worldPos = headTransform.MultiplyPoint(worldPos);
                    worldPoints[i] = worldPos;
                }

                DepthToWorld.SampleWorld(worldPoints, out trackResult.corners);

                var corners = trackResult.corners;

                Vector3 up = (corners[1] - corners[0]).normalized;
                Vector3 right = (corners[2] - corners[1]).normalized;
                Vector3 normal = -Vector3.Cross(up, right).normalized;

                Vector3 center = (corners[2] + corners[0]) / 2f;

                trackResult.pose = new Pose(center, Quaternion.LookRotation(normal, up));

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