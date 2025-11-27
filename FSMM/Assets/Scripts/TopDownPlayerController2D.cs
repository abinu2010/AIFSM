using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class TopDownPlayerController2D : MonoBehaviour
{
    // Movement speed units
    public float moveSpeed = 6f;
    // Ground mask reference
    public LayerMask groundMask;

    // Gizmos toggles here
    public bool showGizmos = true;
    public float footstepPreview = 6f;

    Rigidbody2D rb;

    // Cache rigidbody reference
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    // Handle top down input
    void Update()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float y = Input.GetAxisRaw("Vertical");
        Vector2 dir = new Vector2(x, y).normalized;
        rb.linearVelocity = dir * moveSpeed;

        if (dir.sqrMagnitude > 0.001f)
            transform.right = dir;
    }

    // Called when caught
    public void OnCaught()
    {
        rb.linearVelocity = Vector2.zero;
        enabled = false;
        Debug.Log("Player caught by guard");
    }

    // Draw preview ring
    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;
        Gizmos.color = new Color(0f, 1f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, footstepPreview);
    }
}
