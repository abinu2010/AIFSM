using System;
using UnityEngine;

public static class AlertBus
{
    // Simple alert bus
    public static event Action<Vector2> OnAlert;

    // Broadcast alert position
    public static void Broadcast(Vector2 position)
    {
        OnAlert?.Invoke(position);
    }
}
