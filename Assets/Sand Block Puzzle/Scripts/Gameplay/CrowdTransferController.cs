using System.Collections;
using UnityEngine;
using SandBlocks.Core.Boundary;
using SandBlocks.Core.Data;
using SandBlocks.Core.Simulation;
using SandBlocks.Puzzle.Input;
using SandBlocks.Puzzle.Vehicles;

namespace SandBlocks.Gameplay
{
    public class CrowdTransferController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PBDCrowdSimulation simulation;
        [SerializeField] private FunnelBoundary boundary;
        [SerializeField] private VehicleDragHandler dragHandler;
        [SerializeField] private CrowdColorPalette palette;
        [SerializeField] private GameObject commuterStreamPrefab;

        [Header("Transfer Tuning")]
        [SerializeField] private float transferInterval = 0.08f;
        [SerializeField] private float flightDuration = 0.35f;
        [SerializeField] private float hoverDetectionRadius = 1.6f;

        private float transferTimer;

        private void Awake()
        {
            if (simulation == null) simulation = FindAnyObjectByType<PBDCrowdSimulation>();
            if (boundary == null) boundary = FindAnyObjectByType<FunnelBoundary>();
            if (dragHandler == null) dragHandler = FindAnyObjectByType<VehicleDragHandler>();
        }

        private void Update()
        {
            transferTimer += Time.deltaTime;
            if (transferTimer >= transferInterval)
            {
                transferTimer = 0f;
                ProcessTransfer();
            }
        }

        private void ProcessTransfer()
        {
            if (simulation == null || boundary == null) return;

            VehicleBase activeVehicle = GetActiveVehicleNearNozzle();
            if (activeVehicle == null || activeVehicle.IsFull) return;

            int bottomColor = simulation.GetLowestParticleColor();
            if (bottomColor < 0 || bottomColor != activeVehicle.ColorId) return;

            if (simulation.TryExtractLowestParticle(activeVehicle.ColorId, out Vector3 startPos))
            {
                if (activeVehicle.TryAddPassenger(out Vector3 seatPos))
                {
                    StartCoroutine(AnimateCommuterFlight(startPos, seatPos, activeVehicle.ColorId));
                }
            }
        }

        private VehicleBase GetActiveVehicleNearNozzle()
        {
            Vector3 nozzlePos = new Vector3(boundary.NozzlePosition.x, boundary.NozzlePosition.y, 0f);

            // 1. Check if player is currently dragging/hovering a vehicle near nozzle
            if (dragHandler != null && dragHandler.HoveredVehicle != null)
            {
                float dist = Vector3.Distance(dragHandler.HoveredVehicle.transform.position, nozzlePos);
                if (dist <= hoverDetectionRadius)
                {
                    return dragHandler.HoveredVehicle;
                }
            }

            // 2. Overlap sphere query for placed/docked vehicles near nozzle
            Collider[] hits = Physics.OverlapSphere(nozzlePos, hoverDetectionRadius);
            for (int i = 0; i < hits.Length; i++)
            {
                var v = hits[i].GetComponentInParent<VehicleBase>();
                if (v != null && !v.IsFull) return v;
            }

            return null;
        }

        private IEnumerator AnimateCommuterFlight(Vector3 start, Vector3 end, int colorId)
        {
            GameObject commuter;
            if (commuterStreamPrefab != null)
            {
                commuter = Instantiate(commuterStreamPrefab, start, Quaternion.identity);
            }
            else
            {
                commuter = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                commuter.transform.position = start;
                commuter.transform.localScale = Vector3.one * 0.35f;
                var col = commuter.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }

            var rend = commuter.GetComponent<Renderer>();
            if (rend != null && palette != null)
            {
                rend.material.color = palette.GetColor(colorId);
            }

            // Quadratic Bezier arc control point
            Vector3 midPoint = (start + end) * 0.5f + Vector3.up * 0.8f;
            float elapsed = 0f;

            while (elapsed < flightDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / flightDuration);

                // Bezier formula: (1-t)^2 * P0 + 2*(1-t)*t * P1 + t^2 * P2
                float oneMinusT = 1f - t;
                Vector3 pos = oneMinusT * oneMinusT * start + 2f * oneMinusT * t * midPoint + t * t * end;
                commuter.transform.position = pos;

                yield return null;
            }

            Destroy(commuter);
        }
    }
}
