using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController2D : MonoBehaviour
{
    // Horizontal move speed
    public float moveSpeed = 6f;
    // Jump power force
    public float jumpForce = 10f;
    // Ground check distance
    public float groundRay = 0.7f;
    // Ground ray y offset
    public float rayOriginYOffset = 0.1f;
    // Ground collision mask
    public LayerMask groundMask;

    Rigidbody2D rb;
    bool controlEnabled = true;

    // Cache rigidbody reference
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    // Per frame input
    void Update()
    {
        if (!controlEnabled) return;

        float x = Input.GetAxisRaw("Horizontal");
        Vector2 lv = rb.linearVelocity;
        lv.x = x * moveSpeed;
        rb.linearVelocity = lv;

        if (x != 0f)
        {
            var ls = transform.localScale;
            ls.x = Mathf.Sign(x) * Mathf.Abs(ls.x);
            transform.localScale = ls;
        }

        if (Input.GetKeyDown(KeyCode.Space) && IsGrounded())
        {
            Vector2 v = rb.linearVelocity;
            v.y = 0f;
            rb.linearVelocity = v;
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
        }
    }

    // Simple grounded check
    bool IsGrounded()
    {
        Vector2 o = (Vector2)transform.position + Vector2.down * rayOriginYOffset;
        var hit = Physics2D.Raycast(o, Vector2.down, groundRay, groundMask);
        return hit.collider != null;
    }

    // Called when caught
    public void OnCaught()
    {
        controlEnabled = false;
        rb.linearVelocity = Vector2.zero;
        Debug.Log("Player caught by guard");
    }
}
