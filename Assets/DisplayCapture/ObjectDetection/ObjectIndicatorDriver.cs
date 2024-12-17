using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.DisplayCapture.ObjectDetection
{
    public class ObjectIndicatorDriver : MonoBehaviour
    {
        [SerializeField] private ObjectTracker objectTracker;
        [SerializeField] private GameObject indicatorPrefab;

        // Public input for the center eye transform
        public Transform centerEyeTransform;

        private List<ObjectIndicator> indicators = new(5);

        private void InstantiateIndicator()
        {
            var indicator = Instantiate(indicatorPrefab).GetComponent<ObjectIndicator>();
            indicator.centerEyeTransform = centerEyeTransform; // Pass the transform to the indicator
            indicators.Add(indicator);
        }

        private void Awake()
        {
            for (int i = 0; i < indicators.Capacity; i++)
                InstantiateIndicator();

            objectTracker.OnTrackObjects += OnTrackObjects;
        }

        private void OnDestroy()
        {
            foreach (ObjectIndicator indicator in indicators)
            {
                Destroy(indicator.gameObject);
            }

            if (objectTracker != null)
                objectTracker.OnTrackObjects -= OnTrackObjects;
        }

        private void OnTrackObjects(IEnumerable<ObjectTracker.TrackedObject> results)
        {
            int i = 0;
            foreach (ObjectTracker.TrackedObject result in results)
            {
                if (i >= indicators.Count)
                    InstantiateIndicator();

                indicators[i].gameObject.SetActive(true);

                indicators[i].Set(result);
                i++;
            }

            while (i < indicators.Count)
            {
                indicators[i].gameObject.SetActive(false);
                i++;
            }
        }
    }
}