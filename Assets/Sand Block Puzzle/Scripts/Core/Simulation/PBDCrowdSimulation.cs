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
        [SerializeField] private float gravity = -18f;
        [SerializeField] private int solverIterations = 4;
        [SerializeField] private float damping = 0.94f;
        [SerializeField] private float friction = 0.25f;
        [SerializeField] private float sleepVelocityThreshold = 0.08f;

        private NativeArray<CrowdParticle> particles;
        private int activeCount;
        private int capacity;
        private IContainerBoundary boundary;
        private bool isInitialized;

        public int ActiveCount => activeCount;
        public NativeArray<CrowdParticle> Particles => particles;

        public void Initialize(int maxCapacity, IContainerBoundary boundary)
        {
            if (isInitialized) Dispose();

            capacity = maxCapacity;
            this.boundary = boundary;
            particles = new NativeArray<CrowdParticle>(capacity, Allocator.Persistent);
            activeCount = 0;
            isInitialized = true;
        }

        public void SpawnParticle(float2 position, int colorId, float radius)
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
                isSettled = false
            };

            particles[activeCount] = p;
            activeCount++;
        }

        public void StepSimulation(float deltaTime)
        {
            if (!isInitialized || activeCount == 0) return;

            float dt = Mathf.Min(deltaTime, 0.033f);
            float2 grav = new float2(0f, gravity * dt);

            // 1. Predict positions
            for (int i = 0; i < activeCount; i++)
            {
                var p = particles[i];
                if (!p.isActive) continue;

                if (!p.isSettled)
                {
                    p.velocity += grav;
                    p.velocity *= damping;
                    p.prevPosition = p.position;
                    p.position += p.velocity * dt;
                }
                particles[i] = p;
            }

            // 2. Constraint Projection Iterations
            for (int it = 0; it < solverIterations; it++)
            {
                // Boundary constraints
                if (boundary != null)
                {
                    for (int i = 0; i < activeCount; i++)
                    {
                        var p = particles[i];
                        if (!p.isActive) continue;
                        p.position = boundary.ProjectInside(p.position, p.radius);
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

                        float2 diff = pA.position - pB.position;
                        float distSq = math.lengthsq(diff);
                        float minDist = pA.radius + pB.radius;

                        if (distSq < minDist * minDist && distSq > 0.00001f)
                        {
                            float dist = math.sqrt(distSq);
                            float delta = 0.5f * (minDist - dist);
                            float2 normal = diff / dist;

                            // Project apart
                            pA.position += normal * delta;
                            pB.position -= normal * delta;

                            // Lateral friction
                            float2 relVel = pA.velocity - pB.velocity;
                            float2 tangent = new float2(-normal.y, normal.x);
                            float tangVel = math.dot(relVel, tangent);
                            pA.velocity -= tangent * (tangVel * friction);
                            pB.velocity += tangent * (tangVel * friction);

                            // Wake up on collision
                            pA.isSettled = false;
                            pB.isSettled = false;

                            particles[j] = pB;
                        }
                    }
                    particles[i] = pA;
                }
            }

            // 3. Finalize velocities and sleep check
            float sqrSleep = sleepVelocityThreshold * sleepVelocityThreshold;
            for (int i = 0; i < activeCount; i++)
            {
                var p = particles[i];
                if (!p.isActive) continue;

                p.velocity = (p.position - p.prevPosition) / dt;
                if (math.lengthsq(p.velocity) < sqrSleep)
                {
                    p.isSettled = true;
                    p.velocity = float2.zero;
                }
                else
                {
                    p.isSettled = false;
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
                worldPos = new Vector3(target.position.x, target.position.y, 0f);
                target.isActive = false;
                particles[targetIdx] = target;

                // Wake up nearby particles to trigger cascade
                WakeNeighbors(target.position, target.radius * 4f);
                return true;
            }

            return false;
        }

        private void WakeNeighbors(float2 center, float radius)
        {
            float rSq = radius * radius;
            for (int i = 0; i < activeCount; i++)
            {
                var p = particles[i];
                if (!p.isActive) continue;
                if (math.distancesq(p.position, center) <= rSq)
                {
                    p.isSettled = false;
                    particles[i] = p;
                }
            }
        }

        public void Dispose()
        {
            if (isInitialized && particles.IsCreated)
            {
                particles.Dispose();
            }
            isInitialized = false;
            activeCount = 0;
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
