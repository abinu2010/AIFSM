using UnityEngine;

public class ScentTrail : MonoBehaviour
{
    // Scent node prefab
    public ScentNode scentPrefab;
    // Seconds between drops
    public float dropInterval = 0.35f;
    // Node initial value
    public float initialIntensity = 1f;
    // Minimum step distance
    public float minStepDistance = 0.3f;

    float timer;
    Vector3 lastPos;

    // Initialize last position
    void Start()
    {
        lastPos = transform.position;
    }

    // Spawn scent while moving
    void Update()
    {
        timer += Time.deltaTime;
        if (timer < dropInterval) return;

        if (Vector3.Distance(transform.position, lastPos) >= minStepDistance)
        {
            var node = Instantiate(scentPrefab, transform.position, Quaternion.identity);
            node.intensity = initialIntensity;
            lastPos = transform.position;
            timer = 0f;
        }
    }
}
