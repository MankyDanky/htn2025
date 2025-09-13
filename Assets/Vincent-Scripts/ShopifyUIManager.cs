using UnityEngine;
using UnityEngineUI;
using TMPro;

public class ShopifyUIManager : MonoBehaviour
{
    // ShopifyProductFetcher Object (to get Items from):
    public ShopifyProductFetcher fetcher; // Assign in Inspector
    public Transform gridParent; // Assign to UI Grid Content Object
    public GameObject productCardPrefab; // Assign to Card Prefab

    void Start()
    {
        // Wait for fetcher to finish loading, then populate UI fields:
        StartCoroutine(WaitAndPopulate());
    }

    private IEnumerator WaitAndUpdate()
    {
        // Wait until fetcher.Items is populated:
        while (fetcher.Items.Count == 0) yield return null;
        PopulateUI();
    }

    public void PopulateUI()
    {
        foreach (Transform child in gridParent) Destroy(child.gameObject);

        foreach (var item in fetcher.Items)
        {
            var cardGO = Instantiate(productCardPrefab, gridParent);
            var card = cardGO.GetComponent<ProductCardUI>();
            card.Init(item, fetcher);
        }
    }
}