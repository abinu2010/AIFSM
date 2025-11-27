using TMPro;
using UnityEngine;

public class GuardStateBillboard : MonoBehaviour
{
    // Text component reference
    public TMP_Text text;
    // Tracked guard ai
    public GaurdAI guard;

    // Auto resolve references
    void Awake()
    {
        if (!guard) guard = GetComponentInParent<GaurdAI>();
        if (!text) text = GetComponentInChildren<TMP_Text>();
    }

    // Update label text
    void Update()
    {
        if (!guard || !text) return;
        text.text = guard.enabled ? GetStateName() : "Disabled";
        transform.rotation = Quaternion.identity;
    }

    // Map current state
    string GetStateName()
    {
        var f = typeof(GaurdAI).GetField("state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var s = (GaurdAI.State)f.GetValue(guard);
        return s.ToString();
    }
}
