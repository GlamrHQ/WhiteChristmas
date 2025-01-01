using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using Firebase;
using Firebase.Storage;
using Firebase.Firestore;
using Firebase.Extensions;
using Firebase.Auth;
using Firebase.Functions;

namespace Anaglyph.Firebase
{
    public class FirebaseService : MonoBehaviour
    {
        private static FirebaseService instance;
        public static FirebaseService Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("FirebaseService");
                    instance = go.AddComponent<FirebaseService>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        private FirebaseStorage storage;
        private FirebaseFirestore firestore;
        private FirebaseAuth auth;
        private FirebaseFunctions functions;
        private bool isInitialized = false;

        [SerializeField] private string storageBucketUrl = "your-project-id.appspot.com";

        // Public property to access Firestore
        public FirebaseFirestore Firestore
        {
            get
            {
                if (!isInitialized)
                    throw new InvalidOperationException("Firebase is not initialized");
                return firestore;
            }
        }

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                InitializeFirebase();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private async void InitializeFirebase()
        {
            try
            {
                await FirebaseApp.CheckAndFixDependenciesAsync();

                // Initialize Auth (required for Storage)
                auth = FirebaseAuth.DefaultInstance;
                await auth.SignInAnonymouslyAsync();

                // Initialize Storage
                storage = FirebaseStorage.DefaultInstance;

                // Initialize Firestore
                firestore = FirebaseFirestore.DefaultInstance;

                // Initialize Functions
                functions = FirebaseFunctions.DefaultInstance;

                isInitialized = true;
                Debug.Log("Firebase initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize Firebase: {ex.Message}");
            }
        }

        public async Task<(string name, string documentId)> DetectShoe(byte[] imageData)
        {
            if (!isInitialized)
            {
                throw new InvalidOperationException("Firebase is not initialized");
            }

            try
            {
                // Convert image data to base64
                string base64Image = Convert.ToBase64String(imageData);

                // Create data object for the function call
                var data = new Dictionary<string, object>
                {
                    { "data", base64Image }
                };

                // Call the cloud function
                var function = functions.GetHttpsCallable("detectShoe");
                var result = await function.CallAsync(data);

                // Parse the result
                if (result.Data is Dictionary<string, object> resultDict &&
                    resultDict.TryGetValue("name", out object nameObj) &&
                    resultDict.TryGetValue("documentId", out object documentIdObj))
                {
                    return (nameObj.ToString(), documentIdObj.ToString());
                }

                throw new InvalidOperationException("Invalid response format from shoe detection function");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to detect shoe: {ex.Message}");
                throw;
            }
        }

        public async Task<(string downloadUrl, string storagePath)> UploadDetectedObjectImage(
            byte[] imageData,
            string objectLabel,
            string trackingId)
        {
            if (!isInitialized)
            {
                throw new InvalidOperationException("Firebase is not initialized");
            }

            try
            {
                // Generate a unique filename
                string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                string filename = $"detected_objects/{objectLabel}_{trackingId}_{timestamp}.jpg";

                // Get a reference to the storage location
                StorageReference storageRef = storage.GetReferenceFromUrl($"gs://{storageBucketUrl}");
                StorageReference imageRef = storageRef.Child(filename);

                // Upload the image
                var metadata = new MetadataChange
                {
                    ContentType = "image/jpeg",
                };

                await imageRef.PutBytesAsync(imageData, metadata);
                string downloadUrl = (await imageRef.GetDownloadUrlAsync()).AbsoluteUri;

                return (downloadUrl, filename);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to upload image: {ex.Message}");
                throw;
            }
        }

        public async Task SaveDetectedObjectData(
            string objectLabel,
            int trackingId,
            float confidence,
            Vector3 worldPosition,
            string imageUrl,
            string storagePath,
            Guid anchorId,
            string detectedShoeId,
            string detectedShoeName)
        {
            if (!isInitialized)
            {
                throw new InvalidOperationException("Firebase is not initialized");
            }

            try
            {
                var detectedObjectData = new Dictionary<string, object>
                {
                    { "objectLabel", objectLabel },
                    { "trackingId", trackingId },
                    { "confidence", confidence },
                    { "worldPosition", new Dictionary<string, float>
                        {
                            { "x", worldPosition.x },
                            { "y", worldPosition.y },
                            { "z", worldPosition.z }
                        }
                    },
                    { "imageUrl", imageUrl },
                    { "storagePath", storagePath },
                    { "anchorId", anchorId.ToString() },
                    { "detectedShoeId", detectedShoeId },
                    { "detectedShoeName", detectedShoeName },
                    { "timestamp", Timestamp.FromDateTime(DateTime.UtcNow) }
                };

                // Add to Firestore
                DocumentReference docRef = firestore.Collection("detected_objects").Document();
                await docRef.SetAsync(detectedObjectData);

                Debug.Log($"Object data saved to Firestore with ID: {docRef.Id}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save object data: {ex.Message}");
                throw;
            }
        }
    }
}