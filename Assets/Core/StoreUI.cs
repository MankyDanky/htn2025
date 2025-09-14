using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class StoreUI : MonoBehaviour
{
    [SerializeField] private ShopifyProductFetcher fetcher;

    [SerializeField] private Transform paneParent;
    
    [SerializeField] private GameObject panePrefab;
    
    [SerializeField] private List<ItemPane> UIItems;

    [SerializeField] private int currentItem = -1;

    private bool menuOpen;

    [SerializeField] private Transform handTest;

    [SerializeField]
    private Material testMat;

    [SerializeField]
    private RectTransform plusIcon;

    [SerializeField] private Animator menuAnimator;

    [SerializeField] private Toggle toggle;
    
    public void AddItem(ShopifyProductFetcher.VRItem item)
    {
        // Create Pane, Set Store UI, Set Item
        ItemPane newPane = Instantiate(panePrefab, paneParent).GetComponent<ItemPane>();
        newPane.SetStoreUI(this);
        newPane.SetVrItem(item);
        UIItems.Add(newPane);
    }

    public void SetCurrentItem(ItemPane itm)
    {
        SetCurrentItem(UIItems.IndexOf(itm));
    }

    public void SetCurrentItem(int index)
    {
        if (index < 0 || index >= UIItems.Count) return;
        
        float H, S, V;
        Color.RGBToHSV(testMat.color, out H, out S, out V);
        testMat.color = Color.HSVToRGB((H + 50 / 360.0f) % 1.0f, S, V);
        
        /* Later...
        GameObject newModel = Instantiate(UIItems[index].item.instantiatedModel);
        newModel.transform.position = handTest.transform.position;
        newModel.transform.parent = handTest;
        */
    }

    void Update()
    {
        plusIcon.localRotation = Quaternion.Slerp(plusIcon.localRotation, Quaternion.AngleAxis(menuOpen ? 45.0f : 0.0f, Vector3.forward), Time.deltaTime * 10.0f);

        if (Keyboard.current[Key.Space].wasPressedThisFrame)
        {
            toggle.isOn = !toggle.isOn;
            UpdateMenu();
        }
    }

    public void UpdateMenu()
    {
        if (toggle.isOn) OpenMenu();
        else CloseMenu();
    }

    public void OpenMenu()
    {
        menuOpen = true;
        menuAnimator.Play("Hi");
    }
    
    public void CloseMenu()
    {
        menuOpen = false;
        menuAnimator.Play("Bye");
    }
}
