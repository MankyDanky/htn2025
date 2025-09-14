using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ProductCardUI : MonoBehaviour
{
    // Public Fields:
    public Image thumbnail;
    public TMP_Text titleText;
    public Button viewButton;

    // Private Fields:
    private ShopifyProductFetcher.VRItem item;
    private ShopifyProductFetcher fetcher;
    private int itemIndex;

    public void Init(ShopifyProductFetcher.VRItem data, ShopifyProductFetcher fetcherRef)
    {
        item = data;
        fetcher = fetcherRef;
        titleText.text = item.title;
        // Load thumbail
        // viewButton.onClick.AddListner(OnViewClicked);
    }

    private void OnViewClicked()
    {
        int index = fetcher.Items.IndexOf(item);
        fetcher.InstantiateModel(index);
    }
}