using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using Firebase;
using Firebase.Storage;
using Firebase.Firestore;
using Firebase.Extensions;
using Firebase.Auth;

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
        private bool isInitialized = false;

        [SerializeField] private string storageBucketUrl = "your-project-id.appspot.com";

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

                isInitialized = true;
                Debug.Log("Firebase initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize Firebase: {ex.Message}");
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
                string downloadUrl = await imageRef.GetDownloadUrlAsync();

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
            Guid anchorId)
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