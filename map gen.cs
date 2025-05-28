using UnityEngine;

public class MapGenerator : MonoBehaviour
{
    public enum DrawMode { NoiseMap, ColourMap, Mesh };
    public DrawMode drawMode;

    public const int MapChunkSize = 241;
    [Range(0, 6)]
    public int levelOfDetail;

    public float noiseScale;

    public int octaves;
    [Range(0, 1)]
    public float persistance;
    public float lacunarity;

    public int seed;
    public Vector2 offset;

    public float MeshHeightMulti;
    public bool autoUpdate;

    public AnimationCurve MeshHeightCurve;

    public TerrainType[] regions;

    public fBMWithDerivatives fbmSource; // Make sure this is assigned in the Inspector

    public void GenerateMap()
    {
        float halfWidth = MapChunkSize / 2f;
        float halfHeight = MapChunkSize / 2f;

        float[,] noiseMap = new float[MapChunkSize, MapChunkSize];
        Color[] colourMap = new Color[MapChunkSize * MapChunkSize];

        float minHeight = float.MaxValue;
        float maxHeight = float.MinValue;

        for (int y = 0; y < MapChunkSize; y++)
        {
            for (int x = 0; x < MapChunkSize; x++)
            {
                float sampleX = (x - halfWidth) / noiseScale + offset.x;
                float sampleY = (y - halfHeight) / noiseScale + offset.y;
                Vector3 position = new Vector3(sampleX, 0, sampleY);

                float baseFrequency = 1f / Mathf.Max(noiseScale, 0.0001f); // Prevent division by zero
                Vector4 fbm = fbmSource.GenerateFBM(position, octaves, persistance, baseFrequency);

                float rawHeight = fbm.x;

                // Normalize height from [-1, 1] to [0, 1]
                float height = Mathf.InverseLerp(-1f, 1f, rawHeight);

                noiseMap[x, y] = height;

                // Track height range for debugging
                if (height < minHeight) minHeight = height;
                if (height > maxHeight) maxHeight = height;

                // Color from region
                for (int i = 0; i < regions.Length; i++)
                {
                    if (height <= regions[i].height)
                    {
                        colourMap[y * MapChunkSize + x] = regions[i].colour;
                        break;
                    }
                }
            }
        }

        Debug.Log($"Map generated. Min height: {minHeight}, Max height: {maxHeight}");

        MapDisplay display = FindObjectOfType<MapDisplay>();
        if (drawMode == DrawMode.NoiseMap)
        {
            display.DrawTexture(TextureGenerator.TextureFromHeightMap(noiseMap));
        }
        else if (drawMode == DrawMode.ColourMap)
        {
            display.DrawTexture(TextureGenerator.TextureFromColourMap(colourMap, MapChunkSize, MapChunkSize));
        }
        else if (drawMode == DrawMode.Mesh)
        {
            display.DrawMesh(
                MeshGenerator.GenerateTerrainMesh(noiseMap, MeshHeightMulti, MeshHeightCurve,levelOfDetail),
                TextureGenerator.TextureFromColourMap(colourMap, MapChunkSize, MapChunkSize)
            );
        }
    }

    void OnValidate()
    {
        if (lacunarity < 1) lacunarity = 1;
        if (octaves < 0) octaves = 0;
    }

    [System.Serializable]
    public struct TerrainType
    {
        public string name;
        public float height;
        public Color colour;
    }
}
