using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.InputSystem; // Add this at the top

public class Checkout : MonoBehaviour
{
    [SerializeField] private string endpoint;
    [SerializeField] private string storefrontAccessToken;
    
    void Start()
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

    void Update()
    {
        // Test checkout with first 3 items when 'L' key is pressed (Input System)
        if (Keyboard.current.lKey.wasPressedThisFrame)
        {
            var fetcher = ShopifyProductFetcher.Instance;
            if (fetcher != null && fetcher.Items != null && fetcher.Items.Count >= 3)
            {
                var testItems = fetcher.Items.GetRange(0, 3);
                RedirectToCheckout(testItems);
            }
            else
            {
                Debug.LogWarning("Not enough items to test checkout (need at least 3)");
            }
        }
    }

    public void RedirectToCheckout(List<ShopifyProductFetcher.VRItem> items)
    {
        if (items == null || items.Count == 0)
        {
            Debug.LogWarning("Cannot checkout with empty cart");
            return;
        }

        StartCoroutine(CreateCheckoutCoroutine(items));
    }

    private IEnumerator CreateCheckoutCoroutine(List<ShopifyProductFetcher.VRItem> items)
    {
        // First fetch proper variant IDs for all products
        Dictionary<string, string> variantIds = new Dictionary<string, string>();
        
        // Batch fetch variant IDs
        yield return StartCoroutine(FetchVariantIdsCoroutine(items, variantIds));
        
        if (variantIds.Count == 0)
        {
            Debug.LogError("Failed to fetch variant IDs for products");
            yield break;
        }
        
        // Now create the cart with proper variant IDs
        string lineItemsJson = BuildLineItemsJsonWithVariants(items, variantIds);
        
        // GraphQL mutation for cart creation
        string mutation = @"
        mutation CartCreate($input: CartInput!) {
          cartCreate(input: $input) {
            cart {
              checkoutUrl
            }
            userErrors {
              field
              message
            }
          }
        }";
        
        // Build the complete request body
        string jsonBody = $"{{\"query\":\"{EscapeJson(mutation)}\",\"variables\":{{\"input\":{{\"lines\":{lineItemsJson}}}}}}}";
        
        using (var request = new UnityWebRequest(endpoint, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-Shopify-Storefront-Access-Token", storefrontAccessToken);
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseText = request.downloadHandler.text;
                Debug.Log($"Cart response: {responseText}");
                
                // Parse response to get the checkout URL
                string webUrl = ExtractCheckoutUrl(responseText);
                
                if (!string.IsNullOrEmpty(webUrl))
                {
                    Debug.Log($"Opening checkout URL: {webUrl}");
                    Application.OpenURL(webUrl);
                }
                else
                {
                    Debug.LogError("Failed to extract checkout URL from response");
                }
            }
            else
            {
                Debug.LogError($"Checkout creation failed: {request.error}\nResponse: {request.downloadHandler.text}");
            }
        }
    }

    private IEnumerator FetchVariantIdsCoroutine(List<ShopifyProductFetcher.VRItem> items, Dictionary<string, string> variantIds)
    {
        // Build list of product IDs to query
        List<string> productIds = new List<string>();
        foreach (var item in items)
        {
            string productId = item.id;
            if (!variantIds.ContainsKey(productId) && !string.IsNullOrEmpty(productId))
            {
                productIds.Add(productId);
            }
        }
        
        if (productIds.Count == 0)
        {
            yield break;
        }
        
        // Build a GraphQL query to fetch variants for all products at once
        StringBuilder queryBuilder = new StringBuilder();
        queryBuilder.Append("query {");
        
        for (int i = 0; i < productIds.Count; i++)
        {
            string productId = productIds[i];
            // Extract the numeric part of the product ID
            string productGid = ExtractIdFromGid(productId);
            
            queryBuilder.Append($"p{i}: product(id: \"{productId}\") {{");
            queryBuilder.Append("id variants(first: 1) { edges { node { id } } }");
            queryBuilder.Append("}");
        }
        
        queryBuilder.Append("}");
        
        string query = queryBuilder.ToString();
        string jsonBody = $"{{\"query\":\"{EscapeJson(query)}\"}}";
        
        using (var request = new UnityWebRequest(endpoint, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-Shopify-Storefront-Access-Token", storefrontAccessToken);
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseText = request.downloadHandler.text;
                Debug.Log($"Variant fetch response: {responseText}");
                
                // Parse the response to extract variant IDs
                for (int i = 0; i < productIds.Count; i++)
                {
                    string productId = productIds[i];
                    string searchText = $"\"p{i}\":{{\"id\":\"{productId}\",\"variants\":{{\"edges\":[{{\"node\":{{\"id\":\"";
                    int startIndex = responseText.IndexOf(searchText);
                    
                    if (startIndex >= 0)
                    {
                        startIndex += searchText.Length;
                        int endIndex = responseText.IndexOf("\"", startIndex);
                        if (endIndex > startIndex)
                        {
                            string variantId = responseText.Substring(startIndex, endIndex - startIndex);
                            variantIds[productId] = variantId;
                        }
                    }
                }
            }
            else
            {
                Debug.LogError($"Variant fetch failed: {request.error}\nResponse: {request.downloadHandler.text}");
            }
        }
    }

    private string ExtractIdFromGid(string gid)
    {
        // Extract the numeric part of a Shopify GID
        int lastSlash = gid.LastIndexOf('/');
        if (lastSlash != -1 && lastSlash < gid.Length - 1)
        {
            return gid.Substring(lastSlash + 1);
        }
        return gid;
    }

    private string BuildLineItemsJsonWithVariants(List<ShopifyProductFetcher.VRItem> items, Dictionary<string, string> variantIds)
    {
        StringBuilder sb = new StringBuilder("[");
        for (int i = 0; i < items.Count; i++)
        {
            string productId = items[i].id;
            string variantId = variantIds.ContainsKey(productId) ? variantIds[productId] : ConvertToVariantId(productId);
            
            sb.Append($"{{\"merchandiseId\":\"{variantId}\",\"quantity\":1}}");
            if (i < items.Count - 1)
                sb.Append(",");
        }
        sb.Append("]");
        return sb.ToString();
    }
    
    private string BuildLineItemsJson(List<ShopifyProductFetcher.VRItem> items)
    {
        StringBuilder sb = new StringBuilder("[");
        for (int i = 0; i < items.Count; i++)
        {
            // Convert Product ID to ProductVariant ID format
            // This assumes the first variant of each product
            string productId = items[i].id;
            string variantId = ConvertToVariantId(productId);
            
            sb.Append($"{{\"merchandiseId\":\"{variantId}\",\"quantity\":1}}");
            if (i < items.Count - 1)
                sb.Append(",");
        }
        sb.Append("]");
        return sb.ToString();
    }
    
    // Helper to convert Product ID to Variant ID format
    private string ConvertToVariantId(string productId)
    {
        // Check if it's already a variant ID
        if (productId.Contains("/ProductVariant/"))
            return productId;
            
        // If it's a product ID, extract the numeric part and create a variant ID
        if (productId.Contains("/Product/"))
        {
            // Extract the product number
            int lastSlash = productId.LastIndexOf('/');
            if (lastSlash != -1 && lastSlash < productId.Length - 1)
            {
                string productNumber = productId.Substring(lastSlash + 1);
                // In a real application, you should query the API to get the actual variant ID
                // This is a temporary solution that assumes the first variant ID is the same as product ID
                return $"gid://shopify/ProductVariant/{productNumber}";
            }
        }
        
        // If nothing else works, return the original ID
        return productId;
    }
    
    private string ExtractCheckoutUrl(string jsonResponse)
    {
        // Look for \"checkoutUrl\":\"...\" in the response
        int urlIndex = jsonResponse.IndexOf("\"checkoutUrl\":\"");
        if (urlIndex >= 0)
        {
            int start = urlIndex + 15;
            int end = jsonResponse.IndexOf("\"", start);
            if (end > start)
                return jsonResponse.Substring(start, end - start);
        }
        return null;
    }
    
    private string EscapeJson(string json)
    {
        return json.Replace("\"", "\\\"")
                  .Replace("\n", "\\n")
                  .Replace("\r", "\\r")
                  .Replace("\t", "\\t");
    }
}