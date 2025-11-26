using System;
using UnityEngine;

public enum SoundTag { Footstep, Thud, Bait, Hit, Misc }

public static class SoundBus
{
    // Simple sound bus
    public static event Action<Vector2, float, SoundTag> OnSound;

    // Emit sound event
    public static void Emit(Vector2 position, float loudness, SoundTag tag)
    {
        OnSound?.Invoke(position, loudness, tag);
    }
}
