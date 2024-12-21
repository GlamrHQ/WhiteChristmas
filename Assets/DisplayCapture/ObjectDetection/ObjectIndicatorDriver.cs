using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.DisplayCapture.ObjectDetection
{
    public class ObjectIndicatorDriver : MonoBehaviour
    {
        [SerializeField] private ObjectTracker objectTracker;
        [SerializeField] private GameObject indicatorPrefab;
        [SerializeField] private bool enableDebugVisualization = true;

        // Public input for the center eye transform
        public Transform centerEyeTransform;

        private List<ObjectIndicator> indicators = new(5);
        private bool isVisualizationEnabled;

        private void InstantiateIndicator()
        {
            var indicator = Instantiate(indicatorPrefab).GetComponent<ObjectIndicator>();
            indicator.centerEyeTransform = centerEyeTransform;
            indicator.gameObject.SetActive(isVisualizationEnabled);
            indicators.Add(indicator);
        }

        private void Awake()
        {
            isVisualizationEnabled = enableDebugVisualization;

            for (int i = 0; i < indicators.Capacity; i++)
                InstantiateIndicator();

            objectTracker.OnTrackObjects += OnTrackObjects;
        }

        private void OnDestroy()
        {
            foreach (ObjectIndicator indicator in indicators)
            {
                if (indicator != null && indicator.gameObject != null)
                    Destroy(indicator.gameObject);
            }

            if (objectTracker != null)
                objectTracker.OnTrackObjects -= OnTrackObjects;
        }

        public void SetDebugVisualization(bool enable)
        {
            isVisualizationEnabled = enable;
            foreach (var indicator in indicators)
            {
                if (indicator != null && indicator.gameObject != null)
                    indicator.gameObject.SetActive(isVisualizationEnabled);
            }
        }

        private void OnTrackObjects(IEnumerable<ObjectTracker.TrackedObject> results)
        {
            int i = 0;
            foreach (ObjectTracker.TrackedObject result in results)
            {
                if (i >= indicators.Count)
                    InstantiateIndicator();

                var indicator = indicators[i];
                if (indicator != null)
                {
                    indicator.gameObject.SetActive(isVisualizationEnabled);
                    if (isVisualizationEnabled)
                    {
                        indicator.Set(result);
                    }
                }
                i++;
            }

            // Hide unused indicators
            while (i < indicators.Count)
            {
                if (indicators[i] != null)
                    indicators[i].gameObject.SetActive(false);
                i++;
            }
        }
    }
}