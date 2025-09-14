using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ItemPane : MonoBehaviour
{
    private StoreUI storeUI;
    
    [SerializeField]
    private UICover imgCover;
    
    [SerializeField]
    private RawImage imgDisplay;
    
    [SerializeField]
    private TextMeshProUGUI nameLabel;
    
    [SerializeField]
    private TextMeshProUGUI costLabel;

    [field: SerializeField]
    public ShopifyProductFetcher.VRItem item { get; private set; }

    public void OnSelect()
    {
        //Debug.Log($"TODO: bind this to setting something... {item.title}");
        storeUI.SetCurrentItem(this);
    }

    public void SetStoreUI(StoreUI ui)
    {
        storeUI = ui;
    }
    
    public void SetVrItem(ShopifyProductFetcher.VRItem newItem)
    {
        item = newItem;
        imgDisplay.texture = item.image;
        nameLabel.text = item.title;
        costLabel.text = string.Format(new System.Globalization.CultureInfo("en-CA"), "{0:C}", newItem.cost);
    }
}
