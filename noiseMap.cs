using UnityEngine;
using Unity.Mathematics;

public static class NoiseMapGenerator
{
    public static float[,] GenerateNoiseMap(
        int seed, int width, int height,
        float scale,
        int octaves,
        float persistence,
        float lacunarity,
        Vector2 offset,
        float weight // added to match your ridge noise weight
    )
    {
        float[,] noiseMap = new float[width, height];

        if (scale <= 0f)
            scale = 0.0001f;

        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[octaves];
        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000) + offset.x;
            float offsetY = prng.Next(-100000, 100000) + offset.y;
            octaveOffsets[i] = new Vector2(offsetX, offsetY);
        }

        float maxNoiseHeight = float.MinValue;
        float minNoiseHeight = float.MaxValue;

        float halfWidth = width / 2f;
        float halfHeight = height / 2f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float2 pos = new float2(x, y);
                float noiseHeight = 0;

                // Example: you can choose which noise to use here — ridge or simplex
                // For demo, let's mix half ridge, half simplex like your original:
                float ridgeVal = OctavedRidgeNoise(pos, seed, scale, octaves, lacunarity, persistence, octaveOffsets, width, height, weight);
                float simplexVal = OctavedSimplexNoise(pos, seed, scale, octaves, lacunarity, persistence, octaveOffsets, width, height);

                noiseHeight = (ridgeVal + simplexVal) / 2f;

                if (noiseHeight > maxNoiseHeight) maxNoiseHeight = noiseHeight;
                if (noiseHeight < minNoiseHeight) minNoiseHeight = noiseHeight;

                noiseMap[x, y] = noiseHeight;
            }
        }

        // Normalize noiseMap values to [0,1]
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                noiseMap[x, y] = Mathf.InverseLerp(minNoiseHeight, maxNoiseHeight, noiseMap[x, y]);

        return noiseMap;
    }

    private static float OctavedRidgeNoise(
        float2 pos, int seed, float scale, int octaves, float lacunarity, float persistence,
        Vector2[] octaveOffsets, int width, int height, float weight)
    {
        float noiseVal = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float currentWeight = 1f;

        float halfWidth = width / 2f;
        float halfHeight = height / 2f;

        for (int i = 0; i < octaves; i++)
        {
            float sampleX = (pos.x - halfWidth) / scale * frequency + octaveOffsets[i].x;
            float sampleY = (pos.y - halfHeight) / scale * frequency + octaveOffsets[i].y;

            float n = OpenSimplex2S.Noise2_ImproveX(seed, sampleX, sampleY);
            float v = 1f - Mathf.Abs(n);
            v *= v;
            v *= currentWeight;

            currentWeight = Mathf.Clamp01(v * weight);

            noiseVal += v * amplitude;

            frequency *= lacunarity;
            amplitude *= persistence;
        }

        return noiseVal;
    }

    private static float OctavedSimplexNoise(
        float2 pos, int seed, float scale, int octaves, float lacunarity, float persistence,
        Vector2[] octaveOffsets, int width, int height)
    {
        float noiseVal = 0f;
        float amplitude = 1f;
        float frequency = 1f;

        float halfWidth = width / 2f;
        float halfHeight = height / 2f;

        for (int i = 0; i < octaves; i++)
        {
            float sampleX = (pos.x - halfWidth) / scale * frequency + octaveOffsets[i].x;
            float sampleY = (pos.y - halfHeight) / scale * frequency + octaveOffsets[i].y;

            float n = OpenSimplex2S.Noise2_ImproveX(seed, sampleX, sampleY);
            float v = (n + 1f) / 2f;

            noiseVal += v * amplitude;

            frequency *= lacunarity;
            amplitude *= persistence;
        }

        return noiseVal;
    }
}
