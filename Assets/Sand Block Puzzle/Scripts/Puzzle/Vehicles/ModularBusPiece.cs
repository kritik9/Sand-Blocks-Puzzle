using System.Collections.Generic;
using UnityEngine;

namespace SandBlocks.Puzzle.Vehicles
{
    public enum BusPieceType
    {
        Front,
        Mid,
        Rear,
        Corner
    }

    /// <summary>
    /// Component placed on modular bus piece prefabs (Bus_Front, Bus_Mid, Bus_Rear).
    /// Holds the piece's type, passenger capacity, and seated passenger character references.
    /// </summary>
    [DisallowMultipleComponent]
    public class ModularBusPiece : MonoBehaviour
    {
        [Header("Piece Configuration")]
        [Tooltip("Type of modular bus piece")]
        [SerializeField] private BusPieceType pieceType = BusPieceType.Mid;

        [Tooltip("Passenger capacity for this piece (e.g. Front: 20, Mid: 25, Rear: 20)")]
        [SerializeField] private int pieceCapacity = 25;

        [Header("Passengers / Seats")]
        [Tooltip("Transform holding the seated passenger character models for this piece")]
        [SerializeField] private Transform passengersParent;

        [Tooltip("Explicit list of seated passenger GameObjects. If empty, collected from passengersParent.")]
        [SerializeField] private List<GameObject> passengers = new List<GameObject>();

        [Header("Auto-Generation Settings")]
        [Tooltip("Character prefab to spawn when auto-generating seated passengers")]
        [SerializeField] private GameObject characterPrefab;

        [Tooltip("Scale of seated passenger characters (default 0.14f)")]
        [SerializeField] private float characterScale = 0.14f;

        [Tooltip("Y elevation for seated characters relative to piece center")]
        [SerializeField] private float seatYOffset = 0.55f;

        public BusPieceType PieceType => pieceType;
        public int PieceCapacity { get => pieceCapacity; set => pieceCapacity = Mathf.Max(0, value); }
        public Transform PassengersParent { get => passengersParent; set => passengersParent = value; }
        public List<GameObject> Passengers => passengers;

        private void Reset()
        {
            string nm = gameObject.name.ToLower();
            if (nm.Contains("front"))
            {
                pieceType = BusPieceType.Front;
                pieceCapacity = 20;
            }
            else if (nm.Contains("rear") || nm.Contains("back"))
            {
                pieceType = BusPieceType.Rear;
                pieceCapacity = 20;
            }
            else
            {
                pieceType = BusPieceType.Mid;
                pieceCapacity = 25;
            }
        }

        /// <summary>
        /// Returns all seated passenger GameObjects on this piece.
        /// </summary>
        public List<GameObject> GetPassengers()
        {
            if (passengers != null && passengers.Count > 0)
            {
                // Filter out any null entries
                var valid = new List<GameObject>();
                for (int i = 0; i < passengers.Count; i++)
                {
                    if (passengers[i] != null) valid.Add(passengers[i]);
                }
                if (valid.Count > 0) return valid;
            }

            Transform container = passengersParent != null ? passengersParent : transform.Find("Passengers");
            if (container == null) container = transform.Find("Seats");

            var result = new List<GameObject>();
            if (container != null)
            {
                for (int i = 0; i < container.childCount; i++)
                {
                    var child = container.GetChild(i);
                    if (child != null) result.Add(child.gameObject);
                }
            }
            return result;
        }

#if UNITY_EDITOR
        [ContextMenu("Auto-Generate Seated Passengers Grid")]
        public void GeneratePassengerGrid()
        {
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(gameObject))
            {
                string path = UnityEditor.AssetDatabase.GetAssetPath(gameObject);
                if (!string.IsNullOrEmpty(path))
                {
                    GameObject root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
                    var piece = root.GetComponent<ModularBusPiece>();
                    if (piece != null)
                    {
                        piece.GeneratePassengerGridInternal();
                        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
                        Debug.Log($"[ModularBusPiece] Generated seated passengers for Prefab Asset at {path}");
                    }
                    UnityEditor.PrefabUtility.UnloadPrefabContents(root);
                    return;
                }
            }

            GeneratePassengerGridInternal();
        }

        private void GeneratePassengerGridInternal()
        {
            if (characterPrefab == null)
            {
                characterPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Sand Block Puzzle/Prefabs/Character/CharacterModel.prefab");
            }

            Transform container = passengersParent;
            if (container == null)
            {
                container = transform.Find("Passengers");
                if (container == null)
                {
                    var go = new GameObject("Passengers");
                    go.transform.SetParent(transform, false);
                    container = go.transform;
                }
                passengersParent = container;
            }

            // Clear old children
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(container.GetChild(i).gameObject, true);
            }
            passengers.Clear();

            int targetCount = pieceCapacity > 0 ? pieceCapacity : 20;
            // Determine grid: e.g. 20 -> 4 rows x 5 cols, 25 -> 5 rows x 5 cols
            int cols = (targetCount == 25) ? 5 : 4;
            int rows = Mathf.CeilToInt((float)targetCount / cols);

            // Piece bounds along local X (~ -0.45 to +0.45) and local Z (~ -0.55 to +0.55)
            float xMin = -0.42f;
            float xMax = 0.42f;
            float zMin = -0.52f;
            float zMax = 0.52f;

            int spawned = 0;
            for (int r = 0; r < rows && spawned < targetCount; r++)
            {
                float tz = rows > 1 ? (float)r / (rows - 1) : 0.5f;
                float z = Mathf.Lerp(zMax, zMin, tz);

                for (int c = 0; c < cols && spawned < targetCount; c++)
                {
                    float tx = cols > 1 ? (float)c / (cols - 1) : 0.5f;
                    float x = Mathf.Lerp(xMin, xMax, tx);

                    Vector3 localPos = new Vector3(x, seatYOffset, z);
                    GameObject pGo;
                    if (characterPrefab != null)
                    {
                        pGo = Instantiate(characterPrefab, container);
                    }
                    else
                    {
                        pGo = new GameObject($"Seat_{spawned + 1}");
                        pGo.transform.SetParent(container, false);
                    }

                    pGo.name = $"Passenger_{spawned + 1:D2}";
                    pGo.transform.localPosition = localPos;
                    pGo.transform.localRotation = Quaternion.identity;
                    pGo.transform.localScale = Vector3.one * characterScale;

                    var anim = pGo.GetComponentInChildren<Animator>(true);
                    if (anim != null) anim.enabled = false;

                    var charPass = pGo.GetComponent<Gameplay.CharacterPassenger>();
                    if (charPass != null)
                    {
                        charPass.SetScale(characterScale);
                        charPass.PlaySitting();
                    }

                    pGo.SetActive(false); // Initially hidden, will show as commuters board
                    passengers.Add(pGo);
                    spawned++;
                }
            }

            UnityEditor.EditorUtility.SetDirty(gameObject);
            Debug.Log($"[ModularBusPiece] Generated {spawned} seated passengers on {gameObject.name}.");
        }

        [ContextMenu("Clear Seated Passengers")]
        public void ClearPassengers()
        {
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(gameObject))
            {
                string path = UnityEditor.AssetDatabase.GetAssetPath(gameObject);
                if (!string.IsNullOrEmpty(path))
                {
                    GameObject root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
                    var piece = root.GetComponent<ModularBusPiece>();
                    if (piece != null)
                    {
                        piece.ClearPassengersInternal();
                        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
                        Debug.Log($"[ModularBusPiece] Cleared seated passengers for Prefab Asset at {path}");
                    }
                    UnityEditor.PrefabUtility.UnloadPrefabContents(root);
                    return;
                }
            }

            ClearPassengersInternal();
        }

        private void ClearPassengersInternal()
        {
            Transform container = passengersParent != null ? passengersParent : transform.Find("Passengers");
            if (container != null)
            {
                for (int i = container.childCount - 1; i >= 0; i--)
                {
                    DestroyImmediate(container.GetChild(i).gameObject, true);
                }
            }
            passengers.Clear();
            UnityEditor.EditorUtility.SetDirty(gameObject);
        }
#endif
    }
}
