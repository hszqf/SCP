using UnityEngine;

public class ModalDimmerHandle : MonoBehaviour
{
    [SerializeField] private GameObject dimmerRoot;

    public void SetDimmerActive(bool active)
    {
        if (dimmerRoot == null) return;

        dimmerRoot.SetActive(active);

        var cg = dimmerRoot.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.blocksRaycasts = active;
        }
    }
}
