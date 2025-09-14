using Meta.XR.MRUtilityKit;
using UnityEngine;
using System.Threading.Tasks;

public class ScanPrompter : MonoBehaviour
{
    [SerializeField]
    private GameObject effectMeshPrefab;
    
    async void Start()
    {
        // Force the system scan UI
        OVRScene.RequestSpaceSetup();

        // Then wait for MRUK to finish loading the scene
        var result = await MRUK.Instance.LoadSceneFromDevice(
            requestSceneCaptureIfNoDataFound: false,
            removeMissingRooms: true
        );

        if (result == MRUK.LoadDeviceResult.Success)
        {
            Debug.Log("✅ Scan complete, scene loaded with " 
                      + MRUK.Instance.GetCurrentRoom().Anchors.Count + " anchors.");
            OnSceneReady();
        }
        else
        {
            Debug.LogWarning("❌ Scan failed or was cancelled: " + result);
        }
    }

    private void OnSceneReady()
    {
        var room = MRUK.Instance.GetCurrentRoom();
        Debug.Log("Current room has " + room.Anchors.Count + " anchors");
        
        Instantiate(effectMeshPrefab);
    }
}

