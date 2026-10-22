using System;
using System.Collections.Generic;
using UnityEngine;

namespace SandBlocks.Core.Data
{
    [Serializable]
    public struct ColorLayerConfig
    {
        public string label;
        public int colorId;
        public float maxY;
    }

    [Serializable]
    public struct BusSpawnData
    {
        public string name;
        public int colorId;
        public int capacity;
        public int length;
        public bool isVertical;
        public Vector2Int gridPosition;
    }

    [CreateAssetMenu(fileName = "Level_01", menuName = "SandBlocks/Level Data")]
    public class LevelData : ScriptableObject
    {
        [Header("General Settings")]
        public string levelName = "Level 1";
        public float particleRadius = 0.18f;
        public int maxParticles = 600;

        [Header("Crowd Color Layers (Ordered from Bottom to Top)")]
        public List<ColorLayerConfig> colorLayers = new List<ColorLayerConfig>
        {
            new ColorLayerConfig { label = "Blue (Bottom)", colorId = 0, maxY = -1.6f },
            new ColorLayerConfig { label = "Green",         colorId = 1, maxY = -0.8f },
            new ColorLayerConfig { label = "Yellow",        colorId = 2, maxY =  0.2f },
            new ColorLayerConfig { label = "Pink",          colorId = 3, maxY =  1.4f },
            new ColorLayerConfig { label = "Black (Top)",   colorId = 4, maxY =  3.0f }
        };

        [Header("Optional Image Color Map (Pixel Art)")]
        [Tooltip("If assigned, spawns crowd colors sampled directly from this texture")]
        public Texture2D colorMapTexture;

        [Header("Puzzle Grid Buses")]
        public List<BusSpawnData> buses = new List<BusSpawnData>
        {
            new BusSpawnData { name = "Blue Bus",   colorId = 0, capacity = 4, length = 2, isVertical = false, gridPosition = new Vector2Int(0, 4) },
            new BusSpawnData { name = "Yellow Bus", colorId = 2, capacity = 4, length = 2, isVertical = false, gridPosition = new Vector2Int(3, 4) },
            new BusSpawnData { name = "Green Bus",  colorId = 1, capacity = 4, length = 2, isVertical = true,  gridPosition = new Vector2Int(0, 1) },
            new BusSpawnData { name = "Pink Bus",   colorId = 3, capacity = 4, length = 2, isVertical = false, gridPosition = new Vector2Int(2, 2) },
            new BusSpawnData { name = "Black Bus",  colorId = 4, capacity = 6, length = 3, isVertical = false, gridPosition = new Vector2Int(1, 0) }
        };

        public int GetColorForPosition(float y, float x = 0f)
        {
            for (int i = 0; i < colorLayers.Count; i++)
            {
                if (y <= colorLayers[i].maxY)
                {
                    return colorLayers[i].colorId;
                }
            }
            return colorLayers.Count > 0 ? colorLayers[colorLayers.Count - 1].colorId : 0;
        }
    }
}
