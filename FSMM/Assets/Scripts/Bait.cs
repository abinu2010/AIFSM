using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Bait : MonoBehaviour
{
    // Sound on impact
    public float thudLoudness = 8f;
    // Evidence lifetime seconds
    public float evidenceSeconds = 12f;

    bool landed;
    float deathAt;

    // Setup lifetime window
    void Start()
    {
        deathAt = Time.time + evidenceSeconds;
        gameObject.layer = LayerMask.NameToLayer("Evidence");
    }

    // Emit thud on land
    void OnCollisionEnter2D(Collision2D col)
    {
        if (landed) return;
        landed = true;
        SoundBus.Emit(transform.position, thudLoudness, SoundTag.Thud);
    }

    // Destroy when expired
    void Update()
    {
        if (Time.time >= deathAt) Destroy(gameObject);
    }
}
