using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class ClickToMove : MonoBehaviour
{
    public float speed = 6f;
    public LayerMask roadLayer;

    private Rigidbody2D rb;
    private Vector2 targetPosition;
    private bool hasTarget = false;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos =
                Camera.main.ScreenToWorldPoint(Input.mousePosition);

            Collider2D road =
                Physics2D.OverlapPoint(mousePos, roadLayer);

            if (road != null)
            {
                targetPosition = mousePos;
                hasTarget = true;
            }
        }
    }

    void FixedUpdate()
    {
        if (!hasTarget)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 direction =
            (targetPosition - rb.position).normalized;

        Vector2 nextPosition =
            rb.position + direction * speed * Time.fixedDeltaTime;

        Collider2D roadAtNextPosition =
            Physics2D.OverlapPoint(nextPosition, roadLayer);

        if (roadAtNextPosition == null)
        {
            rb.linearVelocity = Vector2.zero;
            hasTarget = false;

            Debug.Log("Road ended.");
            return;
        }

        rb.linearVelocity = direction * speed;

        float angle =
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        rb.MoveRotation(angle - 90f);

        if (Vector2.Distance(rb.position, targetPosition) < 0.2f)
        {
            rb.linearVelocity = Vector2.zero;
            hasTarget = false;
        }
    }
}