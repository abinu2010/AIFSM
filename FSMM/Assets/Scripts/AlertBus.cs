using System;
using UnityEngine;

// Centralized alert bus
public static class AlertBus
{
    // Global alert event
    public static event Action<Vector2> OnAlert;

    // Broadcast alert ping
    public static void Broadcast(Vector2 pos)
    {
        Debug.Log($"[AlertBus] Broadcast at {pos}");
        OnAlert?.Invoke(pos);
    }
}
