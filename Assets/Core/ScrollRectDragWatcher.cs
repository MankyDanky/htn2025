using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(ScrollRect))]
public class ScrollRectDragWatcher : MonoBehaviour, IBeginDragHandler, IEndDragHandler {
    public bool IsScrolling { get; private set; }
    public float velocityStop = 30f;
    public float checkInterval = 0.02f;
    ScrollRect sr; Coroutine settle;
    void Awake(){ sr = GetComponent<ScrollRect>(); }
    public void OnBeginDrag(PointerEventData e){
        IsScrolling = true;
        if (settle!=null){ StopCoroutine(settle); settle=null; }
    }
    public void OnEndDrag(PointerEventData e){
        if (settle!=null) StopCoroutine(settle);
        settle = StartCoroutine(WaitForSettle());
    }
    IEnumerator WaitForSettle(){
        while (sr && sr.velocity.sqrMagnitude > velocityStop*velocityStop)
            yield return new WaitForSeconds(checkInterval);
        IsScrolling = false; settle = null;
    }
}