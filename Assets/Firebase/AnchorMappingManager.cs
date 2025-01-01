using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Firestore;
using System.Linq;

namespace Anaglyph.Firebase
{
    public class AnchorMappingManager : MonoBehaviour
    {
        private static AnchorMappingManager instance;
        public static AnchorMappingManager Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("AnchorMappingManager");
                    instance = go.AddComponent<AnchorMappingManager>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        private Dictionary<string, string> anchorToShoeMap = new();
        private ListenerRegistration mappingListener;
        private string currentRoomId;
        private bool isInitialized = false;

        public IReadOnlyDictionary<string, string> AnchorToShoeMap => anchorToShoeMap;

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            // Clean up Firestore listener
            mappingListener?.Stop();
        }

        public async Task Initialize(string roomId)
        {
            if (isInitialized && currentRoomId == roomId)
            {
                return;
            }

            // Clean up existing listener if any
            mappingListener?.Stop();
            anchorToShoeMap.Clear();

            currentRoomId = roomId;
            await SetupFirestoreListener();
            isInitialized = true;
        }

        private async Task SetupFirestoreListener()
        {
            try
            {
                var docRef = FirebaseService.Instance.Firestore
                    .Collection("anchor_mappings")
                    .Document(currentRoomId);

                // Create the document if it doesn't exist
                var snapshot = await docRef.GetSnapshotAsync();
                if (!snapshot.Exists)
                {
                    await docRef.SetAsync(new Dictionary<string, object>
                    {
                        { "anchors", new Dictionary<string, string>() }
                    });
                }

                // Set up real-time listener
                mappingListener = docRef.Listen(async (snapshot) =>
                {
                    if (snapshot.Exists)
                    {
                        var data = snapshot.ConvertTo<Dictionary<string, object>>();
                        if (data.TryGetValue("anchors", out var anchorsObj) &&
                            anchorsObj is Dictionary<string, object> anchorsDict)
                        {
                            // Update local cache
                            anchorToShoeMap = anchorsDict.ToDictionary(
                                kvp => kvp.Key,
                                kvp => kvp.Value?.ToString() ?? string.Empty
                            );
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to setup Firestore listener: {ex.Message}");
                throw;
            }
        }

        public async Task AddAnchorMapping(string anchorUuid, string shoeDocumentId)
        {
            if (!isInitialized)
            {
                throw new InvalidOperationException("AnchorMappingManager not initialized");
            }

            try
            {
                var docRef = FirebaseService.Instance.Firestore
                    .Collection("anchor_mappings")
                    .Document(currentRoomId);

                // Update Firestore
                await docRef.UpdateAsync(new Dictionary<string, object>
                {
                    { $"anchors.{anchorUuid}", shoeDocumentId }
                });

                // Local cache will be updated via the listener
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to add anchor mapping: {ex.Message}");
                throw;
            }
        }

        public async Task RemoveAnchorMapping(string anchorUuid)
        {
            if (!isInitialized)
            {
                throw new InvalidOperationException("AnchorMappingManager not initialized");
            }

            try
            {
                var docRef = FirebaseService.Instance.Firestore
                    .Collection("anchor_mappings")
                    .Document(currentRoomId);

                // Update Firestore
                await docRef.UpdateAsync(new Dictionary<string, object>
                {
                    { $"anchors.{anchorUuid}", FieldValue.Delete }
                });

                // Local cache will be updated via the listener
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to remove anchor mapping: {ex.Message}");
                throw;
            }
        }

        public string GetShoeDocumentId(string anchorUuid)
        {
            return anchorToShoeMap.TryGetValue(anchorUuid, out var shoeDocumentId)
                ? shoeDocumentId
                : null;
        }
    }
}