using UnityEngine;

[ExecuteAlways]
public class KeepTextUnstretched : MonoBehaviour
{
    [Header("Canvas world size")]
    public float targetWorldScale = 0.03f;

    [Header("Label rotation")]
    public float rotationZ = 270f;

    void LateUpdate()
    {
        if (transform.parent == null) return;

        Vector3 p = transform.parent.lossyScale;

        float px = Mathf.Approximately(p.x, 0f) ? 1f : Mathf.Abs(p.x);
        float py = Mathf.Approximately(p.y, 0f) ? 1f : Mathf.Abs(p.y);

        transform.localScale = new Vector3(
            targetWorldScale / px,
            targetWorldScale / py,
            1f
        );

        transform.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
    }
}