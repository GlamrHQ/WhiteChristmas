using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using Newtonsoft.Json;
using System.IO;

namespace Anaglyph.GCP
{
    [Serializable]
    public class ObjectAttributes
    {
        public string color;
        public string size;
        public string condition;
        public string[] distinguishing_features;
    }

    [Serializable]
    public class ObjectAnalysis
    {
        public string main_object;
        public float confidence;
        public ObjectAttributes attributes;
        public string context;
        public string visible_text;
        public string brand;
    }

    [Serializable]
    public class AnalysisMetadata
    {
        public string model;
        public float processing_time;
        public double timestamp;
        public string file_id;
        public string storage_path;
        public float upload_time;
        public float analysis_time;
        public float total_processing_time;
    }

    [Serializable]
    public class AnalysisResponse
    {
        public string status;
        public ObjectAnalysis data;
        public AnalysisMetadata metadata;
    }

    public class ObjectAnalysisService : MonoBehaviour
    {
        [SerializeField] private string apiUrl = "https://YOUR-CLOUD-RUN-URL/analyze";
        private const float TimeoutSeconds = 30f;

        public async Task<AnalysisResponse> AnalyzeImage(Texture2D image)
        {
            try
            {
                byte[] imageBytes = image.EncodeToJPG();
                string fileName = $"image_{DateTime.UtcNow.Ticks}.jpg";

                // Create form data
                WWWForm form = new WWWForm();
                form.AddBinaryData("file", imageBytes, fileName, "image/jpeg");

                // Create request
                using UnityWebRequest request = UnityWebRequest.Post(apiUrl, form);
                request.timeout = Mathf.RoundToInt(TimeoutSeconds);

                // Start time measurement
                float startTime = Time.realtimeSinceStartup;

                // Send request
                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                // Calculate total time
                float totalTime = Time.realtimeSinceStartup - startTime;
                Debug.Log($"API request completed in {totalTime:F2} seconds");

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"API request failed: {request.error}");
                }

                // Parse response
                string jsonResponse = request.downloadHandler.text;
                var response = JsonConvert.DeserializeObject<AnalysisResponse>(jsonResponse);

                // Log response details
                Debug.Log($"Analysis completed for object: {response.data.main_object}");
                Debug.Log($"Confidence: {response.data.confidence:P2}");
                Debug.Log($"Processing time: {response.metadata.total_processing_time:F2}s");
                Debug.Log($"Storage path: {response.metadata.storage_path}");

                return response;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error analyzing image: {e.Message}");
                throw;
            }
        }
    }
}