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

    // Calculates the total cost for a list of product ID's:
    public void GetCartTotal(List<string> productIds, System.Action<string> onTotalReady)
    {
        StartCoroutine(GetCartTotalCoroutine(productIds, onTotalReady));
    }

    // GetCartTotalCoroutine:
    private IEnumerator GetCartTotalCoroutine(List<string> productIds, System.Action<string> onTotalReady)
    {
        // Build GraphQL query to fetch prices for all ID's:
        string idsQuery = string.Join(",", productIds.ConvertAll(idsQuery => $"\"{id}"));
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

        var body = new { query = query };
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

            // Simple JSON parsing:
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
}