using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class CarController : MonoBehaviour
{
    public float moveSpeed = 5f;

    private Rigidbody2D rb;
    private Vector2 movement;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        float horizontal = Input.GetAxisRaw("Horizontal"); // A/D or Left/Right
        float vertical = Input.GetAxisRaw("Vertical");     // W/S or Up/Down

        // Prioritize vertical movement over horizontal if both pressed
        if (vertical != 0)
        {
            movement = new Vector2(0, vertical).normalized;

            if (vertical > 0)
                transform.rotation = Quaternion.Euler(0, 0, 90);   // up
            else
                transform.rotation = Quaternion.Euler(0, 0, 270);  // down
        }
        else if (horizontal != 0)
        {
            movement = new Vector2(horizontal, 0).normalized;

            if (horizontal > 0)
                transform.rotation = Quaternion.Euler(0, 0, 0);    // right
            else
                transform.rotation = Quaternion.Euler(0, 0, 180);  // left
        }
        else
        {
            movement = Vector2.zero; // stop when no keys pressed
        }
    }

    void FixedUpdate()
    {
        rb.linearVelocity = movement * moveSpeed;
    }
}
