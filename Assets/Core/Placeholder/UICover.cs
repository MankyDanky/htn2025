using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class UICover : MonoBehaviour
{
    [SerializeField]
    private RawImage image;

    [SerializeField]
    private AspectRatioFitter f;
    
    void Awake() { UpdateCover(); }

    [ContextMenu("Update Cover Now")]
    void UpdateCover()
    {
        if (f == null || image == null) return;
        f.aspectRatio = (float)image.texture.width / image.texture.height;
    }
}