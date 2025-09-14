using System.Collections.Generic;
using UnityEngine;

public class CheckoutTest : MonoBehaviour
{
    public CheckoutManager checkoutManager;

    void Start()
    {
        // Replace with real Shopify product IDs (gid://shopify/Product/...)
        var testIds = new List<string>
        {
            "gid://shopify/Product/10090772857016",
            "gid://shopify/Product/10090755555512"
        };

        checkoutManager.GetCartTotal(testIds, (total) =>
        {
            Debug.Log("🛒 Cart Total: " + total);
        });

        // Cart: { "<Variant_ID>", <Quantity> }
        var cart = new Dictionary<string, int>
        {
            { "10090776461496", 1 },
            { "10090772857016", 1 },
            { "10090755981496", 1 },
            { "10090755915960", 1 },
            { "10090755621048", 1 },
            { "10090755555512", 1 },
            { "10090755424440", 1 },
            { "10090755293368", 1 },
            { "10090755260600", 1 },
            { "10090755227832", 1 },
            { "10090754769080", 1 },
            { "10090754474168", 1 },
            { "10090754244792", 1 },
            { "10090753163448", 1 },
            { "10090753065144", 1 },
            { "10090752704696", 1 },
            { "10090693427384", 1 },
            { "10090527752376", 1 }
        };

        checkoutManager.OpenShopifyCheckout(cart);
    }
}