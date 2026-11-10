using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SandBlocks.Core.Data;
using SandBlocks.Gameplay;
using TMPro;

namespace SandBlocks.Puzzle.Vehicles
{
    public enum BusSeatFillOrder
    {
        FillFromPlayerToFirstRow = 0,
        FillFromFirstRowToPlayer = 1
    }

    public enum BusDepartureMode
    {
        Disappear = 0,
        DriveAway = 1
    }

    public class BusVehicle : VehicleBase
    {
        public const int MaxBusCapacity = 256;
        public const int DefaultFrontPieceCapacity = 9;
        public const int DefaultMidPieceCapacity = 12;
        public const int DefaultRearPieceCapacity = 9;

        /// <summary>
        /// Calculates modular bus passenger capacity based on piece count:
        /// Size 2: Front (9) + Rear (9) = 18
        /// Size 3: Front (9) + Mid (12) + Rear (9) = 30
        /// Size 4: Front (9) + 2xMid (24) + Rear (9) = 42, etc.
        /// </summary>
        public static int CalculateModularCapacity(int busLength, int frontCap = DefaultFrontPieceCapacity, int midCap = DefaultMidPieceCapacity, int rearCap = DefaultRearPieceCapacity)
        {
            int len = Mathf.Max(2, busLength);
            if (len == 2) return frontCap + rearCap;
            return frontCap + (len - 2) * midCap + rearCap;
        }
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

                [Header("Modular Bus Pieces (Assigned in Inspector)")]
        [Tooltip("Prefab or model reference for the front section of the modular bus (assigned in Inspector)")]
        [SerializeField] private GameObject frontPiecePrefab;

        [Tooltip("Prefab or model reference for the middle section(s) of the modular bus (assigned in Inspector)")]
        [SerializeField] private GameObject midPiecePrefab;

        [Tooltip("Prefab or model reference for the rear section of the modular bus (assigned in Inspector)")]
        [SerializeField] private GameObject rearPiecePrefab;

        [Tooltip("Parent transform to hold spawned bus pieces (assigned in Inspector)")]
        [SerializeField] private Transform piecesParent;

        [Tooltip("Length in units of a single modular bus piece along local Z (matches grid cell size 1.35f)")]
        [SerializeField] private float pieceLength = 1.35f;

        [Tooltip("List of currently spawned modular piece instances")]
        [SerializeField] private List<GameObject> spawnedPieces = new List<GameObject>();

        [Header("Renderers & Materials (Assigned in Inspector)")]
        [Tooltip("List of body renderers to tint with the bus color. If empty, automatically populated from spawned pieces.")]
        [SerializeField] private List<Renderer> bodyRenderers = new List<Renderer>();

        [Tooltip("Legacy fallback body renderer reference")]
        [SerializeField] private Renderer bodyRenderer;

        [Tooltip("Material slot index of the bus body to be colored (default 0).")]
        [SerializeField] private int bodyMaterialIndex = 0;

        [Tooltip("Optional custom material to use for the bus body (e.g. toon shader material).")]
        [SerializeField] private Material customBodyMaterial;

        [SerializeField] private Transform seatsParent;
        [SerializeField] private GameObject passengerPrefab;

        [Header("Headlight & Taillight Settings (Assigned in Inspector)")]
        [Tooltip("Enable realistic headlights that glow only when selected, blink on passenger fill, and light up during departure.")]
        [SerializeField] private bool enableHeadlightAnimation = true;

        [Tooltip("Headlight renderer reference (assigned in Inspector or auto-referenced from front piece).")]
        [SerializeField] private Renderer headlightRenderer;

        [Tooltip("Material slot index of the headlight on the headlight renderer (default 2 for modular piece, 4 for legacy).")]
        [SerializeField] private int headlightMaterialIndex = 2;

        [Tooltip("Maximum emission multiplier for headlight effects.")]
        [Range(0.1f, 4.0f)]
        [SerializeField] private float maxIntensityMultiplier = 2.5f;

        [Tooltip("Base speed of the subtle engine alternator noise while glowing.")]
        [Range(0.02f, 5.0f)]
        [SerializeField] private float baseFlickerSpeed = 1.8f;

        [Header("Selection & Crowd Fill Timings")]
        [Tooltip("Headlights glow when the bus is selected.")]
        [SerializeField] private bool highlightWhenSelected = true;

        [Tooltip("Target glow intensity when the bus is selected.")]
        [Range(0.1f, 3.0f)]
        [SerializeField] private float selectedGlowIntensity = 1.45f;

        [Tooltip("Blink the headlights when a crowd passenger fills into the bus.")]
        [SerializeField] private bool blinkOnPassengerFill = true;

        [Tooltip("Peak intensity multiplier during a passenger fill headlight blink.")]
        [Range(0.1f, 4.0f)]
        [SerializeField] private float passengerFillBlinkIntensity = 2.2f;

        [Tooltip("Duration in seconds of the headlight blink on passenger fill.")]
        [Range(0.04f, 0.5f)]
        [SerializeField] private float passengerFillBlinkDuration = 0.14f;

        [Tooltip("Boost headlights to bright when the bus is driving away.")]
        [SerializeField] private bool highlightWhenMoving = true;

        [Tooltip("Optional custom override for base emission color. Leave clear (alpha 0) to use material default.")]
        [SerializeField] private Color customHeadlightEmissionColor = Color.clear;

        [Header("Optional Taillight Settings (Assigned in Inspector)")]
        [Tooltip("Enable subtle breathing/glow on red taillights.")]
        [SerializeField] private bool enableTaillightAnimation = true;

        [Tooltip("Taillight renderer reference (assigned in Inspector or auto-referenced from rear piece).")]
        [SerializeField] private Renderer taillightRenderer;

        [Tooltip("Material slot index of the taillight on the taillight renderer (default 2 for modular piece, 6 for legacy).")]
        [SerializeField] private int taillightMaterialIndex = 2;

        [Header("Seat Fill Order")]
        [Tooltip("FillFromPlayerToFirstRow: Seats away from first row (near player) fill first. FillFromFirstRowToPlayer: Seats near first row fill first.")]
        [SerializeField] private BusSeatFillOrder seatFillOrder = BusSeatFillOrder.FillFromPlayerToFirstRow;

        public BusSeatFillOrder SeatFillOrder
        {
            get => seatFillOrder;
            set { seatFillOrder = value; SetupPassengerSlots(); }
        }

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
        [Tooltip("Toggle the bus squash-and-stretch pop animation when a passenger fills into the bus")]
        [SerializeField] private bool enablePassengerBoardPop = true;
        [SerializeField] private float popScaleMultiplier = 1.14f;
        [Tooltip("Duration of the bus pop bounce animation when a passenger boards (seconds)")]
        [Range(0.05f, 0.6f)]
        [SerializeField] private float passengerPopDuration = 0.20f;
        [SerializeField] private float blockedShakeMagnitude = 0.16f;

        [Header("Spawn Animation")]
        [Tooltip("Duration in seconds of the bus elastic pop-in animation on level start")]
        [Range(0.15f, 0.8f)]
        [SerializeField] private float spawnBounceDuration = 0.35f;
        [Tooltip("Overshoot amount for the elastic bounce (e.g. 1.7 = 15% overshoot)")]
        [Range(1.0f, 3.0f)]
        [SerializeField] private float spawnOvershoot = 1.70158f;

        [Header("Departure Animation")]
        [Tooltip("Departure mode when bus is full: Disappear in place or DriveAway off-screen")]
        [SerializeField] private BusDepartureMode departureMode = BusDepartureMode.Disappear;

        [Header("Departure Visual Effects")]
        [Tooltip("Optional particle system to play when the bus is full/departing (assigned in Inspector)")]
        [SerializeField] private ParticleSystem fullVFX;

        [Tooltip("Pause delay in seconds after the bus is full before departure begins")]
        [Range(0.0f, 1.5f)]
        [SerializeField] private float driveAwayDelay = 0.15f;

        [Tooltip("Duration in seconds for the bus to drive off-screen (used in DriveAway mode)")]
        [Range(0.1f, 2.5f)]
        [SerializeField] private float driveAwayDuration = 0.5f;

        [Tooltip("Distance the bus travels off-screen during departure before being destroyed")]
        [SerializeField] private float driveAwayDistance = 15f;

        [Tooltip("Duration in seconds of the disappearance squash-pop-shrink animation (used in Disappear mode)")]
        [Range(0.15f, 1.5f)]
        [SerializeField] private float disappearDuration = 0.42f;

        [Tooltip("Anticipation hop height along normal/up direction during disappearance")]
        [Range(0.0f, 1.5f)]
        [SerializeField] private float disappearHopHeight = 0.35f;

        [Tooltip("Enable procedural confetti puff / shockwave burst visual effects on disappear")]
        [SerializeField] private bool enableDisappearVFX = true;

        private readonly List<GameObject> allRoofPassengers = new List<GameObject>();
        private List<Vector3> seatLocalOffsets = new List<Vector3>();
        private List<GameObject> spawnedPassengers = new List<GameObject>();
        private int boardedPassengerCount = 0;
        private MaterialPropertyBlock propBlock;
        private CrowdColorPalette colorPalette;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId_Prop = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock headlightPropBlock;
        private int headlightSlot = -1;
        private Color baseHeadlightEmission = new Color(23.28f, 17.63f, 0f, 1f);
        private Color baseHeadlightAlbedo = new Color(0.9f, 0.8f, 0.2f, 1f);
        private float busNoiseSeed;
        private float busTimeOffset;
        private float busSpeedMultiplier = 1.0f;
        private float currentIntensityMultiplier = 0.0f;
        private float targetIntensityMultiplier = 0.0f;
        private float intensitySmoothVelocity = 0f;
        private bool isDrivingAway = false;
        private bool headlightsInitialized = false;
        private bool isHeadlightFullyOff = false;
        private Coroutine activeBlinkRoutine;
        private float blinkIntensityBonus = 0f;

        private MaterialPropertyBlock taillightPropBlock;
        private int taillightSlot = -1;
        private Color baseTaillightEmission = new Color(24f, 0.05f, 0f, 1f);
        private Color baseTaillightAlbedo = new Color(0.9f, 0.9f, 0.9f, 1f);

        private Coroutine activePopRoutine;
        private Coroutine activeShakeRoutine;
        private Coroutine activeSpawnRoutine;
        private Vector3 originalLocalScale = Vector3.one;
        private bool hasSavedScale = false;
        private Vector3 visualShakeOffset = Vector3.zero;
        private float lastShakeTime = -1f;

        public Vector3 VisualShakeOffset => visualShakeOffset;
        public override Color VehicleColor
        {
            get
            {
                if (colorPalette == null)
                {
                    var gm = SandBlocks.Gameplay.GameManager.Instance;
                    if (gm != null && gm.Palette != null) colorPalette = gm.Palette;
                }
                return colorPalette != null ? colorPalette.GetColor(colorId) : GetColorFallback(colorId);
            }
        }
        public System.Action<BusVehicle> OnBusFullOverride;
                public GameObject FrontPiecePrefab { get => frontPiecePrefab; set => frontPiecePrefab = value; }
        public GameObject MidPiecePrefab { get => midPiecePrefab; set => midPiecePrefab = value; }
        public GameObject RearPiecePrefab { get => rearPiecePrefab; set => rearPiecePrefab = value; }

        public int GetFrontPieceCapacity()
        {
            if (frontPiecePrefab != null)
            {
                var piece = frontPiecePrefab.GetComponent<ModularBusPiece>();
                if (piece != null && piece.PieceCapacity > 0) return piece.PieceCapacity;
                var list = piece != null ? piece.GetPassengers() : null;
                if (list != null && list.Count > 0) return list.Count;
            }
            return DefaultFrontPieceCapacity;
        }

        public int GetMidPieceCapacity()
        {
            if (midPiecePrefab != null)
            {
                var piece = midPiecePrefab.GetComponent<ModularBusPiece>();
                if (piece != null && piece.PieceCapacity > 0) return piece.PieceCapacity;
                var list = piece != null ? piece.GetPassengers() : null;
                if (list != null && list.Count > 0) return list.Count;
            }
            return DefaultMidPieceCapacity;
        }

        public int GetRearPieceCapacity()
        {
            if (rearPiecePrefab != null)
            {
                var piece = rearPiecePrefab.GetComponent<ModularBusPiece>();
                if (piece != null && piece.PieceCapacity > 0) return piece.PieceCapacity;
                var list = piece != null ? piece.GetPassengers() : null;
                if (list != null && list.Count > 0) return list.Count;
            }
            return DefaultRearPieceCapacity;
        }

        public int CalculateCapacityForLength(int busLength)
        {
            return CalculateModularCapacity(busLength, GetFrontPieceCapacity(), GetMidPieceCapacity(), GetRearPieceCapacity());
        }
        public Transform PiecesParent { get => piecesParent; set => piecesParent = value; }
        public float PieceLength { get => pieceLength; set => pieceLength = value; }
        public List<GameObject> SpawnedPieces => spawnedPieces;
        public Renderer TaillightRenderer { get => taillightRenderer; set => taillightRenderer = value; }
        public List<Renderer> BodyRenderers { get => bodyRenderers; set => bodyRenderers = value; }
        public Renderer BodyRenderer { get => bodyRenderer; set => bodyRenderer = value; }
        public Renderer HeadlightRenderer { get => headlightRenderer; set => headlightRenderer = value; }
        public int BodyMaterialIndex { get => bodyMaterialIndex; set { bodyMaterialIndex = value; ApplyColor(colorPalette); } }
        public int HeadlightMaterialIndex { get => headlightMaterialIndex; set => headlightMaterialIndex = value; }
        public int TaillightMaterialIndex { get => taillightMaterialIndex; set => taillightMaterialIndex = value; }
        public Material CustomBodyMaterial { get => customBodyMaterial; set { customBodyMaterial = value; ApplyColor(colorPalette); } }

        public Transform PassengerParent { get => passengerParent != null ? passengerParent : charactersParent; set => passengerParent = value; }
        public Transform CharactersParent => PassengerParent;
        public List<GameObject> PassengerList { get => passengerList; set => passengerList = value; }
        public List<GameObject> SeatPassengers => seatPassengers;
        public int BoardedPassengerCount => boardedPassengerCount;
        public List<Transform> SeatTransforms => seatTransforms;
        public int SeatCount => seatTransforms != null ? seatTransforms.Count : 0;
        public GameObject PassengerPrefab => passengerPrefab;
        public bool EnablePassengerBoardPop
        {
            get => enablePassengerBoardPop;
            set => enablePassengerBoardPop = value;
        }

        public float PassengerPopDuration
        {
            get => passengerPopDuration;
            set => passengerPopDuration = Mathf.Max(0.05f, value);
        }

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
            ApplyColor(colorPalette);
            InitializeHeadlights();
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

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            Vector3 forwardDir;
            Vector3 upDir = isHorizontal ? Vector3.up : Vector3.back;

            if (isHorizontal)
            {
                switch (rotationDegrees)
                {
                    case 90:  forwardDir = Vector3.forward; break;
                    case 180: forwardDir = Vector3.left;    break;
                    case 270: forwardDir = Vector3.back;    break;
                    case 0:
                    default:  forwardDir = Vector3.right;   break;
                }
            }
            else
            {
                switch (rotationDegrees)
                {
                    case 90:  forwardDir = Vector3.up;    break;
                    case 180: forwardDir = Vector3.left;  break;
                    case 270: forwardDir = Vector3.down;  break;
                    case 0:
                    default:  forwardDir = Vector3.right; break;
                }
            }

            Quaternion lookRot = Quaternion.LookRotation(forwardDir, upDir);
            transform.rotation = lookRot * Quaternion.Euler(visualRotationOffset);
        }

        private void OnValidate()
        {
            rotationDegrees = Mathf.RoundToInt(rotationDegrees / 90f) * 90 % 360;
            if (rotationDegrees < 0) rotationDegrees += 360;
            length = Mathf.Max(2, length);
            EnsureCentered();
            ApplyRotation();
            // Do NOT auto add/instantiate modular pieces in OnValidate to avoid duplicate pieces on script recompile
            EnsureCollider();
            AutoDetectHierarchyAndSeats();
            UpdatePassengerCountUI();
            ApplyColor(colorPalette);
            if (enableHeadlightAnimation)
            {
                InitializeHeadlights();
            }
        }

        [ContextMenu("Setup Passenger Slots")]
        public void SetupPassengerSlots()
        {
            allRoofPassengers.Clear();

            // 1. If modular pieces are spawned, gather seated passenger models from each piece
            if (spawnedPieces != null && spawnedPieces.Count > 0)
            {
                for (int i = 0; i < spawnedPieces.Count; i++)
                {
                    if (spawnedPieces[i] == null) continue;
                    var pieceComp = spawnedPieces[i].GetComponent<ModularBusPiece>();
                    if (pieceComp != null)
                    {
                        var pList = pieceComp.GetPassengers();
                        if (pList != null && pList.Count > 0)
                        {
                            allRoofPassengers.AddRange(pList);
                        }
                    }
                    else
                    {
                        // Fallback: search for child named Passengers or Seats
                        Transform c = spawnedPieces[i].transform.Find("Passengers");
                        if (c == null) c = spawnedPieces[i].transform.Find("Seats");
                        if (c != null)
                        {
                            for (int ch = 0; ch < c.childCount; ch++)
                            {
                                var child = c.GetChild(ch);
                                if (child != null) allRoofPassengers.Add(child.gameObject);
                            }
                        }
                    }
                }
            }

            // 2. Direct inspector or legacy fallback
            if (allRoofPassengers.Count == 0)
            {
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
                            if (child != null) allRoofPassengers.Add(child.gameObject);
                        }
                    }
                }
            }

            if (allRoofPassengers.Count == 0)
            {
                seatPassengers = new List<GameObject>();
                return;
            }

            for (int i = 0; i < allRoofPassengers.Count; i++)
            {
                allRoofPassengers[i].SetActive(false);
                var anim = allRoofPassengers[i].GetComponentInChildren<Animator>(true);
                if (anim != null) anim.enabled = false;
            }

            if (seatTransforms != null && seatTransforms.Count > 0) seatTransforms.Clear();

            bool isHorizontal = (GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            bool fillFromPlayer = (seatFillOrder == BusSeatFillOrder.FillFromPlayerToFirstRow);

            int CompareSeats(GameObject a, GameObject b)
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;

                if (isHorizontal)
                {
                    // In Horizontal_XZ: player is at lower Z (-Z), first row of crowd is at higher Z (+Z)
                    float dz = fillFromPlayer
                        ? (a.transform.position.z - b.transform.position.z)
                        : (b.transform.position.z - a.transform.position.z);

                    if (Mathf.Abs(dz) > 0.05f) return dz.CompareTo(0f);
                    return a.transform.position.x.CompareTo(b.transform.position.x);
                }
                else
                {
                    // In Vertical_XY: player is at lower Y (-Y), first row of crowd is at higher Y (+Y)
                    float dy = fillFromPlayer
                        ? (a.transform.position.y - b.transform.position.y)
                        : (b.transform.position.y - a.transform.position.y);

                    if (Mathf.Abs(dy) > 0.05f) return dy.CompareTo(0f);
                    return a.transform.position.x.CompareTo(b.transform.position.x);
                }
            }

            int targetCount = Mathf.Clamp(capacity, 1, allRoofPassengers.Count);
            if (targetCount >= allRoofPassengers.Count)
            {
                var allSorted = new List<GameObject>(allRoofPassengers);
                allSorted.Sort(CompareSeats);
                seatPassengers = allSorted;
                return;
            }

            Vector3 center = Vector3.zero;
            for (int i = 0; i < allRoofPassengers.Count; i++)
            {
                center += allRoofPassengers[i].transform.position;
            }
            center /= allRoofPassengers.Count;

            var sortedByCenter = new List<GameObject>(allRoofPassengers);
            sortedByCenter.Sort((a, b) =>
            {
                float da = Vector3.SqrMagnitude(a.transform.position - center);
                float db = Vector3.SqrMagnitude(b.transform.position - center);
                return da.CompareTo(db);
            });

            var chosen = new List<GameObject>();
            for (int i = 0; i < targetCount; i++) chosen.Add(sortedByCenter[i]);

            chosen.Sort(CompareSeats);

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
            ApplyColor(colorPalette);
        }

                [ContextMenu("Clear Modular Pieces")]
        public void ClearModularPieces()
        {
            Transform container = piecesParent != null ? piecesParent : transform;
            if (container != null && container != transform)
            {
                for (int i = container.childCount - 1; i >= 0; i--)
                {
                    var child = container.GetChild(i);
                    if (child != null)
                    {
                        SafeDestroy(child.gameObject);
                    }
                }
            }
            if (spawnedPieces != null)
            {
                for (int i = spawnedPieces.Count - 1; i >= 0; i--)
                {
                    if (spawnedPieces[i] != null)
                    {
                        SafeDestroy(spawnedPieces[i]);
                    }
                }
                spawnedPieces.Clear();
            }
        }

        [ContextMenu("Rebuild Modular Mesh")]
        public void RebuildModularMeshMenu()
        {
            RebuildModularMesh(length);
        }

        public void RebuildModularMesh(int targetLength)
        {
#if UNITY_EDITOR
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(gameObject))
            {
                return;
            }
#endif
            int effLength = Mathf.Max(2, targetLength);
            length = effLength;

            // Pure reference-based: requires front and rear piece prefabs from Inspector
            if (frontPiecePrefab == null || rearPiecePrefab == null)
            {
                EnsureCollider();
                return;
            }

            Transform container = piecesParent != null ? piecesParent : transform;

            // Thoroughly clear previous pieces
            ClearModularPieces();

            if (spawnedPieces == null) spawnedPieces = new List<GameObject>();

            float halfSpan = (effLength - 1) * 0.5f;

            // 1. Rear piece at index 0 (z = -halfSpan * pieceLength)
            float rearZ = -halfSpan * pieceLength;
            GameObject rearGo = InstantiatePiece(rearPiecePrefab, container, new Vector3(0f, 0f, rearZ));
            if (rearGo != null) spawnedPieces.Add(rearGo);

            // 2. Mid pieces for index 1 to effLength - 2
            int midCount = effLength - 2;
            if (midPiecePrefab != null && midCount > 0)
            {
                for (int i = 1; i <= midCount; i++)
                {
                    float midZ = (i - halfSpan) * pieceLength;
                    GameObject midGo = InstantiatePiece(midPiecePrefab, container, new Vector3(0f, 0f, midZ));
                    if (midGo != null) spawnedPieces.Add(midGo);
                }
            }

            // 3. Front piece at index effLength - 1 (z = +halfSpan * pieceLength)
            float frontZ = halfSpan * pieceLength;
            GameObject frontGo = InstantiatePiece(frontPiecePrefab, container, new Vector3(0f, 0f, frontZ));
            if (frontGo != null) spawnedPieces.Add(frontGo);

            // 4. Update BoxCollider to encapsulate full modular length
            EnsureCollider();

            // 5. Update renderers directly from the instantiated piece references
            if (bodyRenderers == null) bodyRenderers = new List<Renderer>();
            bodyRenderers.Clear();
            for (int i = 0; i < spawnedPieces.Count; i++)
            {
                if (spawnedPieces[i] == null) continue;
                var rends = spawnedPieces[i].GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < rends.Length; r++)
                {
                    if (rends[r] != null) bodyRenderers.Add(rends[r]);
                }
            }

            if (frontGo != null)
            {
                headlightRenderer = frontGo.GetComponentInChildren<Renderer>(true);
            }
            if (rearGo != null)
            {
                taillightRenderer = rearGo.GetComponentInChildren<Renderer>(true);
            }

            // 6. Apply colors and lights
            InitializeHeadlights();
            ApplyColor(colorPalette);

            // 7. Refresh passengers / UI
            SetupPassengerSlots();
            UpdatePassengerCountUI();
        }

        private GameObject InstantiatePiece(GameObject prefab, Transform parent, Vector3 localPos)
        {
            if (prefab == null) return null;
#if UNITY_EDITOR
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(parent))
            {
                return null;
            }
#endif
            GameObject go = Instantiate(prefab, parent);
            if (go != null)
            {
                go.transform.localPosition = localPos;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }
            return go;
        }

        public override void Initialize(int id, int cap, int len, bool vertical, Vector2Int startPos)
        {
            Initialize(id, cap, len, vertical ? 90 : 0, startPos);
        }

        public void Initialize(int id, int cap, int len, int rotDegrees, Vector2Int startPos)
        {
            colorId = id;
            capacity = Mathf.Clamp(cap, 1, MaxBusCapacity);
            length = Mathf.Max(2, len);
            rotationDegrees = Mathf.RoundToInt(rotDegrees / 90f) * 90 % 360;
            if (rotationDegrees < 0) rotationDegrees += 360;
            isVertical = (rotationDegrees == 90 || rotationDegrees == 270);
            gridPosition = startPos;

            ClearPassengers();
            CleanupOldChildColliders();
            EnsureCentered();
            ApplyRotation();
            if (frontPiecePrefab != null || rearPiecePrefab != null || piecesParent != null)
            {
                RebuildModularMesh(length);
            }
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
            var box = GetComponent<BoxCollider>();
            if (box == null)
            {
                box = gameObject.AddComponent<BoxCollider>();
            }
            int effLength = Mathf.Max(2, length);
            box.size = new Vector3(1.35f, 1.2f, effLength * pieceLength);
            box.center = new Vector3(0f, 0.75f, 0f);
        }

        public void ApplyColor(CrowdColorPalette palette)
        {
            if (palette != null) colorPalette = palette;
            if (colorPalette == null)
            {
                var gm = GameManager.Instance ?? FindAnyObjectByType<Gameplay.GameManager>();
                if (gm != null && gm.Palette != null) colorPalette = gm.Palette;
            }

            Color targetColor = colorPalette != null ? colorPalette.GetColor(colorId) : GetColorFallback(colorId);
            if (propBlock == null) propBlock = new MaterialPropertyBlock();
            propBlock.Clear();
            propBlock.SetColor(BaseColorId, targetColor);
            propBlock.SetColor(ColorId_Prop, targetColor);
            propBlock.SetColor(Shader.PropertyToID("_1st_ShadeColor"), targetColor * 0.85f);
            propBlock.SetColor(Shader.PropertyToID("_2nd_ShadeColor"), targetColor * 0.70f);

            var renderersToColor = (bodyRenderers != null && bodyRenderers.Count > 0) 
                ? bodyRenderers 
                : (bodyRenderer != null ? new List<Renderer> { bodyRenderer } : null);

            if (renderersToColor != null)
            {
                for (int i = 0; i < renderersToColor.Count; i++)
                {
                    var r = renderersToColor[i];
                    if (r == null) continue;

                    var mats = r.sharedMaterials;
                    int slot = (bodyMaterialIndex >= 0 && mats != null && bodyMaterialIndex < mats.Length) 
                        ? bodyMaterialIndex 
                        : 0;

                    if (customBodyMaterial != null && mats != null && slot < mats.Length && mats[slot] != customBodyMaterial)
                    {
                        var newMats = (Material[])mats.Clone();
                        newMats[slot] = customBodyMaterial;
                        r.sharedMaterials = newMats;
                    }

                    r.SetPropertyBlock(propBlock, slot);
                }
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

        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj, true);
        }

        public void InitScaleIfNeeded()
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

            SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusPlace();
            SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerUIButton();

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
            if (!enablePassengerBoardPop) return;
            if (GameManager.Instance != null && !GameManager.Instance.EnableBusPassengerPopAnimation) return;

            InitScaleIfNeeded();
            if (activePopRoutine != null) StopCoroutine(activePopRoutine);
            activePopRoutine = StartCoroutine(PassengerPopRoutine());
        }

        private IEnumerator PassengerPopRoutine()
        {
            float duration = Mathf.Max(0.05f, passengerPopDuration);
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

        public void PlayBlockedShake(Vector3 dragDirection, bool showToast = true)
        {
            if (Time.time - lastShakeTime < 0.2f) return;
            lastShakeTime = Time.time;

            SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusBlocked();
            SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerVehicleBlocked();
            if (showToast)
            {
                SandBlocks.Gameplay.ToastManager.Show("Path is blocked!");
            }

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
                bool isHoriz = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
                if (isHoriz)
                {
                    shakeAxis = isVertical ? Vector3.forward : Vector3.right;
                }
                else
                {
                    shakeAxis = isVertical ? Vector3.up : Vector3.right;
                }
            }

            var dragHandler = FindAnyObjectByType<SandBlocks.Puzzle.Input.VehicleDragHandler>();
            var grid = FindAnyObjectByType<SandBlocks.Puzzle.Grid.PuzzleGrid>();

            // If the vehicle is currently snapping back to slot, wait for the short snap (0.08s) to complete first
            if (dragHandler != null && dragHandler.HoveredVehicle != this && grid != null)
            {
                Vector3 targetSlotPos = grid.GridToWorld(GridPosition, Length, IsVertical);
                if ((transform.position - targetSlotPos).sqrMagnitude > 0.01f)
                {
                    yield return new WaitForSeconds(0.08f);
                }
            }

            Vector3 basePosition = (grid != null && (dragHandler == null || dragHandler.HoveredVehicle != this))
                ? grid.GridToWorld(GridPosition, Length, IsVertical)
                : transform.position;

            float duration = 0.22f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / duration;
                float decay = 1f - progress;
                float offset = Mathf.Sin(progress * Mathf.PI * 6f) * blockedShakeMagnitude * decay;

                visualShakeOffset = shakeAxis * offset;

                if (dragHandler == null || dragHandler.HoveredVehicle != this)
                {
                    transform.position = basePosition + visualShakeOffset;
                }

                yield return null;
            }

            visualShakeOffset = Vector3.zero;
            if (dragHandler == null || dragHandler.HoveredVehicle != this)
            {
                transform.position = basePosition;
            }
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

            if (blinkOnPassengerFill)
            {
                TriggerHeadlightBlink();
            }

            SandBlocks.Core.Audio.AudioManager.Instance?.PlayPassengerBoard();
            SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerPassengerBoard();

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
            SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerBusDepart();

            SetSelected(false);

            var dragHandler = FindAnyObjectByType<SandBlocks.Puzzle.Input.VehicleDragHandler>();
            if (dragHandler != null)
            {
                dragHandler.OnVehicleFullOrDeparting(this);
            }

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
            // Classic mode & Unified BusDriveAway mode departure:
            var grid = FindAnyObjectByType<SandBlocks.Puzzle.Grid.PuzzleGrid>();
            if (grid != null)
            {
                grid.RemoveVehicle(this);
                grid.ClearHoverTileHighlights();
            }

            if (departureMode == BusDepartureMode.Disappear)
            {
                StartCoroutine(DisappearRoutine());
            }
            else
            {
                StartCoroutine(DriveAwayRoutine());
            }
        }

        private void OnDestroy()
        {
            var grid = FindAnyObjectByType<SandBlocks.Puzzle.Grid.PuzzleGrid>();
            if (grid != null)
            {
                grid.RemoveVehicle(this);
                grid.ClearHoverTileHighlights();
            }

            var dragHandler = FindAnyObjectByType<SandBlocks.Puzzle.Input.VehicleDragHandler>();
            if (dragHandler != null)
            {
                dragHandler.OnVehicleFullOrDeparting(this);
            }
        }

        public ParticleSystem FullVFX { get => fullVFX; set => fullVFX = value; }
        public BusDepartureMode DepartureMode { get => departureMode; set => departureMode = value; }
        public float DepartureDelay { get => driveAwayDelay; set => driveAwayDelay = value; }
        public float DriveAwayDelay { get => driveAwayDelay; set => driveAwayDelay = value; }
        public float DriveAwayDuration { get => driveAwayDuration; set => driveAwayDuration = value; }
        public float DriveAwayDistance { get => driveAwayDistance; set => driveAwayDistance = value; }
        public float DisappearDuration { get => disappearDuration; set => disappearDuration = value; }
        public float DisappearHopHeight { get => disappearHopHeight; set => disappearHopHeight = value; }
        public bool EnableDisappearVFX { get => enableDisappearVFX; set => enableDisappearVFX = value; }
        public bool IsDrivingAway => isDrivingAway;
        public bool IsDeparting => isDrivingAway;

        public void PlayFullVFX()
        {
            if (fullVFX == null) return;

            ParticleSystem vfxInstance;
            if (fullVFX.gameObject.scene.IsValid())
            {
                vfxInstance = fullVFX;
                // Detach from bus so it is not scaled to zero or destroyed prematurely when the bus vanishes
                vfxInstance.transform.SetParent(null, true);
            }
            else
            {
                vfxInstance = Instantiate(fullVFX, transform.position, transform.rotation);
            }

            vfxInstance.gameObject.SetActive(true);
            vfxInstance.Play(true);
            Destroy(vfxInstance.gameObject, 3.0f);
        }

        private IEnumerator DisappearRoutine()
        {
            isDrivingAway = true;
            SetSelected(false);

            if (activePopRoutine != null)
            {
                StopCoroutine(activePopRoutine);
                activePopRoutine = null;
            }
            if (activeShakeRoutine != null)
            {
                StopCoroutine(activeShakeRoutine);
                activeShakeRoutine = null;
            }

            var dragHandler = FindAnyObjectByType<SandBlocks.Puzzle.Input.VehicleDragHandler>();
            if (dragHandler != null)
            {
                dragHandler.OnVehicleFullOrDeparting(this);
            }

            if (driveAwayDelay > 0f)
            {
                yield return new WaitForSeconds(driveAwayDelay);
            }

            SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusDriveAway();
            SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerBusDepart();
            PlayFullVFX();

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            Vector3 upDir = isHorizontal ? Vector3.up : Vector3.back;

            Vector3 startPos = transform.position;
            InitScaleIfNeeded();
            Vector3 baseScale = originalLocalScale;
            Quaternion baseRot = transform.localRotation;

            float duration = Mathf.Max(0.15f, disappearDuration);
            float elapsed = 0f;

            // Phase 1 (0 -> 0.28): Anticipation Squash
            // Phase 2 (0.28 -> 0.58): Pop hop upward and stretch
            // Phase 3 (0.58 -> 1.0): Spin wobble & elastic cubic shrink to zero
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);

                if (progress < 0.28f)
                {
                    float tSquash = progress / 0.28f;
                    float squash = Mathf.Sin(tSquash * Mathf.PI);

                    if (isHorizontal)
                    {
                        transform.localScale = new Vector3(
                            baseScale.x * (1f + 0.14f * squash),
                            baseScale.y * (1f - 0.25f * squash),
                            baseScale.z * (1f + 0.14f * squash)
                        );
                    }
                    else
                    {
                        transform.localScale = new Vector3(
                            baseScale.x * (1f + 0.14f * squash),
                            baseScale.y * (1f + 0.14f * squash),
                            baseScale.z * (1f - 0.25f * squash)
                        );
                    }
                    transform.position = startPos;
                }
                else if (progress < 0.58f)
                {
                    float tHop = (progress - 0.28f) / 0.30f;
                    float hopCurve = Mathf.Sin(tHop * Mathf.PI);

                    if (isHorizontal)
                    {
                        transform.localScale = new Vector3(
                            baseScale.x * (1f - 0.15f * hopCurve),
                            baseScale.y * (1f + 0.28f * hopCurve),
                            baseScale.z * (1f - 0.15f * hopCurve)
                        );
                    }
                    else
                    {
                        transform.localScale = new Vector3(
                            baseScale.x * (1f - 0.15f * hopCurve),
                            baseScale.y * (1f - 0.15f * hopCurve),
                            baseScale.z * (1f + 0.28f * hopCurve)
                        );
                    }

                    transform.position = startPos + upDir * (disappearHopHeight * hopCurve);

                    float tilt = Mathf.Sin(tHop * Mathf.PI) * 7f;
                    transform.localRotation = baseRot * Quaternion.Euler(0f, tilt, 0f);
                }
                else
                {
                    float tShrink = (progress - 0.58f) / 0.42f;
                    float scaleFactor = 1f - tShrink;
                    float popFactor = Mathf.Max(0f, scaleFactor * scaleFactor * scaleFactor);

                    transform.localScale = baseScale * popFactor;
                    transform.position = startPos + upDir * (disappearHopHeight * (1f - tShrink));

                    float twist = 7f + tShrink * 30f;
                    transform.localRotation = baseRot * Quaternion.Euler(0f, twist, 0f);
                }

                yield return null;
            }

            transform.localScale = Vector3.zero;
            Destroy(gameObject);
        }

        private IEnumerator DriveAwayRoutine()
        {
            isDrivingAway = true;
            SetSelected(false);

            var dragHandler = FindAnyObjectByType<SandBlocks.Puzzle.Input.VehicleDragHandler>();
            if (dragHandler != null)
            {
                dragHandler.OnVehicleFullOrDeparting(this);
            }
            if (driveAwayDelay > 0f)
            {
                yield return new WaitForSeconds(driveAwayDelay);
            }

            SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusDriveAway();
            SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerBusDepart();

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            Vector3 driveDir;
            if (isHorizontal)
            {
                switch (rotationDegrees)
                {
                    case 90: driveDir = Vector3.forward; break;
                    case 180: driveDir = Vector3.left; break;
                    case 270: driveDir = Vector3.back; break;
                    default: driveDir = Vector3.right; break;
                }
            }
            else
            {
                switch (rotationDegrees)
                {
                    case 90: driveDir = Vector3.up; break;
                    case 180: driveDir = Vector3.left; break;
                    case 270: driveDir = Vector3.down; break;
                    default: driveDir = Vector3.right; break;
                }
            }

            var grid = FindAnyObjectByType<SandBlocks.Puzzle.Grid.PuzzleGrid>();
            Vector3 worldDriveDir = (grid != null) ? grid.transform.TransformDirection(driveDir) : driveDir;

            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, driveAwayDuration);
            Vector3 startPos = transform.position;
            Vector3 endPos = startPos + worldDriveDir * driveAwayDistance;

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

        public override void SetSelected(bool selected)
        {
            base.SetSelected(selected);
            if (enableHeadlightAnimation && highlightWhenSelected)
            {
                targetIntensityMultiplier = selected ? selectedGlowIntensity : 0f;
            }
        }

        private void Update()
        {
            if (enableHeadlightAnimation)
            {
                UpdateHeadlightAnimation();
            }
        }

        public void InitializeHeadlights()
        {
            if (!enableHeadlightAnimation) return;

            if (headlightRenderer == null && bodyRenderer != null)
            {
                headlightRenderer = bodyRenderer;
            }
            if (taillightRenderer == null && headlightRenderer != null)
            {
                taillightRenderer = headlightRenderer;
            }

            if (headlightRenderer != null)
            {
                var mats = headlightRenderer.sharedMaterials;
                headlightSlot = (headlightMaterialIndex >= 0 && mats != null && headlightMaterialIndex < mats.Length) 
                    ? headlightMaterialIndex 
                    : -1;

                if (headlightSlot >= 0 && mats[headlightSlot] != null)
                {
                    var mat = mats[headlightSlot];
                    if (!mat.IsKeywordEnabled("_EMISSION")) mat.EnableKeyword("_EMISSION");

                    if (customHeadlightEmissionColor.a > 0.001f)
                    {
                        baseHeadlightEmission = customHeadlightEmissionColor;
                    }
                    else if (mat.HasProperty(EmissionColorId))
                    {
                        Color c = mat.GetColor(EmissionColorId);
                        baseHeadlightEmission = (c.maxColorComponent > 0.05f) ? c : new Color(23.28f, 17.63f, 0f, 1f);
                    }

                    if (mat.HasProperty(BaseColorId)) baseHeadlightAlbedo = mat.GetColor(BaseColorId);
                    else if (mat.HasProperty(ColorId_Prop)) baseHeadlightAlbedo = mat.GetColor(ColorId_Prop);
                }
            }

            if (enableTaillightAnimation && taillightRenderer != null)
            {
                var mats = taillightRenderer.sharedMaterials;
                taillightSlot = (taillightMaterialIndex >= 0 && mats != null && taillightMaterialIndex < mats.Length) 
                    ? taillightMaterialIndex 
                    : -1;

                if (taillightSlot >= 0 && mats[taillightSlot] != null)
                {
                    var mat = mats[taillightSlot];
                    if (!mat.IsKeywordEnabled("_EMISSION")) mat.EnableKeyword("_EMISSION");
                    if (mat.HasProperty(EmissionColorId))
                    {
                        Color c = mat.GetColor(EmissionColorId);
                        if (c.maxColorComponent > 0.05f) baseTaillightEmission = c;
                    }
                    if (mat.HasProperty(BaseColorId)) baseTaillightAlbedo = mat.GetColor(BaseColorId);
                }
            }

            if (headlightPropBlock == null) headlightPropBlock = new MaterialPropertyBlock();
            if (taillightPropBlock == null) taillightPropBlock = new MaterialPropertyBlock();

            busNoiseSeed = UnityEngine.Random.Range(10f, 1000f);
            busTimeOffset = UnityEngine.Random.Range(0f, 500f);
            busSpeedMultiplier = UnityEngine.Random.Range(0.85f, 1.25f);
            currentIntensityMultiplier = 0.0f;
            targetIntensityMultiplier = 0.0f;
            blinkIntensityBonus = 0f;
            isHeadlightFullyOff = false;
            headlightsInitialized = true;

            ApplyHeadlightIntensity(0f);
        }

        /// <summary>
        /// Triggers an energetic automotive blink on the bus headlights (called when passengers board).
        /// </summary>
        public void TriggerHeadlightBlink(float? customDuration = null, float? customIntensity = null)
        {
            if (!enableHeadlightAnimation) return;

            float duration = customDuration ?? passengerFillBlinkDuration;
            float intensity = customIntensity ?? passengerFillBlinkIntensity;

            if (activeBlinkRoutine != null)
            {
                StopCoroutine(activeBlinkRoutine);
            }
            activeBlinkRoutine = StartCoroutine(HeadlightBlinkRoutine(duration, intensity));
        }

        private IEnumerator HeadlightBlinkRoutine(float duration, float peakIntensity)
        {
            float elapsed = 0f;
            duration = Mathf.Max(0.04f, duration);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // Quick attack (first 20% of duration), smooth power decay (remaining 80%)
                float curve = (t < 0.20f)
                    ? (t / 0.20f)
                    : Mathf.Pow(1f - ((t - 0.20f) / 0.80f), 1.6f);

                blinkIntensityBonus = peakIntensity * curve;
                yield return null;
            }

            blinkIntensityBonus = 0f;
            activeBlinkRoutine = null;
        }

        private void ApplyHeadlightIntensity(float multiplier)
        {
            if (headlightRenderer == null || headlightSlot < 0) return;

            multiplier = Mathf.Max(0f, multiplier);

            Color currentEmission = (multiplier > 0.001f)
                ? (baseHeadlightEmission * multiplier)
                : Color.black;

            float normalizedT = Mathf.InverseLerp(0f, maxIntensityMultiplier, multiplier);
            Color currentAlbedo = Color.Lerp(baseHeadlightAlbedo * 0.70f, baseHeadlightAlbedo * 1.25f, normalizedT);

            if (headlightPropBlock == null) headlightPropBlock = new MaterialPropertyBlock();
            headlightPropBlock.SetColor(EmissionColorId, currentEmission);
            headlightPropBlock.SetColor(BaseColorId, currentAlbedo);
            headlightPropBlock.SetColor(ColorId_Prop, currentAlbedo);

            headlightRenderer.SetPropertyBlock(headlightPropBlock, headlightSlot);
        }

        private void UpdateHeadlightAnimation()
        {
            if (!headlightsInitialized || headlightRenderer == null || headlightSlot < 0)
            {
                InitializeHeadlights();
                if (headlightRenderer == null || headlightSlot < 0) return;
            }

            // 1. Determine baseline target intensity based on selection and departure
            float targetIntensity = 0f;
            if (isDrivingAway && highlightWhenMoving)
            {
                targetIntensity = 1.75f;
            }
            else if (isSelected && highlightWhenSelected)
            {
                targetIntensity = selectedGlowIntensity;
            }

            // 2. Smooth transition toward target
            float smoothTime = (targetIntensity > currentIntensityMultiplier) ? 0.05f : 0.10f;
            currentIntensityMultiplier = Mathf.SmoothDamp(
                currentIntensityMultiplier,
                targetIntensity,
                ref intensitySmoothVelocity,
                smoothTime
            );

            if (targetIntensity <= 0.001f && currentIntensityMultiplier < 0.01f)
            {
                currentIntensityMultiplier = 0f;
            }

            // 3. Organic alternator idle shimmer (only active when lights are glowing)
            float organicFlutter = 0f;
            if (currentIntensityMultiplier > 0.05f)
            {
                float busTime = (Time.time + busTimeOffset) * baseFlickerSpeed * busSpeedMultiplier;
                float slowDrift = (Mathf.PerlinNoise(busTime * 0.75f, busNoiseSeed) - 0.5f) * 0.20f;
                float fastVibe = (Mathf.PerlinNoise(busTime * 3.8f, busNoiseSeed + 33.1f) - 0.5f) * 0.08f;
                organicFlutter = (slowDrift + fastVibe) * Mathf.Clamp01(currentIntensityMultiplier);
            }

            // 4. Combine base smoothed intensity, alternator flutter, and passenger fill blink bonus
            float finalMultiplier = currentIntensityMultiplier + organicFlutter + blinkIntensityBonus;

            // 5. Impact wall shake modulation (flicker on collision if lights are glowing)
            if (visualShakeOffset.sqrMagnitude > 0.0001f && finalMultiplier > 0.1f)
            {
                finalMultiplier *= (Mathf.Sin(Time.time * 30f) > 0f ? 1.4f : 0.6f);
            }

            finalMultiplier = Mathf.Max(0f, finalMultiplier);

            // 6. Apply to renderer (with zero-emission early-out when fully off)
            if (finalMultiplier <= 0.001f)
            {
                if (!isHeadlightFullyOff)
                {
                    ApplyHeadlightIntensity(0f);
                    isHeadlightFullyOff = true;
                }
            }
            else
            {
                isHeadlightFullyOff = false;
                ApplyHeadlightIntensity(finalMultiplier);
            }

            // 7. Optional taillights
            if (enableTaillightAnimation && taillightSlot >= 0 && headlightRenderer != null)
            {
                UpdateTaillightAnimation();
            }
        }

        private void UpdateTaillightAnimation()
        {
            var rend = taillightRenderer != null ? taillightRenderer : headlightRenderer;
            if (rend == null || taillightSlot < 0) return;

            if (!isSelected && !isDrivingAway && blinkIntensityBonus <= 0.001f)
            {
                if (taillightPropBlock == null) taillightPropBlock = new MaterialPropertyBlock();
                taillightPropBlock.SetColor(EmissionColorId, Color.black);
                rend.SetPropertyBlock(taillightPropBlock, taillightSlot);
                return;
            }

            float t = (Time.time + busTimeOffset * 0.7f) * 1.2f;
            float pulse = Mathf.Sin(t) * 0.15f + 0.85f;
            Color currentEmission = baseTaillightEmission * (isSelected ? 1.4f : 0.8f) * pulse;
            Color currentAlbedo = baseTaillightAlbedo;

            if (taillightPropBlock == null) taillightPropBlock = new MaterialPropertyBlock();
            taillightPropBlock.SetColor(EmissionColorId, currentEmission);
            taillightPropBlock.SetColor(BaseColorId, currentAlbedo);
            taillightPropBlock.SetColor(ColorId_Prop, currentAlbedo);

            rend.SetPropertyBlock(taillightPropBlock, taillightSlot);
        }

        [ContextMenu("Trigger Headlight Blink (Test)")]
        public void TriggerHeadlightBlinkTest()
        {
            TriggerHeadlightBlink();
        }

        [ContextMenu("Toggle Headlight Selection Glow (Test)")]
        public void ToggleHeadlightSelectionTest()
        {
            SetSelected(!isSelected);
        }
    }
}
