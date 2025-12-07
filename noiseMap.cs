using UnityEngine;
using Unity.Mathematics;

public struct NoiseMapResult
{
    public float[,] noiseMap;
    public float minHeight;
    public float maxHeight;
}

public static class NoiseMapGenerator
{
    public enum NormalizeMode { Local, Global }

    public static NoiseMapResult GenerateNoiseMap(
        int seed, int width, int height,
        float scale,
        int octaves,
        float persistence,
        float lacunarity,
        Vector2 offset,
        float weight,
        NormalizeMode normalizeMode
    )
    {
        float[,] noiseMap = new float[width, height];

        if (scale <= 0f)
            scale = 0.0001f;

        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[octaves];

        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000);
            float offsetY = prng.Next(-100000, 100000);
            octaveOffsets[i] = new Vector2(offsetX, offsetY);
        }

        float maxNoiseHeight = float.MinValue;
        float minNoiseHeight = float.MaxValue;

        // STEP 1: Generate raw noise values
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // CRITICAL FIX: Changed 'offset.y + y' to 'offset.y - y'
                // This aligns the noise sampling direction (North->South) 
                // with the MeshGenerator's loop direction (Index 0 is +Z/North).
                float2 worldPos = new float2(offset.x + x, offset.y - y);

                float ridgeVal = OctavedRidgeNoise(worldPos, seed, scale, octaves, lacunarity, persistence, octaveOffsets, weight);
                float simplexVal = OctavedSimplexNoise(worldPos, seed, scale, octaves, lacunarity, persistence, octaveOffsets);
                float noiseHeight = (ridgeVal + simplexVal) / 2f;

                if (float.IsNaN(noiseHeight) || float.IsInfinity(noiseHeight) || Mathf.Abs(noiseHeight) > 1000f)
                {
                    noiseHeight = 0.5f;
                }

                if (noiseHeight > maxNoiseHeight) maxNoiseHeight = noiseHeight;
                if (noiseHeight < minNoiseHeight) minNoiseHeight = noiseHeight;

                noiseMap[x, y] = noiseHeight;
            }
        }

        // STEP 2: Normalize based on mode
        if (normalizeMode == NormalizeMode.Local)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (maxNoiseHeight != minNoiseHeight)
                    {
                        noiseMap[x, y] = Mathf.InverseLerp(minNoiseHeight, maxNoiseHeight, noiseMap[x, y]);
                    }
                    else
                    {
                        noiseMap[x, y] = 0.5f;
                    }
                }
            }
        }
        else // Global mode
        {
            float gMin, gMax;
            EndlessTerrain.GetGlobalBounds(out gMin, out gMax);

            if (float.IsNaN(gMin) || float.IsNaN(gMax) ||
                float.IsInfinity(gMin) || float.IsInfinity(gMax) ||
                Mathf.Approximately(gMax, gMin) ||
                !EndlessTerrain.globalHeightPrecomputed)
            {
                Debug.LogWarning("Invalid global bounds, falling back to local normalization for this chunk");
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (maxNoiseHeight != minNoiseHeight)
                        {
                            noiseMap[x, y] = Mathf.InverseLerp(minNoiseHeight, maxNoiseHeight, noiseMap[x, y]);
                        }
                        else
                        {
                            noiseMap[x, y] = 0.5f;
                        }
                    }
                }
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float normalizedValue = Mathf.InverseLerp(gMin, gMax, noiseMap[x, y]);
                        if (float.IsNaN(normalizedValue) || float.IsInfinity(normalizedValue))
                        {
                            normalizedValue = 0.5f;
                        }
                        noiseMap[x, y] = Mathf.Clamp01(normalizedValue);
                    }
                }
            }
        }

        return new NoiseMapResult
        {
            noiseMap = noiseMap,
            minHeight = minNoiseHeight,
            maxHeight = maxNoiseHeight
        };
    }

    private static float OctavedRidgeNoise(
        float2 worldPos, int seed, float scale, int octaves, float lacunarity, float persistence,
        Vector2[] octaveOffsets, float weight)
    {
        float noiseVal = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float currentWeight = 1f;
        float maxValue = 0f;

        for (int i = 0; i < octaves; i++)
        {
            float sampleX = (worldPos.x + octaveOffsets[i].x) / scale * frequency;
            float sampleY = (worldPos.y + octaveOffsets[i].y) / scale * frequency;

            float n = OpenSimplex2S.Noise2_ImproveX(seed + i, sampleX, sampleY);
            float ridge = 1f - Mathf.Abs(n);

            ridge = Mathf.Pow(ridge, Mathf.Clamp(weight, 0.1f, 10f));
            ridge *= currentWeight;

            currentWeight = Mathf.Clamp01(ridge * weight);

            noiseVal += ridge * amplitude;
            maxValue += amplitude;

            frequency *= lacunarity;
            amplitude *= persistence;
        }

        return maxValue > 0f ? noiseVal / maxValue : 0f;
    }

    private static float OctavedSimplexNoise(
        float2 worldPos, int seed, float scale, int octaves, float lacunarity, float persistence,
        Vector2[] octaveOffsets)
    {
        float noiseVal = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxValue = 0f;

        for (int i = 0; i < octaves; i++)
        {
            float sampleX = (worldPos.x + octaveOffsets[i].x) / scale * frequency;
            float sampleY = (worldPos.y + octaveOffsets[i].y) / scale * frequency;

            float n = Unity.Mathematics.noise.snoise(new float2(sampleX, sampleY));
            float v = (n + 1f) / 2f;

            noiseVal += v * amplitude;
            maxValue += amplitude;

            frequency *= lacunarity;
            amplitude *= persistence;
        }

        return maxValue > 0f ? noiseVal / maxValue : 0.5f;
    }
}
