using UnityEngine;

public class ScentNode : MonoBehaviour
{
    // Starting intensity value
    public float intensity = 1f;
    // Intensity decay rate
    public float decayPerSecond = 0.25f;

    // Tick decay timer
    void Update()
    {
        intensity -= decayPerSecond * Time.deltaTime;
        if (intensity <= 0f) Destroy(gameObject);
    }
}
