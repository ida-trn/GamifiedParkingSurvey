using UnityEngine;

[ExecuteAlways]
public class TextSortingOrder : MonoBehaviour
{
    [SerializeField] private string sortingLayerName = "UI";
    [SerializeField] private int orderInLayer = 310;

    private void OnEnable()
    {
        ApplySorting();
    }

    private void OnValidate()
    {
        ApplySorting();
    }

    private void ApplySorting()
    {
        Renderer rend = GetComponent<Renderer>();

        if (rend != null)
        {
            rend.sortingLayerName = sortingLayerName;
            rend.sortingOrder = orderInLayer;
        }
    }
}