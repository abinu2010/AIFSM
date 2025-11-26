using UnityEngine;

public class CameraFollow2D : MonoBehaviour
{
    // Follow target reference
    public Transform target;
    // Screen space offset
    public Vector2 offset = new Vector2(0f, 0.5f);
    // Smooth damp time
    public float smoothTime = 0.15f;

    Vector3 velocity;

    // Late tick follow
    void LateUpdate()
    {
        if (!target) return;
        Vector3 goal = new Vector3(target.position.x + offset.x,
                                   target.position.y + offset.y,
                                   transform.position.z);
        transform.position = Vector3.SmoothDamp(transform.position, goal, ref velocity, smoothTime);
    }
}
