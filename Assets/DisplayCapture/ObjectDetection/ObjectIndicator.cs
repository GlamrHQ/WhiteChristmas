using TMPro;
using UnityEngine;

namespace Anaglyph.DisplayCapture.ObjectDetection
{
    public class ObjectIndicator : MonoBehaviour
    {
        [SerializeField] private TMP_Text textMesh;
        public TMP_Text TextMesh => textMesh;

        public Transform centerEyeTransform;

        public void Set(ObjectTracker.TrackedObject result) => Set(result.trackingId, result.text, result.center, result.confidence);

        public void Set(int trackingId, string text, Vector3 center, float confidence)
        {
            transform.position = center;

            // Make the text face the center eye transform
            if (centerEyeTransform != null)
            {
                transform.LookAt(centerEyeTransform.position);
                transform.Rotate(0, 180, 0); // Flip the text
            }

            textMesh.text = $"ID: {trackingId}\n{text}\nConf: {confidence:F2}";
        }
    }
}