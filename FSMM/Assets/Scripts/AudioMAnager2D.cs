using System.Collections.Generic;
using UnityEngine;

public class AudioManager2D : MonoBehaviour
{
    // Clip banks here
    public AudioClip[] footstepClips;
    public AudioClip[] thudClips;
    public AudioClip[] hitClips;
    public AudioClip[] baitClips;

    [Range(0f, 1f)] public float masterVolume = 0.9f;
    public float footstepRef = 2f;
    public float thudRef = 8f;
    public float hitRef = 8f;
    public Vector2 pitchJitter = new Vector2(0.95f, 1.05f);

    // Debug rings here
    public bool drawGizmos = true;
    public float ringLife = 1.5f;
    public float ringFootstep = 6f;
    public float ringThud = 7f;
    public float ringHit = 7f;

    struct Marker { public Vector2 pos; public float until; public float radius; public Color color; }
    readonly List<Marker> markers = new List<Marker>();

    void OnEnable() { SoundBus.OnSound += OnSound; }
    void OnDisable() { SoundBus.OnSound -= OnSound; }

    void Update()
    {
        // Cull markers here
        for (int i = markers.Count - 1; i >= 0; i--)
            if (Time.time > markers[i].until) markers.RemoveAt(i);
    }

    void OnSound(Vector2 pos, float loud, SoundTag tag)
    {
        AudioClip clip = null;
        float volume = 1f;

        switch (tag)
        {
            case SoundTag.Footstep:
                clip = Pick(footstepClips);
                volume = Mathf.Clamp01(loud / Mathf.Max(0.01f, footstepRef));
                AddMarker(pos, ringFootstep, new Color(0f, 1f, 0f, 1f));
                break;
            case SoundTag.Thud:
                clip = Pick(thudClips);
                volume = Mathf.Clamp01(loud / Mathf.Max(0.01f, thudRef));
                AddMarker(pos, ringThud, new Color(1f, 0f, 1f, 1f));
                break;
            case SoundTag.Hit:
                clip = Pick(hitClips);
                volume = Mathf.Clamp01(loud / Mathf.Max(0.01f, hitRef));
                AddMarker(pos, ringHit, new Color(1f, 0f, 0f, 1f));
                break;
            case SoundTag.Bait:
                clip = Pick(baitClips);
                volume = 0.5f;
                AddMarker(pos, ringThud * 0.6f, new Color(0.6f, 0.6f, 0.6f, 1f));
                break;
        }

        if (!clip) return;
        var go = new GameObject("OneShot");
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = masterVolume * volume;
        src.pitch = Random.Range(pitchJitter.x, pitchJitter.y);
        src.spatialBlend = 0f;
        src.Play();
        Destroy(go, clip.length + 0.1f);
    }

    AudioClip Pick(AudioClip[] set)
    {
        if (set == null || set.Length == 0) return null;
        int i = Random.Range(0, set.Length);
        return set[i];
    }

    void AddMarker(Vector2 pos, float radius, Color color)
    {
        markers.Add(new Marker { pos = pos, radius = radius, color = color, until = Time.time + ringLife });
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        foreach (var m in markers)
        {
            Gizmos.color = m.color;
            Gizmos.DrawWireSphere(m.pos, m.radius);
        }
    }
}
