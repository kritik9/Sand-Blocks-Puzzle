using System;
using UnityEngine;

namespace SandBlocks.Core.Data
{
    [Serializable]
    public struct ColorEntry
    {
        public int id;
        public string name;
        public Color color;
    }

    [CreateAssetMenu(fileName = "CrowdColorPalette", menuName = "SandBlocks/CrowdColorPalette")]
    public class CrowdColorPalette : ScriptableObject
    {
        [SerializeField] private ColorEntry[] entries = new ColorEntry[]
        {
            new ColorEntry { id = 0, name = "Blue", color = new Color(0.18f, 0.55f, 1f, 1f) },
            new ColorEntry { id = 1, name = "Green", color = new Color(0.2f, 0.85f, 0.35f, 1f) },
            new ColorEntry { id = 2, name = "Yellow", color = new Color(1f, 0.82f, 0.1f, 1f) },
            new ColorEntry { id = 3, name = "Pink", color = new Color(1f, 0.35f, 0.65f, 1f) },
            new ColorEntry { id = 4, name = "Black", color = new Color(0.18f, 0.18f, 0.22f, 1f) },
            new ColorEntry { id = 5, name = "White", color = new Color(0.92f, 0.92f, 0.95f, 1f) },
            new ColorEntry { id = 6, name = "Orange", color = new Color(1f, 0.5f, 0.1f, 1f) }
        };

        public Color GetColor(int id)
        {
            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].id == id) return entries[i].color;
                }
            }
            return Color.white;
        }

        public string GetColorName(int id)
        {
            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].id == id) return entries[i].name;
                }
            }
            return $"Color {id}";
        }

        public int Count => entries != null ? entries.Length : 0;
        public ColorEntry[] Entries => entries;

        public void AddColor(string name, Color color)
        {
            int nextId = 0;
            if (entries != null && entries.Length > 0)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].id >= nextId) nextId = entries[i].id + 1;
                }
                Array.Resize(ref entries, entries.Length + 1);
                entries[entries.Length - 1] = new ColorEntry { id = nextId, name = name, color = color };
            }
            else
            {
                entries = new ColorEntry[] { new ColorEntry { id = 0, name = name, color = color } };
            }
        }
    }
}
