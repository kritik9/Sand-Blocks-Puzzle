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
        public const int MaxBusCapacity = 56;
        [Header("Orientation & Alignment")]
        [Tooltip("Fixed in-plane puzzle rotation (0 = Right, 90 = Up, 180 = Left, 270 = Down)")]
        [Range(0, 360)]
        [SerializeField] private int rotationDegrees = 0;

        [Tooltip("Optional visual rotation offset in degrees")]
        [SerializeField] private Vector3 visualRotationOffset = Vector3.zero;

        [Header("Passenger References (Assigned in Inspector)")]
        [Tooltip("Parent transform containing the passenger models on the bus. Children are used directly without any name lookup.")]
        [SerializeField] private Transform passengerParent;

        [Tooltip("Legacy fallback parent reference")]
        [SerializeField] private Transform charactersParent;

        [Tooltip("Optional direct list of passenger GameObjects. If assigned, this takes precedence over passengerParent.")]
        [SerializeField] private List<GameObject> passengerList = new List<GameObject>();

        [Header("Active Centered Passenger Slots")]
        [Tooltip("Active passenger GameObjects chosen for this bus (settled in center for capacity < 56).")]
        [SerializeField] private List<GameObject> seatPassengers = new List<GameObject>();

        [Header("Visual Components")]
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Transform seatsParent;
        [SerializeField] private GameObject passengerPrefab;

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

        private readonly List<GameObject> allRoofPassengers = new List<GameObject>();
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
        public System.Action<BusVehicle> OnBusFullOverride;
        public Transform PassengerParent { get => passengerParent != null ? passengerParent : charactersParent; set => passengerParent = value; }
        public Transform CharactersParent => PassengerParent;
        public List<GameObject> PassengerList { get => passengerList; set => passengerList = value; }
        public List<GameObject> SeatPassengers => seatPassengers;
        public int BoardedPassengerCount => boardedPassengerCount;
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
            SetupPassengerSlots();

            if (allRoofPassengers != null)
            {
                for (int i = 0; i < allRoofPassengers.Count; i++)
                {
                    if (allRoofPassengers[i] != null)
                    {
                        allRoofPassengers[i].SetActive(false);
                        var anim = allRoofPassengers[i].GetComponentInChildren<Animator>(true);
                        if (anim != null) anim.enabled = false;
                    }
                }
            }

            if (seatPassengers != null && colorPalette != null)
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

        [ContextMenu("Setup Passenger Slots")]
        public void SetupPassengerSlots()
        {
            allRoofPassengers.Clear();

            // 1. Collect all available passengers strictly from inspector fields (NO name searching)
            if (passengerList != null && passengerList.Count > 0)
            {
                for (int i = 0; i < passengerList.Count; i++)
                {
                    if (passengerList[i] != null) allRoofPassengers.Add(passengerList[i]);
                }
            }
            else
            {
                Transform parentToUse = passengerParent != null ? passengerParent : charactersParent;
                if (parentToUse != null)
                {
                    for (int i = 0; i < parentToUse.childCount; i++)
                    {
                        var child = parentToUse.GetChild(i);
                        if (child != null)
                        {
                            allRoofPassengers.Add(child.gameObject);
                        }
                    }
                }
            }

            if (allRoofPassengers.Count == 0)
            {
                seatPassengers = new List<GameObject>();
                return;
            }

            // 2. Disable animators on all models to eliminate animations
            for (int i = 0; i < allRoofPassengers.Count; i++)
            {
                var anim = allRoofPassengers[i].GetComponentInChildren<Animator>(true);
                if (anim != null) anim.enabled = false;
            }

            // 3. Clear legacy chair transforms
            if (seatTransforms != null && seatTransforms.Count > 0)
            {
                seatTransforms.Clear();
            }

            // 4. If capacity >= all available models, use all models sorted front-to-back, left-to-right
            int targetCount = Mathf.Clamp(capacity, 1, allRoofPassengers.Count);
            if (targetCount >= allRoofPassengers.Count)
            {
                var allSorted = new List<GameObject>(allRoofPassengers);
                allSorted.Sort((a, b) =>
                {
                    float dz = b.transform.localPosition.z - a.transform.localPosition.z;
                    if (Mathf.Abs(dz) > 0.05f) return dz.CompareTo(0f);
                    return a.transform.localPosition.x.CompareTo(b.transform.localPosition.x);
                });
                seatPassengers = allSorted;
                return;
            }

            // 5. Centered selection for capacity < max capacity:
            // Settle passengers in center by choosing the models closest to geometric center
            Vector3 center = Vector3.zero;
            for (int i = 0; i < allRoofPassengers.Count; i++)
            {
                center += allRoofPassengers[i].transform.localPosition;
            }
            center /= allRoofPassengers.Count;

            var scored = new List<System.Tuple<float, GameObject>>();
            for (int i = 0; i < allRoofPassengers.Count; i++)
            {
                var p = allRoofPassengers[i];
                Vector3 lp = p.transform.localPosition;
                float dz = Mathf.Abs(lp.z - center.z);
                float dx = Mathf.Abs(lp.x - center.x);
                float distScore = dz * 1.0f + dx * 0.05f;
                scored.Add(new System.Tuple<float, GameObject>(distScore, p));
            }

            scored.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            // Choose the exact 'capacity' passengers closest to center
            var chosen = new List<GameObject>();
            for (int i = 0; i < targetCount; i++)
            {
                chosen.Add(scored[i].Item2);
            }

            // Explicitly deactivate any unchosen passenger model so only the center ones can be visible
            for (int i = targetCount; i < scored.Count; i++)
            {
                var unchosen = scored[i].Item2;
                if (unchosen != null)
                {
                    unchosen.SetActive(false);
                    var anim = unchosen.GetComponentInChildren<Animator>(true);
                    if (anim != null) anim.enabled = false;
                }
            }

            // Sort chosen center passengers front-to-back, left-to-right for orderly fill
            chosen.Sort((a, b) =>
            {
                float dz = b.transform.localPosition.z - a.transform.localPosition.z;
                if (Mathf.Abs(dz) > 0.05f) return dz.CompareTo(0f);
                return a.transform.localPosition.x.CompareTo(b.transform.localPosition.x);
            });

            seatPassengers = chosen;
        }

        // Backwards compatibility alias
        public void AutoDetectHierarchyAndSeats()
        {
            SetupPassengerSlots();
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
            // Deactivate all available roof passengers
            if (allRoofPassengers != null)
            {
                for (int i = 0; i < allRoofPassengers.Count; i++)
                {
                    if (allRoofPassengers[i] != null)
                    {
                        allRoofPassengers[i].SetActive(false);
                        var anim = allRoofPassengers[i].GetComponentInChildren<Animator>(true);
                        if (anim != null) anim.enabled = false;
                    }
                }
            }
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
            capacity = Mathf.Clamp(cap, 1, MaxBusCapacity);
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
            if (palette != null) colorPalette = palette;
            if (colorPalette == null)
            {
                var gm = FindAnyObjectByType<Gameplay.GameManager>();
                if (gm != null && gm.Palette != null) colorPalette = gm.Palette;
            }
#if UNITY_EDITOR
            if (colorPalette == null)
            {
                colorPalette = UnityEditor.AssetDatabase.LoadAssetAtPath<CrowdColorPalette>("Assets/Sand Block Puzzle/Settings/CrowdColorPalette.asset");
            }
#endif
            Color targetColor = colorPalette != null ? colorPalette.GetColor(colorId) : GetColorFallback(colorId);
            if (propBlock == null) propBlock = new MaterialPropertyBlock();
            propBlock.SetColor(BaseColorId, targetColor);
            propBlock.SetColor(ColorId_Prop, targetColor);

            var allRenderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRenderers)
            {
                if (r == null) continue;
                string n = r.gameObject.name.ToLower();
                if (n.Contains("chair") || n.Contains("seat") || n.Contains("passenger") || n.Contains("text") || n.Contains("canvas")) continue;
                r.SetPropertyBlock(propBlock);
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


        private Color GetColorFallback(int id)
        {
            Color[] fallbacks = new Color[]
            {
                new Color(0.18f, 0.55f, 1f, 1f),
                new Color(0.2f, 0.85f, 0.35f, 1f),
                new Color(1f, 0.82f, 0.1f, 1f),
                new Color(1f, 0.35f, 0.65f, 1f),
                new Color(0.18f, 0.18f, 0.22f, 1f),
                new Color(0.92f, 0.92f, 0.95f, 1f),
                new Color(1f, 0.5f, 0.1f, 1f),
                new Color(1f, 0.208f, 0.102f, 1f),
                new Color(0.102f, 1f, 0.674f, 1f)
            };
            return (id >= 0 && id < fallbacks.Length) ? fallbacks[id] : Color.white;
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

        /// <summary>
        /// Cancels any active spawn bounce animation and immediately restores the bus to full scale.
        /// </summary>
        public void CancelSpawnAnimation()
        {
            InitScaleIfNeeded();
            if (activeSpawnRoutine != null)
            {
                StopCoroutine(activeSpawnRoutine);
                activeSpawnRoutine = null;
            }
            transform.localScale = originalLocalScale;
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

            SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusBlocked();
            SandBlocks.Core.Haptics.HapticManager.Instance?.HeavyImpact();

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

            if (seatPassengers == null || seatPassengers.Count == 0)
            {
                SetupPassengerSlots();
            }

            int seatIndex = passengerCount;
            if (seatPassengers != null && seatIndex < seatPassengers.Count && seatPassengers[seatIndex] != null)
            {
                targetSeatWorldPos = seatPassengers[seatIndex].transform.position;
            }
            else
            {
                targetSeatWorldPos = transform.position + Vector3.up * 1.0f;
            }

            passengerCount++;
            UpdatePassengerCountUI();

            return true;
        }

        public bool TryReserveSeat(out Vector3 targetSeatWorldPos, out bool isVisibleSeat)
        {
            bool success = TryReserveSeat(out targetSeatWorldPos);
            isVisibleSeat = true;
            return success;
        }

        /// <summary>
        /// Boards a passenger into the bus.
        /// Reveals passenger models on the roof container according to exact capacity and boarded count.
        /// When bus capacity is reached, calls OnFull() to drive away.
        /// </summary>
        public void BoardPassenger()
        {
            if (seatPassengers == null || seatPassengers.Count == 0)
            {
                SetupPassengerSlots();
            }

            boardedPassengerCount++;
            UpdatePassengerVisuals();
            PlayPassengerBoardPop();

            SandBlocks.Core.Audio.AudioManager.Instance?.PlayPassengerBoard();
            SandBlocks.Core.Haptics.HapticManager.Instance?.LightImpact();

            if (boardedPassengerCount >= capacity)
            {
                OnFull();
            }
        }

        /// <summary>
        /// Unboards one passenger from the bus, hiding its visual roof seat and returning its launch position.
        /// </summary>
        public bool TryUnboardPassenger(out Vector3 seatWorldPos)
        {
            seatWorldPos = transform.position;
            if (passengerCount <= 0 && boardedPassengerCount <= 0) return false;

            int lastIdx = Mathf.Max(boardedPassengerCount - 1, passengerCount - 1);
            if (seatPassengers != null && lastIdx >= 0 && lastIdx < seatPassengers.Count && seatPassengers[lastIdx] != null)
            {
                seatWorldPos = seatPassengers[lastIdx].transform.position;
            }

            if (passengerCount > 0) passengerCount--;
            if (boardedPassengerCount > 0) boardedPassengerCount--;

            UpdatePassengerVisuals();
            UpdatePassengerCountUI();
            return true;
        }

        /// <summary>
        /// Shows the exact number of boarded passengers on the bus (up to capacity).
        /// Only the chosen center seats become visible as passengers board.
        /// </summary>
        public void UpdatePassengerVisuals()
        {
            if (seatPassengers == null || seatPassengers.Count == 0)
            {
                SetupPassengerSlots();
            }
            if (seatPassengers == null || seatPassengers.Count == 0) return;

            // Show exact boarded count on the bus, never exceeding capacity
            int targetVisibleCount = Mathf.Clamp(boardedPassengerCount, 0, seatPassengers.Count);

            for (int i = 0; i < seatPassengers.Count; i++)
            {
                var p = seatPassengers[i];
                if (p == null) continue;

                bool shouldBeVisible = (i < targetVisibleCount);
                if (p.activeSelf != shouldBeVisible)
                {
                    p.SetActive(shouldBeVisible);
                }

                if (shouldBeVisible)
                {
                    ApplyColorToPassenger(p, colorId);
                    var anim = p.GetComponentInChildren<Animator>(true);
                    if (anim != null && anim.enabled)
                    {
                        anim.enabled = false;
                    }
                }
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
            // Animation removed as requested; disable any animator on bus passengers
            if (p == null) return;
            var anim = p.GetComponentInChildren<Animator>(true);
            if (anim != null) anim.enabled = false;
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
            SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusFull();
            SandBlocks.Core.Haptics.HapticManager.Instance?.MediumImpact();

            if (OnBusFullOverride != null)
            {
                OnBusFullOverride.Invoke(this);
                return;
            }

            if (SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance != null &&
                SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance.IsUnifiedActive &&
                SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance.CurrentUnifiedMode == SandBlocks.Core.Data.UnifiedMode.DestinationDropOff)
            {
                // In DestinationDropOff mode, the bus stays on the grid for manual player driving to the destination
                return;
            }
            // Classic mode drive away:
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

            SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusDriveAway();
            SandBlocks.Core.Haptics.HapticManager.Instance?.MediumImpact();

            Vector3 driveDir;
            switch (rotationDegrees)
            {
                case 90: driveDir = Vector3.up; break;
                case 180: driveDir = Vector3.left; break;
                case 270: driveDir = Vector3.down; break;
                default: driveDir = Vector3.right; break;
            }

            var grid = FindAnyObjectByType<SandBlocks.Puzzle.Grid.PuzzleGrid>();
            Vector3 worldDriveDir = (grid != null) ? grid.transform.TransformDirection(driveDir) : driveDir;

            float elapsed = 0f;
            float duration = 0.8f;
            Vector3 startPos = transform.position;
            Vector3 endPos = startPos + worldDriveDir * 15f;

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
