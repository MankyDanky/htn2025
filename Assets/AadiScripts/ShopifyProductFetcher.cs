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

    // GraphQL query: products with id/title/description and media (Model3d + Image/Video)
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
        public GameObject instantiatedModel; // Reference to the instantiated model in the scene
        public bool isLoading; // Track loading state
    }

    // ---------- Unity ----------
    private void Start()
    {
        StartCoroutine(FetchProductsCoroutine());
    }
    
    private void Update()
    {
        // Press Space key to instantiate the first model in the list
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && Items.Count > 0)
        {
            Debug.Log("Space key pressed. Instantiating first model.");
            InstantiateModel(0);
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

                var item = new VRItem
                {
                    id = p.id,
                    title = p.title,
                    description = string.IsNullOrEmpty(p.description) ? "" : p.description,
                    modelUrl = modelUrl,
                    isLoading = false
                };
                Items.Add(item);
            }

            Debug.Log($"Fetched {Items.Count} product(s). Example[0]: " +
                      (Items.Count > 0
                          ? $"{Items[0].title} | {Items[0].id} | modelUrl={(string.IsNullOrEmpty(Items[0].modelUrl) ? "N/A" : Items[0].modelUrl)}"
                          : "N/A"));
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
}
