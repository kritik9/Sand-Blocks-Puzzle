using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SandBlocks.Core.Data;
using SandBlocks.Gameplay;
using TMPro;

namespace SandBlocks.Puzzle.Vehicles
{
    public class BusVehicle : VehicleBase
    {
        [Header("Orientation & Alignment")]
        [Tooltip("Fixed in-plane puzzle rotation (0 = Right, 90 = Up, 180 = Left, 270 = Down)")]
        [Range(0, 360)]
        [SerializeField] private int rotationDegrees = 0;

        [Tooltip("Optional visual rotation offset in degrees")]
        [SerializeField] private Vector3 visualRotationOffset = Vector3.zero;

        [Header("Pre-placed Seat Passengers")]
        [Tooltip("Existing passenger GameObjects placed on the seats of the bus prefab. Enabled one-by-one as passengers board.")]
        [SerializeField] private List<GameObject> seatPassengers = new List<GameObject>();

        [Header("Visual Components")]
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Transform seatsParent;
        [SerializeField] private GameObject passengerPrefab;

        [Header("Characters Parent & Hierarchy")]
        [Tooltip("Parent transform where seated and boarded characters are stored")]
        [SerializeField] private Transform charactersParent;

        [Header("Seats Configuration")]
        [Tooltip("Physical seat transforms on this bus model. Visible characters will be positioned and oriented on these seats.")]
        [SerializeField] private List<Transform> seatTransforms = new List<Transform>();

        [Tooltip("Scale for characters when seated in the bus chairs")]
        [Range(0.02f, 1.0f)]
        [SerializeField] private float seatedCharacterScale = 0.14f;

        [Tooltip("Optional rotation offset applied to characters when sitting on a chair")]
        [SerializeField] private Vector3 seatedRotationOffset = Vector3.zero;

        [SerializeField] private TMP_Text passengerCountText;

        [Header("Passenger Settings")]
        [SerializeField] private float passengerScale = 0.1f;

        [Header("Visual Juice & Feedback")]
        [SerializeField] private float popScaleMultiplier = 1.14f;
        [SerializeField] private float blockedShakeMagnitude = 0.16f;

        [Header("Spawn Animation")]
        [Tooltip("Duration in seconds of the bus elastic pop-in animation on level start")]
        [Range(0.15f, 0.8f)]
        [SerializeField] private float spawnBounceDuration = 0.35f;
        [Tooltip("Overshoot amount for the elastic bounce (e.g. 1.7 = 15% overshoot)")]
        [Range(1.0f, 3.0f)]
        [SerializeField] private float spawnOvershoot = 1.70158f;

        private List<Vector3> seatLocalOffsets = new List<Vector3>();
        private List<GameObject> spawnedPassengers = new List<GameObject>();
        private int boardedPassengerCount = 0;
        private MaterialPropertyBlock propBlock;
        private CrowdColorPalette colorPalette;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId_Prop = Shader.PropertyToID("_Color");

        private Coroutine activePopRoutine;
        private Coroutine activeShakeRoutine;
        private Coroutine activeSpawnRoutine;
        private Vector3 originalLocalScale = Vector3.one;
        private bool hasSavedScale = false;
        private Vector3 visualShakeOffset = Vector3.zero;
        private float lastShakeTime = -1f;

        public Vector3 VisualShakeOffset => visualShakeOffset;
        public List<GameObject> SeatPassengers => seatPassengers;
        public int BoardedPassengerCount => boardedPassengerCount;
        public Transform CharactersParent => charactersParent;
        public List<Transform> SeatTransforms => seatTransforms;
        public int SeatCount => seatTransforms != null ? seatTransforms.Count : 0;
        public GameObject PassengerPrefab => passengerPrefab;

        public float SeatedCharacterScale
        {
            get => seatedCharacterScale;
            set => seatedCharacterScale = value;
        }

        public int RotationDegrees
        {
            get => rotationDegrees;
            set
            {
                rotationDegrees = Mathf.RoundToInt(value / 90f) * 90 % 360;
                if (rotationDegrees < 0) rotationDegrees += 360;
                isVertical = (rotationDegrees == 90 || rotationDegrees == 270);
                ApplyRotation();
            }
        }

        public void SetPassengerScale(float scale)
        {
            passengerScale = scale;
        }

        private void Awake()
        {
            EnsureCentered();
            ApplyRotation();
            AutoDetectHierarchyAndSeats();
            InitializeSeatPassengers();
            UpdatePassengerCountUI();
        }

        public void InitializeSeatPassengers()
        {
            AutoDetectHierarchyAndSeats();

            if (seatPassengers != null)
            {
                for (int i = 0; i < seatPassengers.Count; i++)
                {
                    if (seatPassengers[i] != null)
                    {
                        seatPassengers[i].SetActive(false);
                        EnsureSittingAnimation(seatPassengers[i]);
                        if (colorPalette != null)
                        {
                            ApplyColorToPassenger(seatPassengers[i], colorId);
                        }
                    }
                }
            }
        }

        public void EnsureCentered()
        {
            var boxCol = GetComponent<BoxCollider>();
            if (boxCol != null && Mathf.Abs(boxCol.center.z) > 0.05f)
            {
                float zOffset = boxCol.center.z;
                for (int i = 0; i < transform.childCount; i++)
                {
                    Transform child = transform.GetChild(i);
                    Vector3 lp = child.localPosition;
                    lp.z -= zOffset;
                    child.localPosition = lp;
                }

                Vector3 c = boxCol.center;
                c.z = 0f;
                boxCol.center = c;
            }
        }

        public void ApplyRotation()
        {
            isVertical = (rotationDegrees == 90 || rotationDegrees == 270);

            Vector3 forwardDir;
            switch (rotationDegrees)
            {
                case 90:  forwardDir = Vector3.up;    break;
                case 180: forwardDir = Vector3.left;  break;
                case 270: forwardDir = Vector3.down;  break;
                case 0:
                default:  forwardDir = Vector3.right; break;
            }

            Quaternion lookRot = Quaternion.LookRotation(forwardDir, Vector3.back);
            transform.rotation = lookRot * Quaternion.Euler(visualRotationOffset);
        }

        private void OnValidate()
        {
            rotationDegrees = Mathf.RoundToInt(rotationDegrees / 90f) * 90 % 360;
            if (rotationDegrees < 0) rotationDegrees += 360;
            EnsureCentered();
            ApplyRotation();
            AutoDetectHierarchyAndSeats();
            UpdatePassengerCountUI();
        }

        [ContextMenu("Detect Seats and Hierarchy")]
        public void AutoDetectHierarchyAndSeats()
        {
            // 1. Detect pre-placed seat passengers if empty
            if (seatPassengers == null) seatPassengers = new List<GameObject>();
            if (seatPassengers.Count == 0)
            {
                var allTransforms = GetComponentsInChildren<Transform>(true);
                var foundPassengers = new List<GameObject>();
                for (int i = 0; i < allTransforms.Length; i++)
                {
                    Transform t = allTransforms[i];
                    if (t == transform) continue;
                    string n = t.name.ToLower();
                    if (n.Contains("passenger") && !n.Contains("count") && !n.Contains("text") && !n.Contains("canvas"))
                    {
                        if (t.GetComponent<CharacterPassenger>() != null || t.GetComponent<Animator>() != null || t.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                        {
                            if (!foundPassengers.Contains(t.gameObject))
                            {
                                foundPassengers.Add(t.gameObject);
                            }
                        }
                    }
                }
                foundPassengers.Sort((a, b) =>
                {
                    string pA = a.transform.parent != null ? a.transform.parent.name : "";
                    string pB = b.transform.parent != null ? b.transform.parent.name : "";
                    int cmp = string.Compare(pA, pB, System.StringComparison.Ordinal);
                    if (cmp != 0) return cmp;
                    return a.transform.localPosition.x.CompareTo(b.transform.localPosition.x);
                });
                seatPassengers = foundPassengers;
            }

            // 2. Detect charactersParent if null
            if (charactersParent == null)
            {
                charactersParent = transform.Find("CharactersParent")
                    ?? transform.Find("CharacterParent")
                    ?? transform.Find("Characters Parent")
                    ?? transform.Find("Chaarcter Parent")
                    ?? transform.Find("Characters");
            }

            if (charactersParent == null)
            {
                var go = new GameObject("CharactersParent");
                go.transform.SetParent(transform, false);
                charactersParent = go.transform;
            }

            // 3. Detect seat transforms if empty
            if (seatTransforms == null) seatTransforms = new List<Transform>();
            if (seatTransforms.Count == 0)
            {
                Transform searchRoot = seatsParent != null ? seatsParent : transform;
                var all = searchRoot.GetComponentsInChildren<Transform>(true);
                var list = new List<Transform>();
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t == transform || t == searchRoot || t == charactersParent) continue;
                    string n = t.name.ToLower();
                    if (n.Contains("chair") || n.Contains("seat"))
                    {
                        list.Add(t);
                    }
                }
                list.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
                seatTransforms = list;
            }

            // 4. Detect passenger count text if null
            if (passengerCountText == null)
            {
                passengerCountText = GetComponentInChildren<TMP_Text>(true);
            }
        }

        public void UpdatePassengerCountUI()
        {
            if (passengerCountText == null)
            {
                passengerCountText = GetComponentInChildren<TMP_Text>(true);
            }

            if (passengerCountText != null)
            {
                int remaining = Mathf.Max(0, capacity - passengerCount);
                passengerCountText.text = remaining.ToString();
            }
        }

        public void ClearPassengers()
        {
            if (seatPassengers != null)
            {
                for (int i = 0; i < seatPassengers.Count; i++)
                {
                    if (seatPassengers[i] != null)
                    {
                        seatPassengers[i].SetActive(false);
                    }
                }
            }

            for (int i = 0; i < spawnedPassengers.Count; i++)
            {
                if (spawnedPassengers[i] != null)
                {
                    SafeDestroy(spawnedPassengers[i]);
                }
            }
            spawnedPassengers.Clear();

            if (charactersParent != null)
            {
                for (int i = charactersParent.childCount - 1; i >= 0; i--)
                {
                    SafeDestroy(charactersParent.GetChild(i).gameObject);
                }
            }

            boardedPassengerCount = 0;
            passengerCount = 0;
            UpdatePassengerCountUI();
        }

        public override void Initialize(int id, int cap, int len, bool vertical, Vector2Int startPos)
        {
            Initialize(id, cap, len, vertical ? 90 : 0, startPos);
        }

        public void Initialize(int id, int cap, int len, int rotDegrees, Vector2Int startPos)
        {
            colorId = id;
            capacity = cap;
            length = len;
            rotationDegrees = Mathf.RoundToInt(rotDegrees / 90f) * 90 % 360;
            if (rotationDegrees < 0) rotationDegrees += 360;
            isVertical = (rotationDegrees == 90 || rotationDegrees == 270);
            gridPosition = startPos;

            ClearPassengers();
            CleanupOldChildColliders();
            EnsureCentered();
            ApplyRotation();
            EnsureCollider();
            AutoDetectHierarchyAndSeats();
            UpdatePassengerCountUI();
            BuildSeatOffsets();
        }

        private void CleanupOldChildColliders()
        {
            Transform oldColChild = transform.Find("[VehicleCollider]");
            if (oldColChild != null)
            {
                SafeDestroy(oldColChild.gameObject);
            }
        }

        private void EnsureCollider()
        {
            // Do NOT add a new GameObject. Use collider present on bus or children (from prefab)
            var col = GetComponent<Collider>();
            if (col == null)
            {
                col = GetComponentInChildren<Collider>();
            }

            // Fallback: If no collider at all exists on the prefab, add BoxCollider directly to this GameObject
            if (col == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                // In local space: X = width, Y = height, Z = length
                box.size = new Vector3(1.2f, 1.0f, length * 1.35f);
                box.center = Vector3.zero;
            }
        }

        public void ApplyColor(CrowdColorPalette palette)
        {
            colorPalette = palette;
            if (palette == null) return;

            Color targetColor = palette.GetColor(colorId);
            if (propBlock == null) propBlock = new MaterialPropertyBlock();
            propBlock.SetColor(BaseColorId, targetColor);
            propBlock.SetColor(ColorId_Prop, targetColor);

            if (bodyRenderer == null)
            {
                var renderers = GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    string n = r.gameObject.name.ToLower();
                    if (n.Contains("bus") || n.Contains("body") || n.Contains("cube"))
                    {
                        bodyRenderer = r;
                        break;
                    }
                }
                if (bodyRenderer == null && renderers.Length > 0) bodyRenderer = renderers[0];
            }

            if (bodyRenderer != null)
            {
                bodyRenderer.SetPropertyBlock(propBlock);
            }

            if (seatPassengers != null)
            {
                for (int i = 0; i < seatPassengers.Count; i++)
                {
                    if (seatPassengers[i] != null)
                    {
                        ApplyColorToPassenger(seatPassengers[i], colorId);
                    }
                }
            }
        }

        private void BuildSeatOffsets()
        {
            seatLocalOffsets.Clear();

            if (seatsParent != null && seatsParent.childCount > 0)
            {
                var allChairs = new List<Transform>();
                for (int i = 0; i < seatsParent.childCount; i++)
                {
                    Transform child = seatsParent.GetChild(i);
                    string n = child.name.ToLower();
                    if (n.Contains("chair") || n.Contains("seat"))
                    {
                        allChairs.Add(child);
                    }
                }

                if (allChairs.Count > 0)
                {
                    for (int i = 0; i < allChairs.Count && i < capacity; i++)
                    {
                        seatLocalOffsets.Add(transform.InverseTransformPoint(allChairs[i].position));
                    }
                }
                else
                {
                    for (int i = 0; i < seatsParent.childCount && i < capacity; i++)
                    {
                        Transform child = seatsParent.GetChild(i);
                        if (child.GetComponent<Canvas>() == null)
                        {
                            seatLocalOffsets.Add(transform.InverseTransformPoint(child.position));
                        }
                    }
                }
            }

            if (seatLocalOffsets.Count == 0)
            {
                float spacing = 0.55f;
                float start = -((capacity - 1) * 0.5f) * spacing;
                for (int i = 0; i < capacity; i++)
                {
                    float offset = start + i * spacing;
                    // In local bus space: Z is along the length of the bus, Y is towards open roof
                    seatLocalOffsets.Add(new Vector3(0f, 0.1f, offset));
                }
            }
        }

        private static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private void InitScaleIfNeeded()
        {
            if (!hasSavedScale)
            {
                originalLocalScale = transform.localScale;
                if (originalLocalScale == Vector3.zero) originalLocalScale = Vector3.one;
                hasSavedScale = true;
            }
        }

        public void PlaySpawnAnimation(float delay = 0f)
        {
            InitScaleIfNeeded();
            if (activeSpawnRoutine != null) StopCoroutine(activeSpawnRoutine);
            if (gameObject.activeInHierarchy)
            {
                activeSpawnRoutine = StartCoroutine(SpawnBounceRoutine(delay));
            }
        }

        private IEnumerator SpawnBounceRoutine(float delay)
        {
            InitScaleIfNeeded();
            transform.localScale = Vector3.zero;

            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            float elapsed = 0f;
            while (elapsed < spawnBounceDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / spawnBounceDuration);

                // EaseOutBack curve for juicy elastic bounce: 0 -> 1.15 -> 1.0
                float c1 = spawnOvershoot;
                float c3 = c1 + 1f;
                float curve = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
                curve = Mathf.Max(0f, curve);

                transform.localScale = originalLocalScale * curve;
                yield return null;
            }

            transform.localScale = originalLocalScale;
            activeSpawnRoutine = null;
        }

        public void PlayPassengerBoardPop()
        {
            InitScaleIfNeeded();
            if (activePopRoutine != null) StopCoroutine(activePopRoutine);
            activePopRoutine = StartCoroutine(PassengerPopRoutine());
        }

        private IEnumerator PassengerPopRoutine()
        {
            float duration = 0.22f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float curve = Mathf.Sin(t * Mathf.PI);

                // Squash on Y, stretch on X and Z
                float sXZ = 1f + (popScaleMultiplier - 1f) * curve;
                float sY = 1f - (popScaleMultiplier - 1f) * 0.75f * curve;

                transform.localScale = new Vector3(originalLocalScale.x * sXZ, originalLocalScale.y * sY, originalLocalScale.z * sXZ);
                yield return null;
            }

            transform.localScale = originalLocalScale;
            activePopRoutine = null;
        }

        public void PlayBlockedShake(Vector3 dragDirection)
        {
            if (Time.time - lastShakeTime < 0.2f) return;
            lastShakeTime = Time.time;

            if (activeShakeRoutine != null) StopCoroutine(activeShakeRoutine);
            activeShakeRoutine = StartCoroutine(BlockedShakeRoutine(dragDirection));
        }

        private IEnumerator BlockedShakeRoutine(Vector3 dragDirection)
        {
            Vector3 shakeAxis;
            if (dragDirection.sqrMagnitude > 0.01f)
            {
                shakeAxis = dragDirection.normalized;
            }
            else
            {
                shakeAxis = isVertical ? Vector3.up : Vector3.right;
            }

            float duration = 0.22f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / duration;
                float decay = 1f - progress;
                float offset = Mathf.Sin(progress * Mathf.PI * 6f) * blockedShakeMagnitude * decay;

                visualShakeOffset = shakeAxis * offset;
                yield return null;
            }

            visualShakeOffset = Vector3.zero;
            activeShakeRoutine = null;
        }

        /// <summary>
        /// Reserves the next passenger slot on this bus.
        /// Returns true if not full, and outputs the target seat world position.
        /// </summary>
        public bool TryReserveSeat(out Vector3 targetSeatWorldPos)
        {
            targetSeatWorldPos = Vector3.zero;

            if (IsFull) return false;

            AutoDetectHierarchyAndSeats();

            int seatIndex = passengerCount;
            if (seatPassengers != null && seatIndex < seatPassengers.Count && seatPassengers[seatIndex] != null)
            {
                targetSeatWorldPos = seatPassengers[seatIndex].transform.position;
            }
            else if (seatTransforms != null && seatIndex < seatTransforms.Count && seatTransforms[seatIndex] != null)
            {
                targetSeatWorldPos = seatTransforms[seatIndex].position;
            }
            else
            {
                targetSeatWorldPos = transform.position + Vector3.up * 0.4f;
            }

            passengerCount++;
            UpdatePassengerCountUI();

            return true;
        }

        public bool TryReserveSeat(out Vector3 targetSeatWorldPos, out bool isVisibleSeat)
        {
            bool success = TryReserveSeat(out targetSeatWorldPos);
            isVisibleSeat = seatPassengers != null && (passengerCount - 1) < seatPassengers.Count;
            return success;
        }

        /// <summary>
        /// Boards a passenger into the bus.
        /// Enables the next pre-placed seat passenger, colors it, and ensures sitting animation.
        /// When bus capacity is reached, calls OnFull() to drive away.
        /// </summary>
        public void BoardPassenger()
        {
            AutoDetectHierarchyAndSeats();

            if (seatPassengers != null && boardedPassengerCount < seatPassengers.Count)
            {
                GameObject p = seatPassengers[boardedPassengerCount];
                if (p != null)
                {
                    p.SetActive(true);
                    ApplyColorToPassenger(p, colorId);
                    EnsureSittingAnimation(p);
                }
            }

            boardedPassengerCount++;
            PlayPassengerBoardPop();

            if (boardedPassengerCount >= capacity)
            {
                OnFull();
            }
        }

        public void BoardPassenger(GameObject commuter, bool isVisibleSeat = true)
        {
            if (commuter != null)
            {
                Destroy(commuter);
            }
            BoardPassenger();
        }

        public void ApplyColorToPassenger(GameObject p, int colorIdx)
        {
            if (p == null) return;
            if (colorPalette == null)
            {
                var gm = FindAnyObjectByType<Gameplay.GameManager>();
                if (gm != null && gm.Palette != null)
                {
                    colorPalette = gm.Palette;
                }
            }

            Color c = colorPalette != null ? colorPalette.GetColor(colorIdx) : Color.white;
            var charPass = p.GetComponent<CharacterPassenger>();
            if (charPass != null)
            {
                charPass.SetColor(c);
            }
            else
            {
                var renderers = p.GetComponentsInChildren<Renderer>(true);
                if (propBlock == null) propBlock = new MaterialPropertyBlock();
                propBlock.SetColor(BaseColorId, c);
                propBlock.SetColor(ColorId_Prop, c);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                    {
                        renderers[i].SetPropertyBlock(propBlock);
                    }
                }
            }
        }

        public void EnsureSittingAnimation(GameObject p)
        {
            if (p == null) return;
            var charPass = p.GetComponent<CharacterPassenger>();
            if (charPass != null && charPass.gameObject.activeInHierarchy)
            {
                charPass.PlaySitting();
            }
            else
            {
                var anim = p.GetComponentInChildren<Animator>(true);
                if (anim != null && p.activeInHierarchy)
                {
                    anim.SetBool("isRunning", false);
                    anim.SetBool("IsSitting", true);
                }
            }
        }

        public override bool TryAddPassenger(out Vector3 targetSeatWorldPos)
        {
            bool success = TryReserveSeat(out targetSeatWorldPos);
            if (success)
            {
                BoardPassenger();
            }
            return success;
        }

        public bool TryAddPassenger(out Vector3 targetSeatWorldPos, out Vector3 localSeatOffset)
        {
            localSeatOffset = Vector3.zero;
            bool success = TryReserveSeat(out targetSeatWorldPos);
            if (success)
            {
                localSeatOffset = transform.InverseTransformPoint(targetSeatWorldPos);
                BoardPassenger();
            }
            return success;
        }

        public override void OnFull()
        {
            var grid = FindAnyObjectByType<SandBlocks.Puzzle.Grid.PuzzleGrid>();
            if (grid != null)
            {
                grid.RemoveVehicle(this);
            }
            StartCoroutine(DriveAwayRoutine());
        }

        private void OnDestroy()
        {
            var grid = FindAnyObjectByType<SandBlocks.Puzzle.Grid.PuzzleGrid>();
            if (grid != null)
            {
                grid.RemoveVehicle(this);
            }
        }

        private IEnumerator DriveAwayRoutine()
        {
            yield return new WaitForSeconds(0.4f);

            Vector3 driveDir;
            switch (rotationDegrees)
            {
                case 90: driveDir = Vector3.up; break;
                case 180: driveDir = Vector3.left; break;
                case 270: driveDir = Vector3.down; break;
                default: driveDir = Vector3.right; break;
            }

            float elapsed = 0f;
            float duration = 0.8f;
            Vector3 startPos = transform.position;
            Vector3 endPos = startPos + driveDir * 15f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                t = t * t;
                transform.position = Vector3.Lerp(startPos, endPos, t);
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
