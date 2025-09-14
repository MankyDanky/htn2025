using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using System.Collections;

[RequireComponent(typeof(RectTransform))]
public class LongPressButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
     [Tooltip("How long (in seconds) the user must hold to trigger OnLongPress.")]
     public float holdTime = 0.5f;

    public UnityEvent OnLongPress;

    private bool isDragging = false;
    private Coroutine holdRoutine;

    public void OnPointerDown(PointerEventData eventData)
    {
        isDragging = false;
        holdRoutine = StartCoroutine(HoldCheck());
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        CancelHold();
    }

    public void OnDrag(PointerEventData eventData)
    {
        // If user starts scrolling/dragging, cancel the long press
        isDragging = true;
        CancelHold();
    }

    private IEnumerator HoldCheck()
    {
        float elapsed = 0f;
        while (elapsed < holdTime)
        {
            if (isDragging)
                yield break;

            elapsed += Time.deltaTime;
            yield return null;
        }

        OnLongPress?.Invoke();
        Debug.Log("HELLO !!!!!!!!!!!!!!");
    }

    private void CancelHold()
    {
        if (holdRoutine != null)
        {
            StopCoroutine(holdRoutine);
            holdRoutine = null;
        }
    }
}