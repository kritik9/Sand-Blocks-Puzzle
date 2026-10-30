using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SandBlocks.Core.Boundary;

namespace SandBlocks.Core.Simulation
{
    public class PBDCrowdSimulation : MonoBehaviour, IParticleSimulation
    {
        [Header("Simulation Settings")]
        [SerializeField] private float gravity = -20f;
        [SerializeField] private int solverIterations = 5;
        [SerializeField] private float damping = 0.88f;
        [SerializeField] private float friction = 0.65f;
        [SerializeField] private float sleepVelocityThreshold = 0.22f;

        [Header("Boundary Exception Margin")]
        [Tooltip("Depth of the safety exception margin below the floor. Any particle pushed deeper than the floor line within this margin is caught, protected from falling, and restored to the floor level.")]
        [SerializeField] private float floorExceptionMargin = 0.8f;
        [Tooltip("Cushion margin above the floor where downward velocity is decelerated to cushion impact and prevent penetration.")]
        [SerializeField] private float floorCushionMargin = 0.35f;

        public float FloorExceptionMargin { get => floorExceptionMargin; set => floorExceptionMargin = value; }
        public float FloorCushionMargin { get => floorCushionMargin; set => floorCushionMargin = value; }

        private NativeArray<CrowdParticle> particles;
        private int activeCount;
        private int remainingActiveCount;
        private int capacity;
        private IContainerBoundary boundary;
        private bool isInitialized;

        public int ActiveCount => activeCount;
        public int RemainingActiveCount => (!isInitialized || activeCount == 0) ? 0 : remainingActiveCount;
        public int Capacity => capacity;
        public bool IsInitialized => isInitialized;
        public NativeArray<CrowdParticle> Particles => particles;

        public void Initialize(int maxCapacity, IContainerBoundary boundary)
        {
            if (isInitialized) Dispose();

            capacity = maxCapacity;
            this.boundary = boundary;
            particles = new NativeArray<CrowdParticle>(capacity, Allocator.Persistent);
            activeCount = 0;
            remainingActiveCount = 0;
            isInitialized = true;
        }

        public void SpawnParticle(float2 position, int colorId, float radius, bool startSettled = true)
        {
            if (activeCount >= capacity) return;

            var p = new CrowdParticle
            {
                position = position,
                prevPosition = position,
                velocity = float2.zero,
                radius = radius,
                colorId = colorId,
                isActive = true,
                isSettled = startSettled
            };

            particles[activeCount] = p;
            activeCount++;
            remainingActiveCount++;
        }

        public void StepSimulation(float deltaTime)
        {
            if (!isInitialized || activeCount == 0) return;

            float dt = Mathf.Min(deltaTime, 0.025f);
            float2 grav = new float2(0f, gravity * dt);

            float safetyFloorY = (boundary != null ? boundary.BottomY : -2.5f);
            float effectiveExceptionMargin = (boundary != null ? boundary.FloorExceptionMargin : floorExceptionMargin);
            float floorLevel = safetyFloorY;

            // 1. Predict positions ONLY for awake particles
            bool anyAwake = false;
            for (int i = 0; i < activeCount; i++)
            {
                var p = particles[i];
                if (!p.isActive) continue;

                if (!p.isSettled)
                {
                    anyAwake = true;
                    p.velocity += grav;
                    p.velocity *= damping;

                    // Cushion Margin Above Floor: Smoothly damp downward velocity when approaching floor
                    float floorWithRadius = floorLevel + p.radius;
                    if (p.position.y <= floorWithRadius + floorCushionMargin && p.velocity.y < 0f)
                    {
                        p.velocity.y *= 0.5f;
                    }

                    // Exception Margin: If at or below floor, arrest downward velocity completely
                    if (p.position.y <= floorWithRadius)
                    {
                        p.velocity.y = math.max(0f, p.velocity.y);
                    }

                    p.prevPosition = p.position;
                    p.position += p.velocity * dt;
                }
                else
                {
                    p.velocity = float2.zero;
                    p.prevPosition = p.position;
                }
                particles[i] = p;
            }

            // If all particles are settled, skip expensive constraint solving entirely
            if (!anyAwake) return;

            // 2. Constraint Projection Iterations
            for (int it = 0; it < solverIterations; it++)
            {
                // Boundary constraints for awake particles
                if (boundary != null)
                {
                    for (int i = 0; i < activeCount; i++)
                    {
                        var p = particles[i];
                        if (!p.isActive || p.isSettled) continue;

                        float2 projected = boundary.ProjectInside(p.position, p.radius);
                        // If pushed upward by boundary floor, arrest downward velocity
                        if (projected.y > p.position.y)
                        {
                            p.velocity.y = math.max(0f, p.velocity.y);
                            p.velocity.x *= (1f - friction);
                        }
                        p.position = projected;
                        particles[i] = p;
                    }
                }

                // Particle vs Particle collisions
                for (int i = 0; i < activeCount; i++)
                {
                    var pA = particles[i];
                    if (!pA.isActive) continue;

                    for (int j = i + 1; j < activeCount; j++)
                    {
                        var pB = particles[j];
                        if (!pB.isActive) continue;

                        // Skip if BOTH are already settled: Zero jelly / micro-jittering!
                        if (pA.isSettled && pB.isSettled) continue;

                        float2 diff = pA.position - pB.position;
                        float distSq = math.lengthsq(diff);
                        float minDist = pA.radius + pB.radius;

                        if (distSq < minDist * minDist && distSq > 0.00001f)
                        {
                            float dist = math.sqrt(distSq);
                            float overlap = minDist - dist;
                            float2 normal = diff / dist;

                            if (pA.isSettled && !pB.isSettled)
                            {
                                // A is resting solidly like a floor, only B is pushed away
                                pB.position -= normal * overlap;
                                float normalVel = math.dot(pB.velocity, normal);
                                if (normalVel < 0f) pB.velocity -= normal * normalVel;
                                pB.velocity.x *= (1f - friction);
                            }
                            else if (!pA.isSettled && pB.isSettled)
                            {
                                // B is resting solidly like a floor, only A is pushed away
                                pA.position += normal * overlap;
                                float normalVel = math.dot(pA.velocity, -normal);
                                if (normalVel < 0f) pA.velocity += normal * normalVel;
                                pA.velocity.x *= (1f - friction);
                            }
                            else
                            {
                                // Both are awake: split overlap evenly
                                float delta = 0.5f * overlap;
                                pA.position += normal * delta;
                                pB.position -= normal * delta;

                                // Tangential friction
                                float2 relVel = pA.velocity - pB.velocity;
                                float2 tangent = new float2(-normal.y, normal.x);
                                float tangVel = math.dot(relVel, tangent);
                                pA.velocity -= tangent * (tangVel * friction * 0.5f);
                                pB.velocity += tangent * (tangVel * friction * 0.5f);
                            }

                            particles[j] = pB;
                        }
                    }
                    particles[i] = pA;
                }
            }

            // 2.5 Post-collision boundary safety enforcement: ensure particles are strictly inside
            if (boundary != null)
            {
                for (int i = 0; i < activeCount; i++)
                {
                    var p = particles[i];
                    if (!p.isActive) continue;

                    float2 projected = boundary.ProjectInside(p.position, p.radius);
                    if (projected.y > p.position.y)
                    {
                        p.velocity.y = math.max(0f, p.velocity.y);
                    }
                    p.position = projected;
                    particles[i] = p;
                }
            }

            // 3. Finalize velocities, safety clamp, and sleep check
            float sqrSleep = sleepVelocityThreshold * sleepVelocityThreshold;

            for (int i = 0; i < activeCount; i++)
            {
                var p = particles[i];
                if (!p.isActive) continue;

                // Floor Exception Margin Catch: If any particle is pushed below the floor by crowd weight,
                // restore it solidly to the floor, arrest velocity, and settle it!
                float restingFloorY = floorLevel + p.radius;
                if (p.position.y < restingFloorY)
                {
                    p.position.y = restingFloorY;
                    p.velocity = float2.zero;
                    p.isSettled = true;
                }

                if (p.isSettled)
                {
                    particles[i] = p;
                    continue;
                }

                p.velocity = (p.position - p.prevPosition) / dt;

                // Apply strong resting friction
                p.velocity.x *= math.clamp(1f - friction * dt * 25f, 0f, 1f);

                if (math.lengthsq(p.velocity) < sqrSleep)
                {
                    p.isSettled = true;
                    p.velocity = float2.zero;
                }

                particles[i] = p;
            }
        }

        public int GetLowestParticleColor()
        {
            if (!isInitialized || activeCount == 0) return -1;

            float lowestY = float.MaxValue;
            int lowestColor = -1;

            for (int i = 0; i < activeCount; i++)
            {
                var p = particles[i];
                if (!p.isActive) continue;

                if (p.position.y < lowestY)
                {
                    lowestY = p.position.y;
                    lowestColor = p.colorId;
                }
            }
            return lowestColor;
        }

        public bool TryExtractLowestParticle(int colorId, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (!isInitialized || activeCount == 0) return false;

            float lowestY = float.MaxValue;
            int targetIdx = -1;

            for (int i = 0; i < activeCount; i++)
            {
                var p = particles[i];
                if (!p.isActive || p.colorId != colorId) continue;

                if (p.position.y < lowestY)
                {
                    lowestY = p.position.y;
                    targetIdx = i;
                }
            }

            if (targetIdx >= 0)
            {
                var target = particles[targetIdx];
                var renderer = FindAnyObjectByType<SandBlocks.Core.Rendering.InstancedCrowdRenderer>();
                if (renderer != null)
                {
                    worldPos = renderer.GetCalculatedWorldPosition(target.position);
                }
                else
                {
                    bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
                    float elevationY = (boundary != null) ? boundary.SurfaceElevationY : 0.08f;
                    worldPos = isHorizontal ? new Vector3(target.position.x, elevationY, target.position.y) : new Vector3(target.position.x, target.position.y, 0f);
                }
                target.isActive = false;
                particles[targetIdx] = target;
                if (remainingActiveCount > 0) remainingActiveCount--;

                // Wake up nearby particles to trigger cascade
                WakeNeighbors(target.position, target.radius * 3.5f);
                return true;
            }

            return false;
        }

        public bool HasColorOnBottomBoundary(int colorId, float bottomY, float minX, float maxX, float tolerance = 0.65f)
        {
            if (!isInitialized || activeCount == 0) return false;

            float margin = (boundary != null) ? boundary.FloorExceptionMargin : 0.8f;
            float lowerCatchY = bottomY - margin;
            for (int i = 0; i < activeCount; i++)
            {
                var p = particles[i];
                if (!p.isActive || p.colorId != colorId) continue;
                // A particle rests on floor at bottomY + p.radius. Center must be within tolerance above resting height.
                float thresholdY = bottomY + p.radius + Mathf.Max(tolerance, 0.02f);
                if (p.position.y >= lowerCatchY && p.position.y <= thresholdY && p.position.x >= minX && p.position.x <= maxX)
                {
                    return true;
                }
            }
            return false;
        }

        public bool HasColorOnBottomBoundary(int colorId, float bottomY, float tolerance = 0.65f)
        {
            return HasColorOnBottomBoundary(colorId, bottomY, float.MinValue, float.MaxValue, tolerance);
        }

        public bool TryExtractBottomParticle(int colorId, float bottomY, float minX, float maxX, float targetX, out Vector3 worldPos, float tolerance = 0.65f)
        {
            worldPos = Vector3.zero;
            if (!isInitialized || activeCount == 0) return false;

            float margin = (boundary != null) ? boundary.FloorExceptionMargin : 0.8f;
            float lowerCatchY = bottomY - margin;
            float bestDistSq = float.MaxValue;
            int targetIdx = -1;

            for (int i = 0; i < activeCount; i++)
            {
                var p = particles[i];
                if (!p.isActive || p.colorId != colorId) continue;

                float thresholdY = bottomY + p.radius + Mathf.Max(tolerance, 0.02f);
                if (p.position.y >= lowerCatchY && p.position.y <= thresholdY && p.position.x >= minX && p.position.x <= maxX)
                {
                    float dx = p.position.x - targetX;
                    float distSq = dx * dx + (p.position.y - bottomY) * (p.position.y - bottomY);

                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        targetIdx = i;
                    }
                }
            }

            if (targetIdx >= 0)
            {
                var target = particles[targetIdx];
                float safeY = math.max(target.position.y, bottomY + target.radius);
                var renderer = FindAnyObjectByType<SandBlocks.Core.Rendering.InstancedCrowdRenderer>();
                if (renderer != null)
                {
                    worldPos = renderer.GetCalculatedWorldPosition(new float2(target.position.x, safeY));
                }
                else
                {
                    bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
                    float elevationY = (boundary != null) ? boundary.SurfaceElevationY : 0.08f;
                    worldPos = isHorizontal ? new Vector3(target.position.x, elevationY, safeY) : new Vector3(target.position.x, safeY, 0f);
                }
                target.isActive = false;
                particles[targetIdx] = target;
                if (remainingActiveCount > 0) remainingActiveCount--;

                WakeNeighbors(target.position, target.radius * 3.5f);
                return true;
            }

            return false;
        }

        public bool TryExtractBottomParticle(int colorId, float bottomY, float targetX, out Vector3 worldPos, float tolerance = 0.65f)
        {
            return TryExtractBottomParticle(colorId, bottomY, float.MinValue, float.MaxValue, targetX, out worldPos, tolerance);
        }

        public void WakeNeighbors(float2 center, float radius)
        {
            float rSq = radius * radius;
            for (int i = 0; i < activeCount; i++)
            {
                var p = particles[i];
                if (!p.isActive) continue;

                bool inRadius = math.distancesq(p.position, center) <= rSq;
                bool inColumnAbove = (p.position.y > center.y) && (math.abs(p.position.x - center.x) <= radius * 0.95f);

                if (inRadius || inColumnAbove)
                {
                    p.isSettled = false;
                    particles[i] = p;
                }
            }
        }

                /// <summary>
        /// Explicit count of particles with isActive == true.
        /// </summary>
        public int CountActiveParticles()
        {
            if (!isInitialized || activeCount == 0) return 0;
            int count = 0;
            for (int i = 0; i < activeCount; i++)
            {
                if (particles[i].isActive) count++;
            }
            return count;
        }

        public void Dispose()
        {
            if (isInitialized && particles.IsCreated)
            {
                particles.Dispose();
            }
            isInitialized = false;
            activeCount = 0;
            remainingActiveCount = 0;
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
