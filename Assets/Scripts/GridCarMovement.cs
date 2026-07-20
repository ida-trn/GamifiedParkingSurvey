using UnityEngine;
using System.Collections;

public class GridCarMovement : MonoBehaviour
{
    public float moveSpeed = 5f;     // movement speed
    public float gridSize = 1f;      // distance moved per step (1 unit grid)
    private bool isMoving = false;   // prevents new input during movement
    private Vector3 targetPosition;  // where the car is going next

    void Start()
    {
        // Start at nearest grid cell
        targetPosition = RoundToGrid(transform.position);
        transform.position = targetPosition;
    }

    void Update()
    {
        if (isMoving) return;   // ignore input during movement

        Vector3 direction = Vector3.zero;

        if (Input.GetKeyDown(KeyCode.W))
        {
            direction = Vector3.up;
            transform.rotation = Quaternion.Euler(0, 0, 90);
        }
        else if (Input.GetKeyDown(KeyCode.S))
        {
            direction = Vector3.down;
            transform.rotation = Quaternion.Euler(0, 0, 270);
        }
        else if (Input.GetKeyDown(KeyCode.A))
        {
            direction = Vector3.left;
            transform.rotation = Quaternion.Euler(0, 0, 180);
        }
        else if (Input.GetKeyDown(KeyCode.D))
        {
            direction = Vector3.right;
            transform.rotation = Quaternion.Euler(0, 0, 0);
        }

        if (direction != Vector3.zero)
        {
            StartCoroutine(MoveStep(direction));
        }
    }

    IEnumerator MoveStep(Vector3 direction)
    {
        isMoving = true;

        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + direction * gridSize;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * moveSpeed;
            transform.position = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }

        // Snap cleanly to final grid point
        transform.position = RoundToGrid(endPos);

        isMoving = false;
    }

    // Force car to always sit on clean grid coordinates
    Vector3 RoundToGrid(Vector3 position)
    {
        float x = Mathf.Round(position.x / gridSize) * gridSize;
        float y = Mathf.Round(position.y / gridSize) * gridSize;
        return new Vector3(x, y, position.z);
    }
}
