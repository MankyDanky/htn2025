using UnityEngine;

public class UIPositionMan : MonoBehaviour
{
    [SerializeField] private Transform ui;
    [SerializeField] private float distance;
    [SerializeField] private Transform head;

    public void Update()
    {
        Vector3 vec = (transform.position - head.position);
        vec = new Vector3(vec.x, 0.0f, vec.z).normalized;
        Vector3 newPos = head.position + vec * distance;
        transform.position = Vector3.Lerp(transform.position, head.position + head.forward * distance, Time.deltaTime * 20.0f);
        transform.rotation = Quaternion.LookRotation(vec, Vector3.up);
    }
}
