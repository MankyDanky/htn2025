using System;
using System.Text;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class CheckoutManager : MonoBehaviour
{
    [Header("Shopify API")]
    public string endpoint;
    public string storeFrontAccessToken;
    public string shopDomain = "htn2025.myshopify.com";

    public void GetCartTotal(List<string> productIds, System.Action<string> onTotalReady)
    {
        StartCoroutine(GetCartTotalCoroutine(productIds, onTotalReady));
    }

    private IEnumerator GetCartTotalCoroutine(List<string> productIds, System.Action<string> onTotalReady)
    {
        // Build GraphQL query to fetch prices for all IDs:
        string idsQuery = string.Join(",", productIds.ConvertAll(id => $"\"{id}\""));
        string query = $@"
        query {{
            nodes(ids: [{idsQuery}]) {{
                ... on Product {{
                    id
                    priceRange {{
                        minVariantPrice {{
                            amount
                        }}
                    }}
                }}
            }}
        }}";

        // Serialize query body
        var body = new GraphQLBody { query = query };
        string json = JsonUtility.ToJson(body);

        using (var req = new UnityWebRequest(endpoint, "POST"))
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(payload);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-Shopify-Storefront-Access-Token", storeFrontAccessToken);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Shopify request failed: {req.error}\n{req.downloadHandler.text}");
                onTotalReady?.Invoke("Error");
                yield break;
            }

            // Parse response and sum prices:
            string response = req.downloadHandler.text;
            float total = 0f;

            foreach (var id in productIds)
            {
                string priceKey = $"\"id\":\"{id}\"";
                int idx = response.IndexOf(priceKey);
                if (idx >= 0)
                {
                    int priceIdx = response.IndexOf("\"amount\":\"", idx);
                    if (priceIdx >= 0)
                    {
                        int start = priceIdx + "\"amount\":\"".Length;
                        int end = response.IndexOf("\"", start);
                        string priceStr = response.Substring(start, end - start);
                        if (float.TryParse(priceStr, out float price)) total += price;
                    }
                }
            }
            onTotalReady?.Invoke($"${total:F2}");
        }
    }

    [Serializable]
    private class GraphQLBody
    {
        public string query;
    }

    public void OpenShopifyCheckout(Dictionary<string, int> variantIdToQuantity)
    {
        var cartItems = new List<string>();
        foreach (var kvp in variantIdToQuantity)
        {
            cartItems.Add($"{kvp.Key}:{kvp.Value}");
        }
        string cartString = string.Join(",", cartItems);

        // Building full URL:
        string url = $"https://{shopDomain}/cart/{cartString}";

        // Open URL in default browser:
        Application.OpenURL(url);
    }

    private void Awake()
    {
        var envPath = Path.Combine(Application.dataPath, "..", ".env");
        if (File.Exists(envPath))
        {
            var lines = File.ReadAllLines(envPath);
            foreach (var line in lines)
            {
                if (line.StartsWith("SHOPIFY_STOREFRONT_ACCESS_TOKEN="))
                    storeFrontAccessToken = line.Substring("SHOPIFY_STOREFRONT_ACCESS_TOKEN=".Length);
                if (line.StartsWith("SHOPIFY_ENDPOINT="))
                    endpoint = line.Substring("SHOPIFY_ENDPOINT=".Length);
            }
        }

        // On checkout the default browser will pop up with the Shopify checkout page:

    }
}