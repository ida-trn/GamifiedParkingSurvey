using UnityEngine;

// Attach this to the Main Camera
public class CameraFollow : MonoBehaviour
{
    public Transform target;   // the car
    public float smoothSpeed = 5f;  // how quickly the camera catches up

    void LateUpdate()
    {
        if (target == null) return;

        // Desired camera position (same X/Y as the target, keep current Z)
        Vector3 desiredPosition = new Vector3(
            target.position.x,
            target.position.y,
            transform.position.z
        );

        // Smoothly move camera towards the target
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
    }
}
