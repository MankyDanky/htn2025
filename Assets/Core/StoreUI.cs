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

    [SerializeField] private bool placing;
    [SerializeField] private GameObject placeTarget;
    [SerializeField] private Transform handTarget;
    [SerializeField] private LayerMask rayMask;
    
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
        currentItem = UIItems.IndexOf(itm);
        SetCurrentItem(currentItem);
        placing = true;
        CloseMenu();
    }

    public void SetCurrentItem(int index)
    {
        if (index < 0 || index >= UIItems.Count) return;
        currentItem = index;
        
        // Shift HSV for indication
        float H, S, V;
        Color.RGBToHSV(testMat.color, out H, out S, out V);
        testMat.color = Color.HSVToRGB((H + 50 / 360.0f) % 1.0f, S, V);

        GameObject newObj = fetcher.Items[currentItem].instantiatedModel;
        newObj.SetActive(true);
        Transform newTransform = fetcher.Items[currentItem].instantiatedModel.transform;
        newTransform.parent = placeTarget.transform;
        
        var filter = newObj.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            Bounds localBounds = filter.sharedMesh.bounds;
            newTransform.localPosition = Vector3.up * localBounds.extents.y;
        }
        

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

        if (placing)
        {
            placeTarget.layer = 6;
            if (currentItem >= 0 && currentItem < UIItems.Count)
            {
                RaycastHit hit;
                if (Physics.Raycast(handTarget.position, handTarget.transform.forward, out hit, 10.0f, rayMask))
                {
                    placeTarget.transform.position = hit.point;
                    placeTarget.transform.up = hit.normal;
                }
            }
        }
        
        if (OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger))
        {
            placing = false;
            if (currentItem >= 0 && currentItem < UIItems.Count)
            {
                Transform newTransform = fetcher.Items[currentItem].instantiatedModel.transform;
                newTransform.parent = null;
            }
            /*
            GameObject spawned = Instantiate(prefab, transform.position, transform.rotation);
            Rigidbody rb = spawned.AddComponent<Rigidbody>();
            rb.linearVelocity = transform.forward * speed;
            */
        }
    }

    public void UpdateMenu()
    {
        if (toggle.isOn) OpenMenu();
        else CloseMenu();
    }

    public void OpenMenu()
    {
        currentItem = -1;
        menuOpen = true;
        menuAnimator.Play("Hi");
    }
    
    public void CloseMenu()
    {
        menuOpen = false;
        menuAnimator.Play("Bye");
    }
}
