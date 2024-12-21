using UnityEngine;
using System.Threading.Tasks;
using Anaglyph.DisplayCapture;
using Anaglyph.DisplayCapture.ObjectDetection;

namespace Anaglyph.GCP
{
    public class ObjectDetectionGCPIntegration : MonoBehaviour
    {
        [SerializeField] private ObjectTracker objectTracker;
        [SerializeField] private ObjectAnalysisService analysisService;
        [SerializeField] private bool enableDetailedAnalysis = true;

        private void Start()
        {
            if (objectTracker != null)
            {
                objectTracker.OnTrackObjects += HandleTrackedObjects;
            }
        }

        private void OnDestroy()
        {
            if (objectTracker != null)
            {
                objectTracker.OnTrackObjects -= HandleTrackedObjects;
            }
        }

        private async void HandleTrackedObjects(System.Collections.Generic.IEnumerable<ObjectTracker.TrackedObject> objects)
        {
            if (!enableDetailedAnalysis) return;

            foreach (var trackedObject in objects)
            {
                // Only analyze objects that have stabilized and have high confidence
                if (trackedObject.confidence > 0.8f)
                {
                    await AnalyzeTrackedObject(trackedObject);
                }
            }
        }

        private async Task AnalyzeTrackedObject(ObjectTracker.TrackedObject trackedObject)
        {
            try
            {
                // Get the current camera texture
                var displayCapture = DisplayCaptureManager.Instance;
                if (displayCapture == null || displayCapture.ScreenCaptureTexture == null) return;

                // Create a copy of the texture
                Texture2D textureCopy = new Texture2D(
                    displayCapture.ScreenCaptureTexture.width,
                    displayCapture.ScreenCaptureTexture.height);

                // Copy the screen capture texture
                Graphics.CopyTexture(displayCapture.ScreenCaptureTexture, textureCopy);

                // Send to GCP for analysis
                var response = await analysisService.AnalyzeImage(textureCopy);

                // Log the detailed analysis
                Debug.Log($"Detailed analysis for tracked object {trackedObject.trackingId}:");
                Debug.Log($"GCP identified: {response.data.main_object}");
                Debug.Log($"Color: {response.data.attributes.color}");
                Debug.Log($"Size: {response.data.attributes.size}");
                Debug.Log($"Context: {response.data.context}");
                if (!string.IsNullOrEmpty(response.data.brand))
                {
                    Debug.Log($"Brand: {response.data.brand}");
                }

                // Clean up
                Destroy(textureCopy);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error analyzing tracked object: {e.Message}");
            }
        }
    }
}