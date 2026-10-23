using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SandBlocks.Core.Data;

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

        [Header("Visual Components")]
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Transform seatsParent;
        [SerializeField] private GameObject passengerPrefab;

        [Header("Passenger Settings")]
        [SerializeField] private float passengerScale = 0.1f;

        [Header("Visual Juice & Feedback")]
        [SerializeField] private float popScaleMultiplier = 1.14f;
        [SerializeField] private float blockedShakeMagnitude = 0.16f;

        private List<Vector3> seatLocalOffsets = new List<Vector3>();
        private List<GameObject> spawnedPassengers = new List<GameObject>();
        private MaterialPropertyBlock propBlock;
        private CrowdColorPalette colorPalette;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId_Prop = Shader.PropertyToID("_Color");

        private Coroutine activePopRoutine;
        private Coroutine activeShakeRoutine;
        private Vector3 originalLocalScale = Vector3.one;
        private bool hasSavedScale = false;
        private Vector3 visualShakeOffset = Vector3.zero;
        private float lastShakeTime = -1f;

        public Vector3 VisualShakeOffset => visualShakeOffset;

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
            passengerCount = 0;

            CleanupOldChildColliders();
            EnsureCentered();
            ApplyRotation();
            EnsureCollider();
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

        public override bool TryAddPassenger(out Vector3 targetSeatWorldPos)
        {
            return TryAddPassenger(out targetSeatWorldPos, out _);
        }

        private void InitScaleIfNeeded()
        {
            if (!hasSavedScale)
            {
                originalLocalScale = transform.localScale;
                hasSavedScale = true;
            }
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

        public bool TryAddPassenger(out Vector3 targetSeatWorldPos, out Vector3 localSeatOffset)
        {
            targetSeatWorldPos = Vector3.zero;
            localSeatOffset = Vector3.zero;
            if (IsFull) return false;

            int seatIndex = passengerCount;
            localSeatOffset = seatIndex < seatLocalOffsets.Count ? seatLocalOffsets[seatIndex] : Vector3.zero;
            targetSeatWorldPos = transform.TransformPoint(localSeatOffset);
            passengerCount++;

            PlayPassengerBoardPop();
            StartCoroutine(SpawnPassengerDelayed(localSeatOffset, 0.35f));

            if (IsFull)
            {
                OnFull();
            }

            return true;
        }

        private IEnumerator SpawnPassengerDelayed(Vector3 localSeat, float delay)
        {
            yield return new WaitForSeconds(delay);

            GameObject pass;
            if (passengerPrefab != null)
            {
                pass = Instantiate(passengerPrefab, transform);
                pass.transform.localPosition = localSeat;
                pass.transform.localRotation = Quaternion.identity;

                float parentScale = Mathf.Max(0.001f, transform.lossyScale.x);
                pass.transform.localScale = Vector3.one * (passengerScale / parentScale);

                var anim = pass.GetComponentInChildren<Animator>();
                if (anim != null)
                {
                    anim.SetBool("isRunning", false);
                    anim.SetTrigger("isSitting");
                }
            }
            else
            {
                pass = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pass.transform.SetParent(transform);
                pass.transform.localPosition = localSeat;
                pass.transform.localScale = Vector3.one * 0.35f;
                var col = pass.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }

            var rends = pass.GetComponentsInChildren<Renderer>();
            if (colorPalette != null)
            {
                Color c = colorPalette.GetColor(colorId);
                foreach (var r in rends)
                {
                    r.material.color = c;
                }
            }

            spawnedPassengers.Add(pass);
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
