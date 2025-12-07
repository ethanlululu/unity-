using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class MapGenerator : MonoBehaviour
{
    public enum DrawMode { NoiseMap, ColourMap, Mesh };
    public DrawMode drawMode;
    public NoiseMapGenerator.NormalizeMode normalizeMode;

    public const int mapChunkSize = 241;
    [Range(0, 6)]
    public int editorPreviewLOD;

    // Performance Settings
    [Header("Performance")]
    [Tooltip("Max milliseconds to spend on main-thread updates per frame AFTER the initial map is loaded (for smooth runtime).")]
    [Range(0.1f, 33f)]
    public float targetFrameRateBudget = 8f; // Defaulted to a smooth 8ms for runtime performance.

    // This private budget is used ONLY at startup to clear the initial backlog quickly.
    private const float burstLoadBudget = 1000f; // Allows up to 30ms per frame during the initial load spike.
    private bool isInitialLoadPhase = true;

    public float noiseScale;
    public int octaves;
    [Range(0, 1)]
    public float persistance;
    public float lacunarity;
    public int seed;
    public Vector2 offset;
    public float meshHeightMultiplier;
    public AnimationCurve meshHeightCurve;
    public float wight;
    public bool autoUpdate;
    public TerrainType[] regions;

    Queue<MapThreadInfo<MapData>> mapDataThreadInfoQueue = new Queue<MapThreadInfo<MapData>>();
    Queue<MapThreadInfo<MeshData>> meshDataThreadInfoQueue = new Queue<MapThreadInfo<MeshData>>();

    public void DrawMapInEditor()
    {
        MapData mapData = GenerateMapData(Vector2.zero, true);
        MapDisplay display = FindObjectOfType<MapDisplay>();

        if (drawMode == DrawMode.NoiseMap)
            display.DrawTexture(TextureGenerator.TextureFromHeightMap(mapData.heightMap));
        else if (drawMode == DrawMode.ColourMap)
            display.DrawTexture(TextureGenerator.TextureFromColourMap(mapData.colourMap, mapChunkSize, mapChunkSize));
        else if (drawMode == DrawMode.Mesh)
            display.DrawMesh(MeshGenerator.GenerateTerrainMesh(mapData.heightMap, meshHeightMultiplier, meshHeightCurve, editorPreviewLOD, Vector2.zero),
                             TextureGenerator.TextureFromColourMap(mapData.colourMap, mapChunkSize, mapChunkSize));
    }

    public void RequestMapData(Vector2 centre, Action<MapData> callback)
    {
        // Optimization: Use Task.Run (ThreadPool) instead of creating new Threads manually
        Task.Run(() => MapDataThread(centre, callback));
    }

    void MapDataThread(Vector2 centre, Action<MapData> callback)
    {
        MapData mapData = GenerateMapData(centre);
        lock (mapDataThreadInfoQueue)
        {
            mapDataThreadInfoQueue.Enqueue(new MapThreadInfo<MapData>(callback, mapData));
        }
    }

    public void RequestMeshData(MapData mapData, int lod, Action<MeshData> callback)
    {
        // Optimization: Use Task.Run (ThreadPool)
        Task.Run(() => MeshDataThread(mapData, lod, callback));
    }

    void MeshDataThread(MapData mapData, int lod, Action<MeshData> callback)
    {
        MeshData meshData = MeshGenerator.GenerateTerrainMesh(mapData.heightMap, meshHeightMultiplier, meshHeightCurve, lod, mapData.chunkPosition);
        lock (meshDataThreadInfoQueue)
        {
            meshDataThreadInfoQueue.Enqueue(new MapThreadInfo<MeshData>(callback, meshData));
        }
    }

    void Update()
    {
        // Check if the initial heavy load is done.
        // If both queues are empty, we can exit the burst phase.
        if (isInitialLoadPhase && mapDataThreadInfoQueue.Count == 0 && meshDataThreadInfoQueue.Count == 0)
        {
            isInitialLoadPhase = false;
            // Debug.Log("Initial Map Loading Complete. Switching to standard smooth budget.");
        }

        // Use the high burst budget at the start, then switch to the user-defined smooth budget.
        float currentBudget = isInitialLoadPhase ? burstLoadBudget : targetFrameRateBudget;

        // PERFORMANCE FIX: Time Budgeting
        // We process as many queue items as possible within the current budget.

        float startTime = Time.realtimeSinceStartup;
        float budgetSeconds = currentBudget / 1000f;

        while (mapDataThreadInfoQueue.Count > 0)
        {
            MapThreadInfo<MapData> threadInfo = mapDataThreadInfoQueue.Dequeue();
            threadInfo.callback(threadInfo.parameter);

            // Stop if we've exceeded our frame budget
            if (Time.realtimeSinceStartup - startTime > budgetSeconds) return;
        }

        while (meshDataThreadInfoQueue.Count > 0)
        {
            MapThreadInfo<MeshData> threadInfo = meshDataThreadInfoQueue.Dequeue();
            threadInfo.callback(threadInfo.parameter);

            // Stop if we've exceeded our frame budget
            if (Time.realtimeSinceStartup - startTime > budgetSeconds) return;
        }
    }

    MapData GenerateMapData(Vector2 centre, bool debugEdges = false)
    {
        // Reverting the previous snap fix as it was insufficient.
        // The core world position for the chunk is (centre + offset).
        Vector2 worldPosition = centre + offset;

        // NEW SEAM FIX: Apply a large constant offset to shift the noise sampling
        // away from the floating-point-sensitive origin (0,0).
        // This makes noise calculations consistent across shared boundaries.
        const float arbitraryLargeOffset = 100000f;
        Vector2 noiseSampleOffset = new Vector2(arbitraryLargeOffset, arbitraryLargeOffset);
        Vector2 noiseSamplePosition = worldPosition + noiseSampleOffset;


        var result = NoiseMapGenerator.GenerateNoiseMap(
            seed, mapChunkSize, mapChunkSize,
            noiseScale, octaves, persistance, lacunarity,
            noiseSamplePosition, // Use the stabilized position for consistent noise
            wight, normalizeMode
        );

        float[,] noiseMap = result.noiseMap;

        // Generate colour map
        Color[] colourMap = new Color[mapChunkSize * mapChunkSize];
        for (int y = 0; y < mapChunkSize; y++)
        {
            for (int x = 0; x < mapChunkSize; x++)
            {
                float currentHeight = noiseMap[x, y];
                for (int i = 0; i < regions.Length; i++)
                {
                    if (currentHeight >= regions[i].height)
                    {
                        colourMap[y * mapChunkSize + x] = regions[i].colour;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        return new MapData(noiseMap, colourMap, result.minHeight, result.maxHeight, centre);
    }

    [ContextMenu("Test Seam Between Two Chunks")]
    public void TestSeam()
    {
        Debug.Log("=== Testing Seam Between Adjacent Chunks ===");

        int chunkSize = mapChunkSize - 1;

        // Test Y-direction (front to back)
        // Since MeshGenerator generates Z top-down (index 0 is North/+Z),
        // we need to check if Chunk A's North Edge matches Chunk B's South Edge.

        Vector2 chunkA_Pos = new Vector2(0, 0) + offset;
        Vector2 chunkB_Pos = new Vector2(0, chunkSize) + offset; // B is North of A

        Debug.Log($"Testing Y-direction seam (Top/Bottom):");
        Debug.Log($"Chunk A (South) center: {chunkA_Pos}");
        Debug.Log($"Chunk B (North) center: {chunkB_Pos}");

        var resultA = NoiseMapGenerator.GenerateNoiseMap(
            seed, mapChunkSize, mapChunkSize,
            noiseScale, octaves, persistance, lacunarity,
            chunkA_Pos, wight, normalizeMode
        );

        var resultB = NoiseMapGenerator.GenerateNoiseMap(
            seed, mapChunkSize, mapChunkSize,
            noiseScale, octaves, persistance, lacunarity,
            chunkB_Pos, wight, normalizeMode
        );

        bool seamY = false;

        // With MeshGenerator: y=0 is Top (North), y=Max is Bottom (South).
        // Chunk A is South. Its North edge (Index 0) touches Chunk B's South edge (Index Max).

        for (int x = 0; x < mapChunkSize; x++)
        {
            // Chunk A Top Edge (Index 0)
            float heightA = resultA.noiseMap[x, 0];

            // Chunk B Bottom Edge (Index Max)
            float heightB = resultB.noiseMap[x, mapChunkSize - 1];

            if (Mathf.Abs(heightA - heightB) > 0.001f)
            {
                Debug.LogError($"Y-SEAM at X={x}: ChunkA[Top]={heightA:F6}, ChunkB[Bottom]={heightB:F6}, diff={Mathf.Abs(heightA - heightB):F6}");
                seamY = true;
            }
        }

        if (!seamY)
        {
            Debug.Log("✅ NO SEAMS FOUND in Y-Direction!");
        }
        else
        {
            Debug.LogError("❌ Y-direction seam detected");
        }
    }

    struct MapThreadInfo<T>
    {
        public readonly Action<T> callback;
        public readonly T parameter;

        public MapThreadInfo(Action<T> callback, T parameter)
        {
            this.callback = callback;
            this.parameter = parameter;
        }
    }
}

[System.Serializable]
public struct TerrainType
{
    public string name;
    public float height;
    public Color colour;
}

public struct MapData
{
    public readonly float[,] heightMap;
    public readonly Color[] colourMap;
    public readonly float minHeight;
    public readonly float maxHeight;
    public readonly Vector2 chunkPosition;

    public MapData(float[,] heightMap, Color[] colourMap, float minHeight, float maxHeight, Vector2 chunkPosition)
    {
        this.heightMap = heightMap;
        this.colourMap = colourMap;
        this.minHeight = minHeight;
        this.maxHeight = maxHeight;
        this.chunkPosition = chunkPosition;
    }
}
