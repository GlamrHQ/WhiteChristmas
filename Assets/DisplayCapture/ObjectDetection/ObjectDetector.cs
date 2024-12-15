using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.DisplayCapture.ObjectDetection
{
    public class ObjectDetector : MonoBehaviour
    {
        [Serializable]
        private struct Results
        {
            public Result[] results;
        }

        [Serializable]
        public struct Result
        {
            public BoundingBox boundingBox;
            public Label[] labels;
            public long timestamp;
            public int trackingId;
        }

        [Serializable]
        public struct BoundingBox
        {
            public float left, top, right, bottom;
        }

        [Serializable]
        public struct Label
        {
            public string text;
            public float confidence;
            public int index;
        }

        private class AndroidInterface
        {
            private AndroidJavaClass androidClass;
            private AndroidJavaObject androidInstance;

            public AndroidInterface(GameObject messageReceiver)
            {
                androidClass = new AndroidJavaClass("com.trev3d.DisplayCapture.ObjectDetection.CustomObjectDetector");
                androidInstance = androidClass.CallStatic<AndroidJavaObject>("getInstance");
                androidInstance.Call("setup", messageReceiver.name);
            }

            public void SetEnabled(bool enabled) => androidInstance.Call("setEnabled", enabled);
        }

        public event Action<IEnumerable<Result>> OnReadObjects = delegate { };

        private AndroidInterface androidInterface;

        private void Awake()
        {
            androidInterface = new AndroidInterface(gameObject);
        }

        private void OnEnable()
        {
            androidInterface.SetEnabled(true);
        }

        private void OnDisable()
        {
            androidInterface.SetEnabled(false);
        }

        private void OnDestroy()
        {
            OnReadObjects = delegate { };
        }

        // Called by Android 

#pragma warning disable IDE0051 // Remove unused private members
        private void OnObjectResults(string json)
        {
            Results results = JsonUtility.FromJson<Results>(json);
            OnReadObjects.Invoke(results.results);
        }
#pragma warning restore IDE0051 // Remove unused private members
    }
}