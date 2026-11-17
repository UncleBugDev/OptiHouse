#pragma warning disable 0618
using System;
using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Safe Ground Loot Clutter Optimizer for OptiHouse.
    /// - Puts motionless ground item Rigidbodies to sleep to stop continuous PhysX collision checks.
    /// - Pauses distant (>18m) ground drop sparkle particles and water Floating ticks.
    /// - Caches all component references on Awake for zero per-frame heap allocations.
    /// - NEVER alters item stacks or destroys ZDOs (100% multiplayer & save safe).
    /// </summary>
    public class ItemDropOptimizer : MonoBehaviour
    {
        public static ItemDropOptimizer? Instance { get; private set; }

        private class TrackedDrop
        {
            public int InstanceId;
            public ItemDrop Drop = null!;
            public Transform Transform = null!;
            public Rigidbody? Body;
            public Floating? Floating;
            public ParticleSystem[] Particles = Array.Empty<ParticleSystem>();
            public bool IsCulled;
        }

        private static readonly List<TrackedDrop> _drops = new List<TrackedDrop>();
        private static readonly Dictionary<int, TrackedDrop> _dropLookup = new Dictionary<int, TrackedDrop>();

        private static int _sleepingRigidbodiesCount;

        public static int TotalDroppedItems => _drops.Count;
        public static int SleepingItemsCount => _sleepingRigidbodiesCount;

        private float _physicsTimer;
        private int _lodIndex;
        private bool _wasActive = true;

        private void Awake()
        {
            Instance = this;
            ScanExistingDrops();
        }

        private void OnDestroy()
        {
            RestoreAllDrops();
            _drops.Clear();
            _dropLookup.Clear();
            _sleepingRigidbodiesCount = 0;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static void ScanExistingDrops()
        {
            ItemDrop[] drops = FindObjectsByType<ItemDrop>(FindObjectsSortMode.None);
            if (drops != null)
            {
                for (int i = 0; i < drops.Length; i++)
                {
                    if (drops[i] != null)
                    {
                        RegisterItemDrop(drops[i]);
                    }
                }
            }
        }

        public static void RegisterItemDrop(ItemDrop drop)
        {
            if (drop == null || drop.gameObject == null) return;

            int id = drop.GetInstanceID();
            if (_dropLookup.ContainsKey(id)) return;

            var entry = new TrackedDrop
            {
                InstanceId = id,
                Drop = drop,
                Transform = drop.transform,
                Body = drop.GetComponent<Rigidbody>(),
                Floating = drop.GetComponent<Floating>(),
                Particles = drop.GetComponentsInChildren<ParticleSystem>(true) ?? Array.Empty<ParticleSystem>(),
                IsCulled = false
            };

            _drops.Add(entry);
            _dropLookup[id] = entry;
        }

        public static void UnregisterItemDrop(ItemDrop drop)
        {
            if (drop == null) return;

            int id = drop.GetInstanceID();
            if (_dropLookup.TryGetValue(id, out TrackedDrop entry))
            {
                RestoreDrop(entry);
                _drops.Remove(entry);
                _dropLookup.Remove(id);
            }
        }

        private void Update()
        {
            bool isActive = OptiHousePlugin.IsOptimizationActive &&
                            ModConfig.EnableItemDropOptimization != null &&
                            ModConfig.EnableItemDropOptimization.Value;

            if (!isActive)
            {
                if (_wasActive)
                {
                    _wasActive = false;
                    RestoreAllDrops();
                }
                return;
            }

            _wasActive = true;

            _physicsTimer += Time.deltaTime;
            if (_physicsTimer >= 0.5f)
            {
                _physicsTimer = 0f;
                ProcessPhysicsSleepAndLOD();
            }
        }

        private void ProcessPhysicsSleepAndLOD()
        {
            if (_drops.Count == 0)
            {
                _sleepingRigidbodiesCount = 0;
                return;
            }

            Vector3 refPos = OptimizationHelpers.GetReferencePosition();
            float vfxDist = ModConfig.DroppedItemVfxCullDistance != null
                ? ModConfig.DroppedItemVfxCullDistance.Value
                : 18.0f;
            float vfxDistSq = vfxDist * vfxDist;

            bool enableSleep = ModConfig.EnableDroppedItemPhysicsSleep != null &&
                               ModConfig.EnableDroppedItemPhysicsSleep.Value;

            int sleepingCount = 0;
            int count = _drops.Count;
            int batch = Mathf.Min(100, count);

            for (int i = 0; i < batch; i++)
            {
                _lodIndex++;
                if (_lodIndex >= _drops.Count)
                {
                    _lodIndex = 0;
                }

                TrackedDrop entry = _drops[_lodIndex];
                if (entry.Drop == null || entry.Transform == null)
                {
                    _dropLookup.Remove(entry.InstanceId);
                    _drops.RemoveAt(_lodIndex);
                    if (_lodIndex > 0) _lodIndex--;
                    continue;
                }

                if (!entry.Drop.gameObject.activeInHierarchy)
                {
                    continue;
                }

                // 1. Rigidbody Physics Sleep on settled motionless items (avoid sleeping items actively floating on liquid)
                bool isFloatingInLiquid = entry.Floating != null && entry.Floating.HaveLiquidLevel();
                if (enableSleep && !isFloatingInLiquid && entry.Body != null && !entry.Body.isKinematic)
                {
                    if (entry.Body.IsSleeping())
                    {
                        sleepingCount++;
                    }
                    else if (entry.Body.velocity.sqrMagnitude < 0.03f && entry.Body.angularVelocity.sqrMagnitude < 0.03f)
                    {
                        entry.Body.Sleep();
                        sleepingCount++;
                    }
                }

                // 2. Sparkle particle & floating script distance culling
                float sqrDist = (entry.Transform.position - refPos).sqrMagnitude;
                bool shouldCullVfx = sqrDist > vfxDistSq;

                if (shouldCullVfx && !entry.IsCulled)
                {
                    entry.IsCulled = true;
                    for (int p = 0; p < entry.Particles.Length; p++)
                    {
                        ParticleSystem ps = entry.Particles[p];
                        if (ps != null && ps.isPlaying)
                        {
                            ps.Pause();
                        }
                    }
                    if (entry.Floating != null && entry.Floating.enabled)
                    {
                        entry.Floating.enabled = false;
                    }
                }
                else if (!shouldCullVfx && entry.IsCulled)
                {
                    RestoreDrop(entry);
                }
            }

            // Scale estimate cleanly if _drops > batch
            _sleepingRigidbodiesCount = batch > 0 ? Mathf.RoundToInt(sleepingCount * ((float)_drops.Count / batch)) : 0;
        }

        private static void RestoreDrop(TrackedDrop entry)
        {
            if (!entry.IsCulled) return;
            entry.IsCulled = false;

            for (int p = 0; p < entry.Particles.Length; p++)
            {
                ParticleSystem ps = entry.Particles[p];
                if (ps != null && ps.isPaused)
                {
                    ps.Play();
                }
            }

            if (entry.Floating != null && !entry.Floating.enabled)
            {
                entry.Floating.enabled = true;
            }
        }

        public static void RestoreAllDrops()
        {
            for (int i = 0; i < _drops.Count; i++)
            {
                RestoreDrop(_drops[i]);
            }
            _sleepingRigidbodiesCount = 0;
        }
    }

    #region Harmony Patches

    [HarmonyPatch(typeof(ItemDrop), "Awake")]
    public static class ItemDropOptimizer_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemDrop __instance)
        {
            ItemDropOptimizer.RegisterItemDrop(__instance);
        }
    }

    [HarmonyPatch(typeof(ItemDrop), "OnDestroy")]
    public static class ItemDropOptimizer_OnDestroy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemDrop __instance)
        {
            ItemDropOptimizer.UnregisterItemDrop(__instance);
        }
    }

    #endregion
}
