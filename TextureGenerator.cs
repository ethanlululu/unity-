using UnityEngine;
using System.Collections;

public static class TextureGenerator
{

    public static Texture2D TextureFromColourMap(Color[] colourMap, int width, int height)
    {

        Texture2D texture = new Texture2D(width, height);

        texture.filterMode = FilterMode.Bilinear;     // 👈 Smooths the texture
        texture.wrapMode = TextureWrapMode.Clamp;     // Optional: prevents tiling edge artifacts

        texture.SetPixels(colourMap);
        texture.Apply();

        return texture;
    }


    public static Texture2D TextureFromHeightMap(float[,] heightMap)
    {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        Color[] colourMap = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float value = heightMap[x, y];
                colourMap[y * width + x] = Color.Lerp(Color.black, Color.white, value);
            }
        }

        Texture2D texture = new Texture2D(width, height);
        texture.filterMode = FilterMode.Bilinear;     // 👈 Smooths the visual output
        texture.wrapMode = TextureWrapMode.Clamp;

        texture.SetPixels(colourMap);
        texture.Apply(); // Apply after setting pixels

        return texture;
    }


}