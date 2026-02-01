using UnityEngine;

public class ModalDimmerHandle : MonoBehaviour
{
    [SerializeField] private GameObject dimmerRoot;

    public void SetDimmerActive(bool active)
    {
        if (dimmerRoot != null)
        {
            dimmerRoot.SetActive(active);
        }
    }
}
