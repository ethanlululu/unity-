using UnityEngine;

[System.Serializable]
public class RidgeNoiseSettings
{
    public float noiseScale = 50f;
    public int octaves = 4;
    [Range(0f, 1f)] public float persistence = 0.5f;
    public float lacunarity = 2f;

    public float ridgeSharpness = 2f; // Controls how sharp the ridges are
    [Range(0f, 1f)] public float taperHighOctaves = 0.5f; // Smooth taper for higher octaves
    public Vector2 offset;
}
