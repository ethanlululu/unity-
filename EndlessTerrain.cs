using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System;

public class EndlessTerrain : MonoBehaviour
{
    const float viewerMoveThresholdForChunkUpdate = 25f;
    const float sqrViewerMoveThresholdForChunkUpdate = viewerMoveThresholdForChunkUpdate * viewerMoveThresholdForChunkUpdate;

    public LODInfo[] detailLevels;
    public static float maxViewDst;

    public Transform viewer;
    public Material mapMaterial;

    public static Vector2 viewerPosition;
    Vector2 viewerPositionOld;
    static MapGenerator mapGenerator;
    int chunkSize;
    int chunksVisibleInViewDst;

    Dictionary<Vector2, TerrainChunk> terrainChunkDictionary = new Dictionary<Vector2, TerrainChunk>();
    static List<TerrainChunk> terrainChunksVisibleLastUpdate = new List<TerrainChunk>();

    public static float globalMinHeight = 0f;
    public static float globalMaxHeight = 1f;
    public static bool globalHeightPrecomputed = false;
    private static readonly object globalHeightLock = new object();

    IEnumerator Start()
    {
        mapGenerator = FindObjectOfType<MapGenerator>();
        if (mapGenerator == null)
        {
            Debug.LogError("MapGenerator not found!");
            yield break;
        }

        // Initialize safe defaults
        globalMinHeight = 0f;
        globalMaxHeight = 1f;
        globalHeightPrecomputed = false;

        maxViewDst = detailLevels[detailLevels.Length - 1].visibleDstThreshold;
        chunkSize = MapGenerator.mapChunkSize - 1;
        chunksVisibleInViewDst = Mathf.RoundToInt(maxViewDst / chunkSize);

        // CRITICAL FIX: Initialize viewer position BEFORE precomputation
        viewerPosition = new Vector2(viewer.position.x, viewer.position.z);
        viewerPositionOld = viewerPosition;

        if (mapGenerator.normalizeMode == NoiseMapGenerator.NormalizeMode.Global)
        {
            Debug.Log("[EndlessTerrain] Starting global height precomputation...");
            // Sample around the VIEWER'S starting position, not origin!
            yield return StartCoroutine(PrecomputeGlobalMinMaxCoroutine());
            Debug.Log($"[EndlessTerrain] Global bounds computed: [{globalMinHeight:F4}, {globalMaxHeight:F4}]");
        }
        else
        {
            // For Local mode, we don't need global bounds
            globalMinHeight = 0f;
            globalMaxHeight = 1f;
            globalHeightPrecomputed = true;
            Debug.Log("[EndlessTerrain] Using Local normalization mode");
        }

        // Start generating chunks
        UpdateVisibleChunks();
        Debug.Log("[EndlessTerrain] Initialization complete");
    }

    IEnumerator PrecomputeGlobalMinMaxCoroutine()
    {
        int sampleSize = 5; // Sample a 11x11 grid of chunks
        float tempMin = float.PositiveInfinity;
        float tempMax = float.NegativeInfinity;
        int samplesProcessed = 0;

        // CRITICAL FIX: Sample around the viewer's current position
        int viewerChunkCoordX = Mathf.RoundToInt(viewerPosition.x / chunkSize);
        int viewerChunkCoordY = Mathf.RoundToInt(viewerPosition.y / chunkSize);

        Debug.Log($"[EndlessTerrain] Sampling around viewer at chunk coords ({viewerChunkCoordX}, {viewerChunkCoordY})");

        for (int yOffset = -sampleSize; yOffset <= sampleSize; yOffset++)
        {
            for (int xOffset = -sampleSize; xOffset <= sampleSize; xOffset++)
            {
                // Calculate chunk coordinate relative to viewer
                Vector2 chunkCoord = new Vector2(viewerChunkCoordX + xOffset, viewerChunkCoordY + yOffset);
                // Convert to world position (same as TerrainChunk constructor)
                Vector2 chunkCentre = chunkCoord * chunkSize;

                // Generate the heightmap data using the SAME method as actual chunks
                MapData sampleData = GenerateSampleMapData(chunkCentre, samplesProcessed);

                // Use the RAW min/max heights from the noise generation (before normalization)
                if (sampleData.minHeight < tempMin) tempMin = sampleData.minHeight;
                if (sampleData.maxHeight > tempMax) tempMax = sampleData.maxHeight;

                samplesProcessed++;

                // Yield periodically to avoid freezing
                if (samplesProcessed % 10 == 0)
                    yield return null;
            }
        }

        // Add 10% padding to the range
        if (samplesProcessed > 0 && tempMin < tempMax)
        {
            float range = tempMax - tempMin;
            globalMinHeight = tempMin - (range * 0.1f);
            globalMaxHeight = tempMax + (range * 0.1f);

            // Ensure bounds are not identical
            if (Mathf.Approximately(globalMinHeight, globalMaxHeight))
            {
                globalMinHeight -= 0.1f;
                globalMaxHeight += 0.1f;
            }

            globalHeightPrecomputed = true;
            Debug.Log($"[EndlessTerrain] Sampled {samplesProcessed} chunks");
            Debug.Log($"[EndlessTerrain] Raw range: [{tempMin:F4}, {tempMax:F4}]");
            Debug.Log($"[EndlessTerrain] Final global bounds: [{globalMinHeight:F4}, {globalMaxHeight:F4}]");
        }
        else
        {
            Debug.LogError("[EndlessTerrain] Failed to compute valid global bounds!");
            globalMinHeight = 0f;
            globalMaxHeight = 1f;
            globalHeightPrecomputed = true;
        }

        // Final safety check
        if (globalMinHeight >= globalMaxHeight)
        {
            Debug.LogError("[EndlessTerrain] Invalid global bounds after computation!");
            globalMinHeight = 0f;
            globalMaxHeight = 1f;
        }
    }

    // Helper method to generate sample map data for precomputation
    // CRITICAL: Must match the noise sampling in MapGenerator.GenerateMapData() exactly
    private MapData GenerateSampleMapData(Vector2 centre, int samplesProcessed)
    {
        Vector2 worldPosition = centre + mapGenerator.offset;

        // NEW SEAM FIX: Apply the SAME large constant offset as MapGenerator
        const float arbitraryLargeOffset = 100000f;
        Vector2 noiseSampleOffset = new Vector2(arbitraryLargeOffset, arbitraryLargeOffset);
        Vector2 noiseSamplePosition = worldPosition + noiseSampleOffset;

        // Debug logging for first few samples
        if (samplesProcessed < 3)
        {
            Debug.Log($"[Sample {samplesProcessed}] centre={centre}, worldPos={worldPosition}, noiseSample={noiseSamplePosition}");
        }

        // Generate noise - the result contains RAW min/max before normalization is applied
        var result = NoiseMapGenerator.GenerateNoiseMap(
            mapGenerator.seed,
            MapGenerator.mapChunkSize,
            MapGenerator.mapChunkSize,
            mapGenerator.noiseScale,
            mapGenerator.octaves,
            mapGenerator.persistance,
            mapGenerator.lacunarity,
            noiseSamplePosition, // Use the SAME offset as actual generation
            mapGenerator.wight,
            NoiseMapGenerator.NormalizeMode.Local // Local mode to get raw values
        );

        // Return a MapData with the raw min/max heights from BEFORE normalization
        return new MapData(result.noiseMap, null, result.minHeight, result.maxHeight, centre);
    }

    public static void GetGlobalBounds(out float minHeight, out float maxHeight)
    {
        lock (globalHeightLock)
        {
            minHeight = globalMinHeight;
            maxHeight = globalMaxHeight;

            // Safety check
            if (minHeight >= maxHeight || !globalHeightPrecomputed)
            {
                Debug.LogWarning("[EndlessTerrain] Global bounds not ready or invalid");
                minHeight = 0f;
                maxHeight = 1f;
            }
        }
    }

    void Update()
    {
        viewerPosition = new Vector2(viewer.position.x, viewer.position.z);

        if ((viewerPositionOld - viewerPosition).sqrMagnitude > sqrViewerMoveThresholdForChunkUpdate)
        {
            viewerPositionOld = viewerPosition;
            UpdateVisibleChunks();
        }
    }

    void UpdateVisibleChunks()
    {
        foreach (var chunk in terrainChunksVisibleLastUpdate)
            chunk.SetVisible(false);
        terrainChunksVisibleLastUpdate.Clear();

        int currentChunkCoordX = Mathf.RoundToInt(viewerPosition.x / chunkSize);
        int currentChunkCoordY = Mathf.RoundToInt(viewerPosition.y / chunkSize);

        for (int yOffset = -chunksVisibleInViewDst; yOffset <= chunksVisibleInViewDst; yOffset++)
        {
            for (int xOffset = -chunksVisibleInViewDst; xOffset <= chunksVisibleInViewDst; xOffset++)
            {
                Vector2 viewedChunkCoord = new Vector2(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset);

                if (terrainChunkDictionary.ContainsKey(viewedChunkCoord))
                {
                    terrainChunkDictionary[viewedChunkCoord].UpdateTerrainChunk();
                }
                else
                {
                    terrainChunkDictionary.Add(viewedChunkCoord, new TerrainChunk(viewedChunkCoord, chunkSize, detailLevels, transform, mapMaterial));
                }
            }
        }
    }

    public class TerrainChunk
    {
        GameObject meshObject;
        Vector2 position;
        Bounds bounds;
        Vector2 chunkCoord;

        MeshRenderer meshRenderer;
        MeshFilter meshFilter;

        LODInfo[] detailLevels;
        LODMesh[] lodMeshes;

        MapData mapData;
        bool mapDataReceived;
        int previousLODIndex = -1;
        static int chunkCount = 0;

        public TerrainChunk(Vector2 coord, int size, LODInfo[] detailLevels, Transform parent, Material material)
        {
            this.detailLevels = detailLevels;
            this.chunkCoord = coord;

            position = coord * size;
            bounds = new Bounds(position, Vector2.one * size);
            Vector3 positionV3 = new Vector3(position.x, 0, position.y);

            meshObject = new GameObject($"Chunk ({coord.x},{coord.y})");
            meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshFilter = meshObject.AddComponent<MeshFilter>();
            meshRenderer.material = material;

            meshObject.transform.position = positionV3;
            meshObject.transform.parent = parent;
            SetVisible(false);

            lodMeshes = new LODMesh[detailLevels.Length];
            for (int i = 0; i < detailLevels.Length; i++)
                lodMeshes[i] = new LODMesh(detailLevels[i].lod, UpdateTerrainChunk);

            // Debug logging for first few chunks
            if (chunkCount < 5)
            {
                Debug.Log($"[Chunk {coord}] position={position}, worldPos={position + mapGenerator.offset}");
                chunkCount++;
            }

            mapGenerator.RequestMapData(position, OnMapDataReceived);
        }

        void OnMapDataReceived(MapData mapData)
        {
            this.mapData = mapData;
            mapDataReceived = true;

            Texture2D texture = TextureGenerator.TextureFromColourMap(mapData.colourMap, MapGenerator.mapChunkSize, MapGenerator.mapChunkSize);
            meshRenderer.material.mainTexture = texture;

            UpdateTerrainChunk();
        }

        public void UpdateTerrainChunk()
        {
            if (!mapDataReceived) return;

            float viewerDstFromNearestEdge = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));
            bool visible = viewerDstFromNearestEdge <= maxViewDst;

            if (visible)
            {
                int lodIndex = 0;
                for (int i = 0; i < detailLevels.Length - 1; i++)
                {
                    if (viewerDstFromNearestEdge > detailLevels[i].visibleDstThreshold)
                        lodIndex = i + 1;
                    else
                        break;
                }

                if (lodIndex != previousLODIndex)
                {
                    LODMesh lodMesh = lodMeshes[lodIndex];
                    if (lodMesh.hasMesh)
                    {
                        previousLODIndex = lodIndex;
                        meshFilter.mesh = lodMesh.mesh;
                    }
                    else if (!lodMesh.hasRequestedMesh)
                    {
                        lodMesh.RequestMesh(mapData);
                    }
                }

                terrainChunksVisibleLastUpdate.Add(this);
            }

            SetVisible(visible);
        }

        public void SetVisible(bool visible)
        {
            meshObject.SetActive(visible);
        }

        public bool IsVisible()
        {
            return meshObject.activeSelf;
        }
    }

    class LODMesh
    {
        public Mesh mesh;
        public bool hasRequestedMesh;
        public bool hasMesh;
        int lod;
        System.Action updateCallback;

        public LODMesh(int lod, System.Action updateCallback)
        {
            this.lod = lod;
            this.updateCallback = updateCallback;
        }

        void OnMeshDataReceived(MeshData meshData)
        {
            var vertices = meshData.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                if (float.IsNaN(v.y) || float.IsInfinity(v.y))
                {
                    v.y = 0f;
                }
                v.y = Mathf.Clamp(v.y, -1000f, 1000f);
                vertices[i] = v;
            }
            meshData.vertices = vertices;

            mesh = meshData.CreateMesh();
            hasMesh = true;
            updateCallback();
        }

        public void RequestMesh(MapData mapData)
        {
            hasRequestedMesh = true;
            mapGenerator.RequestMeshData(mapData, lod, OnMeshDataReceived);
        }
    }

    [System.Serializable]
    public struct LODInfo
    {
        public int lod;
        public float visibleDstThreshold;
    }
}
