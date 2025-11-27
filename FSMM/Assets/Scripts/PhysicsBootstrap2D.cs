using UnityEngine;
public class PhysicsBootstrap2D : MonoBehaviour
{
    void Awake()
    {
        var player = LayerMask.NameToLayer("Player");
        var gaurd = LayerMask.NameToLayer("Gaurd");
        var evidence = LayerMask.NameToLayer("Evidence");
        var water = LayerMask.NameToLayer("Water");
        var obstacles = LayerMask.NameToLayer("Obstacles");
        Physics2D.IgnoreLayerCollision(player, obstacles, false);
        Physics2D.IgnoreLayerCollision(gaurd, obstacles, false);
        Physics2D.IgnoreLayerCollision(evidence, obstacles, false);
        Physics2D.IgnoreLayerCollision(player, water, true);
        Physics2D.IgnoreLayerCollision(gaurd, water, true);
        Physics2D.IgnoreLayerCollision(evidence, water, true);
    }
}
