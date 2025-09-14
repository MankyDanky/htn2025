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
            Debug.Log("Cart Total: " + total);
        });
    }
}