using UnityEngine;

public class BaitThrower : MonoBehaviour
{
    // Bait prefab reference
    public Bait baitPrefab;
    // Throw impulse force
    public float throwForce = 8f;
    // Spawn offset value
    public Vector2 spawnOffset = new Vector2(0.4f, 0.6f);

    // Check for input
    void Update()
    {
        if (Input.GetMouseButtonDown(0)) ThrowBait();
    }

    // Spawn and launch
    void ThrowBait()
    {
        if (!baitPrefab) return;
        Vector3 pos = transform.position + (Vector3)spawnOffset;
        var bait = Instantiate(baitPrefab, pos, Quaternion.identity);
        var rb = bait.GetComponent<Rigidbody2D>();
        if (rb)
        {
            Vector2 dir = new Vector2(transform.localScale.x >= 0 ? 1f : -1f, 0.6f).normalized;
            rb.AddForce(dir * throwForce, ForceMode2D.Impulse);
        }
        SoundBus.Emit(pos, 1f, SoundTag.Bait);
    }
}
