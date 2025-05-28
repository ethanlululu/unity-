using UnityEngine;
using System.Collections.Generic;
public class EndlessTerrain : MonoBehaviour
{
    public const float maxViewDst = 300;
    public Transform viewer;

    public static Vector2 viewerPosiion;
    int chunkSize;
    int chunksVisibleInViewDst;

    Dictionary<Vector2, TerrainChunk> terrianChunkDictionary = new Dictionary<Vector2, TerrainChunk>();

    private void Start()
    {
        chunkSize = MapGenerator.MapChunkSize - 1;
        chunksVisibleInViewDst = Mathf.RoundToInt(maxViewDst) / chunkSize;
    }

    void UpdateVisibleChunks() { 

        int currentChunkCoordx = Mathf.RoundToInt(viewerPosiion.x / chunkSize);

        int currentChunkCoordy = Mathf.RoundToInt(viewerPosiion.y / chunkSize);

        for (int yOffset = -chunksVisibleInViewDst; yOffset <= chunksVisibleInViewDst; yOffset++)
        {
            for (int xOffset = -chunksVisibleInViewDst; xOffset <= chunksVisibleInViewDst; xOffset++)
            {
                Vector2 viewedChunkCoord = new Vector2(currentChunkCoordx + xOffset, currentChunkCoordy + yOffset);

                if (terrianChunkDictionary.ContainsKey(viewedChunkCoord))
                {

                }

                else { 
                
                    terrianChunkDictionary[viewedChunkCoord] = new TerrainChunk();

                }

            }

        }

    }
    public class TerrainChunk { 
    
    }
}
