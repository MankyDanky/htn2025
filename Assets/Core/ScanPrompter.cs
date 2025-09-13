using Meta.XR.MRUtilityKit;
using UnityEngine;
using System.Threading.Tasks;

public class ScanPrompter : MonoBehaviour
{
    [SerializeField]
    private GameObject effectMeshPrefab;
    
    async void Start()
    {
        Debug.Log("Loading scene from device...");

        var result = await MRUK.Instance.LoadSceneFromDevice(
            requestSceneCaptureIfNoDataFound: true,
            removeMissingRooms: true
        );

        if (result == MRUK.LoadDeviceResult.Success)
        {
            Debug.Log("Scene loaded successfully!");
            OnSceneReady();
        }
        else
        {
            Debug.LogWarning("Scene load failed: " + result);
        }
    }

    private void OnSceneReady()
    {
        var room = MRUK.Instance.GetCurrentRoom();
        Debug.Log("Current room has " + room.Anchors.Count + " anchors");
        
        Instantiate(effectMeshPrefab);
    }
}

