using UnityEngine;

public class PlayerNoiseEmitter : MonoBehaviour
{
    // Footstep loudness value
    public float footstepLoudness = 2f;
    // Step distance meters
    public float stepDistance = 1.1f;

    Vector3 lastStepPos;

    // Initialize last step
    void Start()
    {
        lastStepPos = transform.position;
    }

    // Emit while moving
    void Update()
    {
        float d = Vector3.Distance(transform.position, lastStepPos);
        if (d >= stepDistance)
        {
            SoundBus.Emit(transform.position, footstepLoudness, SoundTag.Footstep);
            lastStepPos = transform.position;
        }
    }
}
