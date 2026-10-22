using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SandBlocks.Core.Data;

namespace SandBlocks.Puzzle.Vehicles
{
    public class BusVehicle : VehicleBase
    {
        [Header("Visual Components")]
        [SerializeField] private MeshRenderer bodyRenderer;
        [SerializeField] private Transform seatsParent;
        [SerializeField] private GameObject passengerPrefab;

        private List<Vector3> seatLocalOffsets = new List<Vector3>();
        private List<GameObject> spawnedPassengers = new List<GameObject>();
        private MaterialPropertyBlock propBlock;
        private CrowdColorPalette colorPalette;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public override void Initialize(int id, int cap, int len, bool vertical, Vector2Int startPos)
        {
            colorId = id;
            capacity = cap;
            length = len;
            isVertical = vertical;
            gridPosition = startPos;
            passengerCount = 0;

            BuildSeatOffsets();
            SetupColliders();
        }

        public void ApplyColor(CrowdColorPalette palette)
        {
            colorPalette = palette;
            if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<MeshRenderer>();
            if (bodyRenderer != null && palette != null)
            {
                if (propBlock == null) propBlock = new MaterialPropertyBlock();
                propBlock.SetColor(BaseColorId, palette.GetColor(colorId));
                bodyRenderer.SetPropertyBlock(propBlock);
            }
        }

        private void BuildSeatOffsets()
        {
            seatLocalOffsets.Clear();
            float spacing = 0.5f;
            float start = -((capacity - 1) * 0.5f) * spacing;

            for (int i = 0; i < capacity; i++)
            {
                float offset = start + i * spacing;
                Vector3 localPos = isVertical ? new Vector3(0f, offset, 0.1f) : new Vector3(offset, 0f, 0.1f);
                seatLocalOffsets.Add(localPos);
            }
        }

        private void SetupColliders()
        {
            var boxCol = GetComponent<BoxCollider>();
            if (boxCol == null) boxCol = gameObject.AddComponent<BoxCollider>();

            float sizeX = isVertical ? 0.9f : length * 0.95f;
            float sizeY = isVertical ? length * 0.95f : 0.9f;
            boxCol.size = new Vector3(sizeX, sizeY, 0.8f);
            boxCol.center = Vector3.zero;
        }

        public override bool TryAddPassenger(out Vector3 targetSeatWorldPos)
        {
            targetSeatWorldPos = Vector3.zero;
            if (IsFull) return false;

            int seatIndex = passengerCount;
            Vector3 localSeat = seatIndex < seatLocalOffsets.Count ? seatLocalOffsets[seatIndex] : Vector3.zero;
            targetSeatWorldPos = transform.TransformPoint(localSeat);
            passengerCount++;

            StartCoroutine(SpawnPassengerDelayed(localSeat, 0.25f));

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
            }
            else
            {
                pass = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pass.transform.SetParent(transform);
                pass.transform.localScale = Vector3.one * 0.35f;
                var col = pass.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }

            pass.transform.localPosition = localSeat;
            var rend = pass.GetComponent<Renderer>();
            if (rend != null && colorPalette != null)
            {
                rend.material.color = colorPalette.GetColor(colorId);
            }
            spawnedPassengers.Add(pass);
        }

        public override void OnFull()
        {
            StartCoroutine(DriveAwayRoutine());
        }

        private IEnumerator DriveAwayRoutine()
        {
            yield return new WaitForSeconds(0.4f);

            Vector3 driveDir = isVertical ? Vector3.up : Vector3.right;
            float elapsed = 0f;
            float duration = 0.7f;
            Vector3 startPos = transform.position;
            Vector3 endPos = startPos + driveDir * 12f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                t = t * t; // Ease in
                transform.position = Vector3.Lerp(startPos, endPos, t);
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
