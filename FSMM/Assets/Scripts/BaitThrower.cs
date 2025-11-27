// BaitThrower.cs
using UnityEngine;

public class BaitThrower : MonoBehaviour
{
    public Bait baitPrefab;
    public float throwForce = 8f;
    public Vector2 spawnOffset = new Vector2(0.2f, 0.2f);

    void Update()
    {
        if (Input.GetMouseButtonDown(0)) ThrowBait();
    }

    void ThrowBait()
    {
        if (!baitPrefab) return;
        Vector3 pos = transform.position + (Vector3)spawnOffset;
        var bait = Instantiate(baitPrefab, pos, Quaternion.identity);
        var rb = bait.GetComponent<Rigidbody2D>();
        if (rb)
        {
            Vector3 m = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 dir = ((Vector2)m - (Vector2)pos).normalized;
            rb.AddForce(dir * throwForce, ForceMode2D.Impulse);
        }
        // removed throw ping
    }
}
