using UnityEngine;

public class SpawnTest : MonoBehaviour
{
    [SerializeField]
    private GameObject prefab;
    
    [SerializeField]
    private Transform forwardIndicator;
    
    [SerializeField]
    private float speed;

    // Update is called once per frame
    void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger))
        {
            GameObject spawned = Instantiate(prefab, transform.position, transform.rotation);
            Rigidbody rb = spawned.AddComponent<Rigidbody>();
            rb.linearVelocity = transform.forward * speed;
        }

        forwardIndicator.transform.position = transform.position + transform.forward * 1.2f;
    }
}
