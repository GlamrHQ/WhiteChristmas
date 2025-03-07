using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using System;
using System.Linq;

namespace Anaglyph.Firebase
{
    public class ShoeDataCache : MonoBehaviour
    {
        private static ShoeDataCache instance;
        public static ShoeDataCache Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("ShoeDataCache");
                    instance = go.AddComponent<ShoeDataCache>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        private Dictionary<string, ShoeData> shoeCache = new();
        private Dictionary<string, ShoeVariantData> variantCache = new();
        private ListenerRegistration shoesListener;
        private ListenerRegistration variantsListener;
        private bool isInitialized = false;

        // Add events for data changes
        public event Action OnCacheInitialized;
        public event Action<string> OnShoeDataUpdated;

        // Add property to check initialization
        public bool IsInitialized => isInitialized;

        public class ShoeData
        {
            public string Name { get; set; }
            public string ModelName { get; set; }
            public string Story { get; set; }
            public float BasePrice { get; set; }
            public float HypeScore { get; set; }
            public float ResaleValueIncrease { get; set; }
            public string DefaultColor { get; set; }
            public List<string> AvailableColors { get; set; }
            public List<float> AvailableSizes { get; set; }
            public string MainImageUrl { get; set; }
            public List<string> ImageUrls { get; set; }
            public string Model3dUrl { get; set; }
        }

        public class ShoeVariantData
        {
            public string Color { get; set; }
            public float Size { get; set; }
            public float Price { get; set; }
            public string Model3dUrl { get; set; }
            public int Stock { get; set; }
            public string ShoeId { get; set; }
        }

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                _ = Initialize();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            shoesListener?.Stop();
            variantsListener?.Stop();
        }

        public async Task Initialize()
        {
            if (isInitialized) return;

            try
            {
                await SetupFirestoreListeners();
                isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize ShoeDataCache: {ex.Message}");
            }
        }

        private async Task SetupFirestoreListeners()
        {
            var firestore = FirebaseService.Instance.Firestore;

            // Setup shoes listener
            shoesListener = firestore.Collection("shoes")
                .Listen(snapshot =>
                {
                    foreach (var change in snapshot.GetChanges())
                    {
                        var doc = change.Document;
                        switch (change.ChangeType)
                        {
                            case DocumentChange.Type.Added:
                            case DocumentChange.Type.Modified:
                                shoeCache[doc.Id] = ConvertToShoeData(doc);
                                OnShoeDataUpdated?.Invoke(doc.Id);
                                break;
                            case DocumentChange.Type.Removed:
                                shoeCache.Remove(doc.Id);
                                OnShoeDataUpdated?.Invoke(doc.Id);
                                break;
                        }
                    }
                });

            // Setup variants listener
            variantsListener = firestore.Collection("shoe_variants")
                .Listen(snapshot =>
                {
                    foreach (var change in snapshot.GetChanges())
                    {
                        var doc = change.Document;
                        switch (change.ChangeType)
                        {
                            case DocumentChange.Type.Added:
                            case DocumentChange.Type.Modified:
                                variantCache[doc.Id] = ConvertToVariantData(doc);
                                break;
                            case DocumentChange.Type.Removed:
                                variantCache.Remove(doc.Id);
                                break;
                        }
                    }
                });

            // Initial data load
            var shoesSnapshot = await firestore.Collection("shoes").GetSnapshotAsync();
            foreach (var doc in shoesSnapshot.Documents)
            {
                shoeCache[doc.Id] = ConvertToShoeData(doc);
            }

            var variantsSnapshot = await firestore.Collection("shoe_variants").GetSnapshotAsync();
            foreach (var doc in variantsSnapshot.Documents)
            {
                variantCache[doc.Id] = ConvertToVariantData(doc);
            }

            // Notify that cache is initialized
            OnCacheInitialized?.Invoke();
        }

        private ShoeData ConvertToShoeData(DocumentSnapshot doc)
        {
            return new ShoeData
            {
                Name = doc.GetValue<string>("name"),
                ModelName = doc.GetValue<string>("modelName"),
                Story = doc.GetValue<string>("story"),
                BasePrice = doc.GetValue<float>("basePrice"),
                HypeScore = doc.GetValue<float>("hypeScore"),
                ResaleValueIncrease = doc.GetValue<float>("resaleValueIncrease"),
                DefaultColor = doc.GetValue<string>("defaultColor"),
                AvailableColors = doc.GetValue<List<string>>("availableColors"),
                AvailableSizes = doc.GetValue<List<float>>("availableSizes"),
                MainImageUrl = doc.GetValue<string>("mainImageUrl"),
                ImageUrls = doc.GetValue<List<string>>("imageUrls"),
                Model3dUrl = doc.GetValue<string>("model3dUrl")
            };
        }

        private ShoeVariantData ConvertToVariantData(DocumentSnapshot doc)
        {
            return new ShoeVariantData
            {
                Color = doc.GetValue<string>("color"),
                Size = doc.GetValue<float>("size"),
                Price = doc.GetValue<float>("price"),
                Model3dUrl = doc.GetValue<string>("model3dUrl"),
                Stock = doc.GetValue<int>("stock"),
                ShoeId = doc.GetValue<string>("shoeId")
            };
        }

        public ShoeData GetShoeData(string shoeId)
        {
            return shoeCache.TryGetValue(shoeId, out var data) ? data : null;
        }

        public List<ShoeVariantData> GetShoeVariants(string shoeId)
        {
            return variantCache.Values
                .Where(v => v.ShoeId == shoeId)
                .ToList();
        }
    }
}