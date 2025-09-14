using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif
using GLTFast;

public class ShopifyProductFetcher : MonoBehaviour
{
    // === Output ===
    public List<VRItem> Items = new List<VRItem>();

    // === Config ===
    [Header("Shopify Storefront API")]
    [SerializeField] private string endpoint;
    [SerializeField] private string storefrontAccessToken;

    private void Awake()
    {
        // Set up singleton reference
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        // Load .env values
        var envPath = System.IO.Path.Combine(Application.dataPath, "..", ".env");
        if (System.IO.File.Exists(envPath))
        {
            var lines = System.IO.File.ReadAllLines(envPath);
            foreach (var line in lines)
            {
                if (line.StartsWith("SHOPIFY_STOREFRONT_ACCESS_TOKEN="))
                    storefrontAccessToken = line.Substring("SHOPIFY_STOREFRONT_ACCESS_TOKEN=".Length);
                if (line.StartsWith("SHOPIFY_ENDPOINT="))
                    endpoint = line.Substring("SHOPIFY_ENDPOINT=".Length);
            }
        }
    }
    [SerializeField, Tooltip("How many products to fetch on the first page")]
    private int first = 25;
    
    [Header("Model Settings")]
    [SerializeField] private Transform modelsParent; // Parent for instantiated models
    [SerializeField] private Vector3 modelSpacing = new Vector3(1.5f, 0, 0); // Spacing between models
    [SerializeField] private bool autoInstantiate = true; // Auto-instantiate models on fetch
    [SerializeField] private float modelScale = 0.5f; // Scale factor for models
    [SerializeField] private bool preloadModelsAtStart = false; // Preload models in the background
    [SerializeField] private Vector3 prefabStoragePosition = new Vector3(1000, 0, 0); // Far away from the origin

    // GraphQL query: products with id/title/description, media, and pricing
    private const string Query = @"
query GetProducts($first:Int!) {
  products(first: $first) {
    pageInfo { hasNextPage }
    edges {
      cursor
      node {
        id
        handle
        title
        description
        priceRange {
          minVariantPrice {
            amount
          }
        }
        media(first: 10) {
          edges {
            node {
              __typename
              ... on Model3d { sources { url mimeType } }
              ... on MediaImage { image { url altText } }
              ... on Video { sources { url mimeType } }
            }
          }
        }
      }
    }
  }
}";

    [Serializable] public class VRItem
    {
        public string id;
        public string title;
        public string description;
        public string modelUrl; // preferred GLB if available
        public Texture2D image; // Product image
        public float cost; // Product cost
        public GameObject instantiatedModel; // Reference to the instantiated model in the scene
        public bool isLoading; // Track loading state
    }

    // ---------- Unity ----------
    private void Start()
    {
        StartCoroutine(FetchProductsCoroutine());
    }

    // Add a callback for when products are fetched
    private void OnProductsFetched()
    {
        if (preloadModelsAtStart)
        {
            StartCoroutine(PreloadAllModelsCoroutine());
        }
    }

    // ---------- Networking ----------
    [Serializable] private class GraphQLBody
    {
        public string query;
        public Variables variables;
    }

    [Serializable] private class Variables
    {
        public int first;
    }

    [Serializable] private class Root
    {
        public Data data;
        public Extensions extensions;
    }

    [Serializable] private class Data
    {
        public Products products;
    }

    [Serializable] private class Products
    {
        public PageInfo pageInfo;
        public ProductEdge[] edges;
    }

    [Serializable] private class PageInfo
    {
        public bool hasNextPage;
    }

    [Serializable] private class ProductEdge
    {
        public string cursor;
        public Product node;
    }

    [Serializable] private class Product
    {
        public string id;
        public string handle;
        public string title;
        public string description;
        public Media media;
        public PriceRange priceRange;
    }

    [Serializable] private class PriceRange
    {
        public VariantPrice minVariantPrice;
    }

    [Serializable] private class VariantPrice
    {
        public string amount;
    }

    [Serializable] private class Media
    {
        public MediaEdge[] edges;
    }

    [Serializable] private class MediaEdge
    {
        public MediaNode node;
    }

    [Serializable] private class MediaNode
    {
        // GraphQL __typename maps directly here
        public string __typename;

        // Present when __typename == "Model3d" or "Video"
        public Source[] sources;

        // Present when __typename == "MediaImage"
        public MediaImage image;
    }

    [Serializable] private class Source
    {
        public string url;
        public string mimeType;
    }

    [Serializable] private class MediaImage
    {
        public string url;
        public string altText;
    }

    [Serializable] private class Extensions
    {
        public Cost cost;
    }

    [Serializable] private class Cost
    {
        public int requestedQueryCost;
    }

    private IEnumerator FetchProductsCoroutine()
    {
        // Build JSON body
        var body = new GraphQLBody
        {
            query = Query,
            variables = new Variables { first = Mathf.Max(1, first) }
        };
        var json = JsonUtility.ToJson(body);

        using (var req = new UnityWebRequest(endpoint, "POST"))
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(payload);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-Shopify-Storefront-Access-Token", storefrontAccessToken);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Shopify request failed: {req.error}\n{req.downloadHandler.text}");
                yield break;
            }

            var text = req.downloadHandler.text;
            Root parsed;
            try
            {
                parsed = JsonUtility.FromJson<Root>(text);
            }
            catch (Exception e)
            {
                Debug.LogError($"JSON parse error: {e.Message}\nPayload:\n{text}");
                yield break;
            }

            if (parsed == null || parsed.data == null || parsed.data.products == null || parsed.data.products.edges == null)
            {
                Debug.LogWarning("No products returned (null or empty).");
                yield break;
            }

            Items.Clear();
            foreach (var edge in parsed.data.products.edges)
            {
                if (edge?.node == null) continue;

                var p = edge.node;
                var modelUrl = ExtractBestModelUrl(p.media);
                var imageUrl = ExtractImageUrl(p.media);
                var cost = ExtractCost(p.priceRange);

                var item = new VRItem
                {
                    id = p.id,
                    title = p.title,
                    description = string.IsNullOrEmpty(p.description) ? "" : p.description,
                    modelUrl = modelUrl,
                    cost = cost,
                    isLoading = false
                };
                Items.Add(item);

                // Start downloading the image if available
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    StartCoroutine(DownloadImageCoroutine(imageUrl, Items.Count - 1));
                }
            }

            Debug.Log($"Fetched {Items.Count} product(s). Example[0]: " +
                      (Items.Count > 0
                          ? $"{Items[0].title} | {Items[0].id} | modelUrl={(string.IsNullOrEmpty(Items[0].modelUrl) ? "N/A" : Items[0].modelUrl)} | cost=${Items[0].cost}"
                          : "N/A"));
    
            // Notify that products have been fetched
            OnProductsFetched();
            yield break;
        }
    }

    /// <summary>
    /// Picks a sensible model URL from media:
    /// 1) Prefer GLB (mimeType contains 'gltf-binary').
    /// 2) Fallback to the first Model3d source if no explicit GLB.
    /// 3) Return null if no Model3d present.
    /// </summary>
    private string ExtractBestModelUrl(Media media)
    {
        if (media?.edges == null) return null;

        // First pass: find any Model3d node
        foreach (var me in media.edges)
        {
            var node = me?.node;
            if (node == null) continue;

            if (node.__typename == "Model3d" && node.sources != null && node.sources.Length > 0)
            {
                // Prefer GLB
                foreach (var s in node.sources)
                {
                    if (!string.IsNullOrEmpty(s?.mimeType) &&
                        s.mimeType.IndexOf("gltf-binary", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return s.url;
                    }
                }
                // Fallback: first source
                return node.sources[0].url;
            }
        }

        return null; // No 3D model found
    }

    /// <summary>
    /// Extracts the first image URL from media
    /// </summary>
    private string ExtractImageUrl(Media media)
    {
        if (media?.edges == null) return null;

        foreach (var me in media.edges)
        {
            var node = me?.node;
            if (node == null) continue;

            if (node.__typename == "MediaImage" && node.image != null && !string.IsNullOrEmpty(node.image.url))
            {
                return node.image.url;
            }
        }

        return null; // No image found
    }

    /// <summary>
    /// Extracts cost from price range
    /// </summary>
    private float ExtractCost(PriceRange priceRange)
    {
        if (priceRange?.minVariantPrice?.amount == null) return 0f;

        if (float.TryParse(priceRange.minVariantPrice.amount, out float cost))
        {
            return cost;
        }

        return 0f;
    }

    /// <summary>
    /// Downloads an image from URL and assigns it to the VRItem
    /// </summary>
    private IEnumerator DownloadImageCoroutine(string imageUrl, int itemIndex)
    {
        if (string.IsNullOrEmpty(imageUrl) || itemIndex < 0 || itemIndex >= Items.Count)
            yield break;

        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(imageUrl))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(www);
                if (texture != null && itemIndex < Items.Count)
                {
                    VRItem item = Items[itemIndex];
                    item.image = texture;
                    Items[itemIndex] = item;
                    Debug.Log($"Downloaded image for {item.title}");
                }
            }
            else
            {
                Debug.LogWarning($"Failed to download image from {imageUrl}: {www.error}");
            }
        }
    }
    
    /// <summary>
    /// Downloads and instantiates a model from URL using GLTFast
    /// </summary>
    private IEnumerator DownloadModelCoroutine(string url, Transform parent, Vector3 position, float scale, Action<GameObject, bool> onComplete)
    {
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogError("Model URL is null or empty");
            onComplete?.Invoke(null, false);
            yield break;
        }
        
        Debug.Log($"Loading model from URL: {url}");
        
        // Create a container for the model
        GameObject modelContainer = new GameObject("ModelContainer");
        if (parent != null)
        {
            modelContainer.transform.SetParent(parent, false);
        }
        modelContainer.transform.localPosition = position;
        
        // Create a GLTFast importer
        var gltfImport = new GltfImport();
        
        // Start loading the model
        var importTask = gltfImport.Load(url);
        
        // Wait for the import to complete
        while (!importTask.IsCompleted)
        {
            yield return null;
        }
        
        bool success = importTask.Result;
        
        if (success)
        {
            // Create a game object to hold the model with proper scale
            GameObject modelRoot = new GameObject("ModelRoot");
            modelRoot.transform.SetParent(modelContainer.transform);
            modelRoot.transform.localPosition = Vector3.zero;
            modelRoot.transform.localScale = Vector3.one * scale;
            
            // Instantiate the model
            var instantiateTask = gltfImport.InstantiateMainSceneAsync(modelRoot.transform);
            
            // Wait for instantiation to complete
            while (!instantiateTask.IsCompleted)
            {
                yield return null;
            }
            
            success = instantiateTask.Result;
            
            if (success)
            {
                // Return the container with the model
                onComplete?.Invoke(modelContainer, true);
            }
            else
            {
                Debug.LogError("Failed to instantiate model");
                Destroy(modelContainer);
                onComplete?.Invoke(null, false);
            }
        }
        else
        {
            Debug.LogError($"Failed to load model from {url}");
            Destroy(modelContainer);
            onComplete?.Invoke(null, false);
        }
    }

    /// <summary>
    /// Instantiates all the models in the Items list
    /// </summary>
    private IEnumerator InstantiateModelsCoroutine()
    {
        // Create parent if needed
        if (modelsParent == null)
        {
            GameObject parentGO = new GameObject("ShopifyModels");
            modelsParent = parentGO.transform;
            parentGO.transform.position = transform.position + Vector3.forward * 2; // Place models in front of this object
        }
        
        // Track position for layout
        Vector3 nextPosition = Vector3.zero;
        
        // Loop through all items with model URLs
        for (int i = 0; i < Items.Count; i++)
        {
            VRItem item = Items[i];
            
            // Skip if no model URL or already loading
            if (string.IsNullOrEmpty(item.modelUrl) || item.isLoading)
                continue;
                
            Debug.Log($"Loading model {i+1}/{Items.Count}: {item.title}");
            
            // Mark as loading
            item.isLoading = true;
            Items[i] = item; // Update the list item
            
            // Calculate position for this model
            Vector3 modelPosition = nextPosition;
            nextPosition += modelSpacing;
            
            // Load the model
            int itemIndex = i; // Capture for callback
            StartCoroutine(DownloadModelCoroutine(
                item.modelUrl,
                modelsParent,
                modelPosition,
                modelScale,
                (GameObject modelGO, bool success) => {
                    // Update our item with the result
                    VRItem updatedItem = Items[itemIndex];
                    updatedItem.isLoading = false;
                    
                    if (success && modelGO != null)
                    {
                        // Set name
                        modelGO.name = $"Model_{updatedItem.title}";
                        
                        // Store reference to instantiated model
                        updatedItem.instantiatedModel = modelGO;
                    }
                    
                    // Update the item in our list
                    Items[itemIndex] = updatedItem;
                }
            ));
            
            // Wait a bit between model loads to avoid overwhelming the system
            yield return new WaitForSeconds(0.1f);
        }
        
        Debug.Log("Finished instantiating all models");
    }
    
    /// <summary>
    /// Public method to instantiate all models
    /// </summary>
    public void InstantiateModels()
    {
        StartCoroutine(InstantiateModelsCoroutine());
    }
    
    /// <summary>
    /// Public method to instantiate a specific model by index
    /// </summary>
    public void InstantiateModel(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            Debug.LogError($"Invalid model index: {index}. Valid range: 0-{Items.Count-1}");
            return;
        }
        
        StartCoroutine(InstantiateSingleModelCoroutine(index));
    }
    
    /// <summary>
    /// Public method to instantiate a model by item ID
    /// </summary>
    public void InstantiateModelById(string itemId)
    {
        int index = Items.FindIndex(item => item.id == itemId);
        if (index >= 0)
        {
            InstantiateModel(index);
        }
        else
        {
            Debug.LogError($"No item found with ID: {itemId}");
        }
    }
    
    /// <summary>
    /// Instantiates a single model by index
    /// </summary>
    private IEnumerator InstantiateSingleModelCoroutine(int index)
    {
        // Similar to InstantiateModelsCoroutine but for a single model
        VRItem item = Items[index];
        
        // Skip if no model URL or already loading
        if (string.IsNullOrEmpty(item.modelUrl) || item.isLoading)
            yield break;
        
        // Create parent if needed
        if (modelsParent == null)
        {
            GameObject parentGO = new GameObject("ShopifyModels");
            modelsParent = parentGO.transform;
            parentGO.transform.position = transform.position + Vector3.forward * 2; // Place models in front
        }
        
        // Mark as loading
        item.isLoading = true;
        Items[index] = item; // Update the list item
        
        // Calculate position
        Vector3 modelPosition = modelSpacing * index;
        
        Debug.Log($"Loading model: {item.title}");
        
        // Load the model using our internal method
        StartCoroutine(DownloadModelCoroutine(
            item.modelUrl,
            modelsParent,
            modelPosition,
            modelScale,
            (GameObject modelGO, bool success) => {
                // Update our item with the result
                VRItem updatedItem = Items[index];
                updatedItem.isLoading = false;
                
                if (success && modelGO != null)
                {
                    // Set name
                    modelGO.name = $"Model_{updatedItem.title}";
                    
                    // Store reference to instantiated model
                    updatedItem.instantiatedModel = modelGO;
                }
                
                // Update the item in our list
                Items[index] = updatedItem;
            }
        ));
    }
    
    /// <summary>
    /// Preloads all models in the background and stores them for quick instantiation
    /// </summary>
    private IEnumerator PreloadAllModelsCoroutine()
    {
        Debug.Log("Starting background preloading of all models...");
        
        // Create a parent container for preloaded models if needed
        if (modelsParent == null)
        {
            GameObject parentGO = new GameObject("ShopifyModelsContainer");
            modelsParent = parentGO.transform;
        }
        
        // Create a storage area for prefabs - far from the origin
        GameObject prefabStorageArea = new GameObject("PrefabStorageArea");
        prefabStorageArea.transform.SetParent(modelsParent);
        prefabStorageArea.transform.position = prefabStoragePosition;
        
        // Process models in the background using a low priority coroutine
        int totalModels = 0;
        int successfullyLoaded = 0;
        
        // Calculate total models to load
        foreach (var item in Items)
        {
            if (!string.IsNullOrEmpty(item.modelUrl))
                totalModels++;
        }
        
        Debug.Log($"Found {totalModels} models to preload");
        
        // Load each model sequentially to avoid overwhelming the system
        for (int i = 0; i < Items.Count; i++)
        {
            VRItem item = Items[i];
            
            // Skip if no model URL or already loaded
            if (string.IsNullOrEmpty(item.modelUrl) || item.instantiatedModel != null)
                continue;
            
            // Mark as loading
            item.isLoading = true;
            Items[i] = item;
            
            // Calculate offset position within storage area
            Vector3 modelPosition = Vector3.right * (i * 2); // Space them out
            
            // Use a custom yield instruction to manage the loading process
            var loadOperation = new ModelLoadOperation(item.modelUrl, prefabStorageArea.transform, modelPosition, modelScale);
            yield return loadOperation;
            
            if (loadOperation.Success && loadOperation.Result != null)
            {
                // Update the item with the loaded model
                VRItem updatedItem = Items[i];
                updatedItem.isLoading = false;
                updatedItem.instantiatedModel = loadOperation.Result;
                updatedItem.instantiatedModel.name = $"PreloadedModel_{updatedItem.title}";
                
                // Make the object inactive - it's just a template
                updatedItem.instantiatedModel.SetActive(false);
                
                // Update in list
                Items[i] = updatedItem;
                successfullyLoaded++;
                
                Debug.Log($"Preloaded model {successfullyLoaded}/{totalModels}: {updatedItem.title}");
            }
            else
            {
                // Update to not loading anymore
                VRItem updatedItem = Items[i];
                updatedItem.isLoading = false;
                Items[i] = updatedItem;
                
                Debug.LogWarning($"Failed to preload model for {item.title}");
            }
            
            // Small delay between loads to avoid frame drops
            yield return new WaitForSeconds(0.1f);
        }
        
        Debug.Log($"Finished preloading {successfullyLoaded}/{totalModels} models");
    }

    /// <summary>
    /// Custom yield instruction to manage model loading
    /// </summary>
    private class ModelLoadOperation : CustomYieldInstruction
    {
        private bool isComplete = false;
        public bool Success { get; private set; }
        public GameObject Result { get; private set; }
        
        public ModelLoadOperation(string url, Transform parent, Vector3 position, float scale)
        {
            // Start the loading process
            ShopifyProductFetcher.Instance.StartCoroutine(LoadModel(url, parent, position, scale));
        }
        
        private IEnumerator LoadModel(string url, Transform parent, Vector3 position, float scale)
        {
            // Create a container for the model
            GameObject modelContainer = new GameObject("PreloadedModelContainer");
            modelContainer.transform.SetParent(parent, false);
            modelContainer.transform.localPosition = position;
            
            // Create a GLTFast importer
            var gltfImport = new GltfImport();
            
            // Start loading the model
            var importTask = gltfImport.Load(url);
            
            // Wait for the import to complete
            while (!importTask.IsCompleted)
            {
                yield return null;
            }
            
            if (importTask.Result)
            {
                // Create a game object to hold the model with proper scale
                GameObject modelRoot = new GameObject("ModelRoot");
                modelRoot.transform.SetParent(modelContainer.transform);
                modelRoot.transform.localPosition = Vector3.zero;
                modelRoot.transform.localScale = Vector3.one * scale;
                
                // Instantiate the model
                var instantiateTask = gltfImport.InstantiateMainSceneAsync(modelRoot.transform);
                
                // Wait for instantiation to complete
                while (!instantiateTask.IsCompleted)
                {
                    yield return null;
                }
                
                if (instantiateTask.Result)
                {
                    Success = true;
                    Result = modelContainer;
                }
                else
                {
                    Debug.LogError("Failed to instantiate model");
                    GameObject.Destroy(modelContainer);
                }
            }
            else
            {
                Debug.LogError($"Failed to load model from {url}");
                GameObject.Destroy(modelContainer);
            }
            
            isComplete = true;
        }
        
        public override bool keepWaiting => !isComplete;
    }

    // Make the class accessible via a singleton pattern
    public static ShopifyProductFetcher Instance { get; private set; }
}
