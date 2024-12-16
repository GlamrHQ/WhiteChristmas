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
            public Vector3[] corners; // 4 points: bottom-left, top-left, top-right, bottom-right
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

            // Create a perspective projection matrix based on the FOV and aspect ratio
            displayCaptureProjection = Matrix4x4.Perspective(Fov, aspect, 0.1f, 100f); // Adjust near and far clip planes if necessary
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
                // Use the label with the highest confidence
                string objectLabel = "Unknown";
                float objectConfidence = 0;
                if (objectResult.labels.Length > 0)
                {
                    objectLabel = objectResult.labels[0].text;
                    objectConfidence = objectResult.labels[0].confidence;
                }

                TrackedObject trackResult = new TrackedObject(objectResult.trackingId, objectLabel, objectConfidence);

                float timestampInSeconds = objectResult.timestamp * 0.000000001f;

                // Get the head pose at the timestamp of the object detection result
                OVRPlugin.PoseStatef headPoseState = OVRPlugin.GetNodePoseStateAtTime(timestampInSeconds, OVRPlugin.Node.Head);
                OVRPose headPose = headPoseState.Pose.ToOVRPose();
                Matrix4x4 headTransform = Matrix4x4.TRS(headPose.position, headPose.orientation, Vector3.one);

                ObjectDetector.BoundingBox bbox = objectResult.boundingBox;
                Vector2Int captureSize = DisplayCaptureManager.Instance.Size;

                // Convert bounding box to UV coordinates (and flip the V / Y-axis)
                Vector2[] uvs = new Vector2[] {
                    new Vector2(bbox.left / captureSize.x, bbox.bottom / captureSize.y), // Bottom-left
                    new Vector2(bbox.left / captureSize.x, bbox.top / captureSize.y), // Top-left
                    new Vector2(bbox.right / captureSize.x, bbox.top / captureSize.y), // Top-right
                    new Vector2(bbox.right / captureSize.x, bbox.bottom / captureSize.y)  // Bottom-right
                };

                // Adjust UVs for aspect ratio differences between the capture and the camera's viewport
                float captureAspect = (float)captureSize.x / captureSize.y;
                float cameraAspect = Camera.main.aspect;
                float aspectCorrection = cameraAspect / captureAspect;

                if (aspectCorrection > 1)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        uvs[i].x = (uvs[i].x - 0.5f) * aspectCorrection + 0.5f;
                    }
                }
                else
                {
                    for (int i = 0; i < 4; i++)
                    {
                        uvs[i].y = (uvs[i].y - 0.5f) / aspectCorrection + 0.5f;
                    }
                }

                // Unproject UVs to view space to get 3D points relative to the camera
                Vector3[] viewPoints = new Vector3[4];
                for (int i = 0; i < 4; i++)
                {
                    viewPoints[i] = Unproject(displayCaptureProjection, uvs[i]);
                }

                // Sample depth and convert view points to world space using the head transform
                if (DepthToWorld.SampleWorld(viewPoints, out trackResult.corners))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        trackResult.corners[i] = headTransform.MultiplyPoint(trackResult.corners[i]);
                    }

                    // Calculate pose (position and rotation) from the corners
                    Vector3 up = (trackResult.corners[1] - trackResult.corners[0]).normalized;
                    Vector3 right = (trackResult.corners[2] - trackResult.corners[1]).normalized;
                    Vector3 normal = -Vector3.Cross(up, right).normalized; // Ensure normal points towards the camera
                    Vector3 center = (trackResult.corners[0] + trackResult.corners[2]) / 2f;

                    trackResult.pose = new Pose(center, Quaternion.LookRotation(normal, up));
                    trackedObjects.Add(trackResult);
                }
            }

            OnTrackObjects.Invoke(trackedObjects);
        }

        // Helper function to unproject a UV coordinate to a point in view space
        private static Vector3 Unproject(Matrix4x4 projection, Vector2 uv)
        {
            // Convert UV to normalized device coordinates (NDC), ranging from -1 to 1
            Vector2 ndc = 2f * uv - Vector2.one;

            // Create a point in clip space with a nominal depth (e.g., 0.1)
            // The depth value here is not critical for our purpose, as we'll use DepthToWorld to get the actual depth
            Vector4 clipSpacePoint = new Vector4(ndc.x, ndc.y, 0.1f, 1f);

            // Convert from clip space to view space
            Vector4 viewSpacePoint = projection.inverse * clipSpacePoint;

            // Perspective divide to get the final view space point
            return new Vector3(viewSpacePoint.x, viewSpacePoint.y, viewSpacePoint.z) / viewSpacePoint.w;
        }
    }
}