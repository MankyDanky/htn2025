using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

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
    }

    // ---------- Unity ----------
    private void Start()
    {
        StartCoroutine(FetchProductsCoroutine());
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
                    modelUrl = modelUrl
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
}
