#pragma warning disable 0618
using System;
using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Item Drop & Ground Loot Clutter Optimizer for OptiHouse.
    /// Eliminates severe physics, transform, and draw call overhead caused by hundreds of loose
    /// dropped items (wood logs, stone, resin, trophies, farming drops) around player bases.
    ///
    /// Key Capabilities:
    /// 1. Auto-Stacker: Periodically merges identical loose item drops within a 3.5m radius
    ///    into consolidated max-size stacks, removing redundant GameObjects and ZDOs.
    /// 2. Physics Sleep: Puts motionless item drop Rigidbodies to sleep so PhysX stops
    ///    computing continuous terrain/piece collision queries for settled ground items.
    /// 3. Particle & Floating LOD: Pauses sparkle particle effects and water floating scripts
    ///    on distant dropped items beyond 15m.
    /// </summary>
    public class ItemDropOptimizer : MonoBehaviour
    {
        private static ItemDropOptimizer? _instance;
        public static ItemDropOptimizer? Instance => _instance;

        private static readonly List<ItemDrop> _allItemDrops = new List<ItemDrop>();
        private static int _stackedItemsCount;
        private static int _sleepingRigidbodiesCount;

        public static int TotalDroppedItems => _allItemDrops.Count;
        public static int MergedDropsCount => _stackedItemsCount;
        public static int SleepingItemsCount => _sleepingRigidbodiesCount;

        private float _stackTimer;
        private float _physicsTimer;
        private int _lodIndex;

        private void Awake()
        {
            _instance = this;
            ScanExistingDrops();
        }

        private void OnDestroy()
        {
            _allItemDrops.Clear();
            _stackedItemsCount = 0;
            _sleepingRigidbodiesCount = 0;
            if (_instance == this)
            {
                _instance = null;
            }
        }

        public static void ScanExistingDrops()
        {
            ItemDrop[] drops = FindObjectsByType<ItemDrop>(FindObjectsSortMode.None);
            if (drops != null)
            {
                for (int i = 0; i < drops.Length; i++)
                {
                    RegisterItemDrop(drops[i]);
                }
            }
        }

        public static void RegisterItemDrop(ItemDrop drop)
        {
            if (drop == null || _allItemDrops.Contains(drop)) return;
            _allItemDrops.Add(drop);
        }

        public static void UnregisterItemDrop(ItemDrop drop)
        {
            if (drop == null) return;
            _allItemDrops.Remove(drop);
        }

        private void Update()
        {
            if (!OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableItemDropOptimization == null ||
                !ModConfig.EnableItemDropOptimization.Value)
            {
                return;
            }

            float dt = Time.deltaTime;

            // 1. Periodic Auto-Stacking of nearby loose drops (every 2.5 seconds)
            if (ModConfig.EnableAutoStackNearbyItems != null && ModConfig.EnableAutoStackNearbyItems.Value)
            {
                _stackTimer += dt;
                if (_stackTimer >= 2.5f)
                {
                    _stackTimer = 0f;
                    ProcessAutoStacking();
                }
            }

            // 2. Periodic Physics Sleep & Particle LOD (every 0.5 seconds)
            _physicsTimer += dt;
            if (_physicsTimer >= 0.5f)
            {
                _physicsTimer = 0f;
                ProcessPhysicsSleepAndLOD();
            }
        }

        private void ProcessAutoStacking()
        {
            if (_allItemDrops.Count < 2) return;

            float stackRadius = ModConfig.AutoStackRadius != null
                ? ModConfig.AutoStackRadius.Value
                : 3.5f;
            float stackRadiusSq = stackRadius * stackRadius;

            // Clean up destroyed items first
            _allItemDrops.RemoveAll(d => d == null || d.gameObject == null);

            for (int i = 0; i < _allItemDrops.Count; i++)
            {
                ItemDrop itemA = _allItemDrops[i];
                if (itemA == null || itemA.m_itemData == null) continue;

                ZNetView nviewA = itemA.GetComponent<ZNetView>();
                if (nviewA == null || !nviewA.IsValid()) continue;

                int maxStack = itemA.m_itemData.m_shared.m_maxStackSize;
                if (maxStack <= 1 || itemA.m_itemData.m_stack >= maxStack)
                {
                    continue;
                }

                Vector3 posA = itemA.transform.position;
                string nameA = itemA.m_itemData.m_shared.m_name;
                int qualityA = itemA.m_itemData.m_quality;
                int variantA = itemA.m_itemData.m_variant;

                for (int j = i + 1; j < _allItemDrops.Count; j++)
                {
                    ItemDrop itemB = _allItemDrops[j];
                    if (itemB == null || itemB.m_itemData == null) continue;

                    ZNetView nviewB = itemB.GetComponent<ZNetView>();
                    if (nviewB == null || !nviewB.IsValid()) continue;

                    if (itemB.m_itemData.m_shared.m_name != nameA ||
                        itemB.m_itemData.m_quality != qualityA ||
                        itemB.m_itemData.m_variant != variantA)
                    {
                        continue;
                    }

                    if ((itemB.transform.position - posA).sqrMagnitude <= stackRadiusSq)
                    {
                        // Found matching stackable item within radius
                        int spaceInA = maxStack - itemA.m_itemData.m_stack;
                        int transfer = Mathf.Min(spaceInA, itemB.m_itemData.m_stack);

                        if (transfer > 0)
                        {
                            itemA.SetStack(itemA.m_itemData.m_stack + transfer);

                            if (itemB.m_itemData.m_stack <= transfer)
                            {
                                if (!nviewB.IsOwner())
                                {
                                    nviewB.ClaimOwnership();
                                }
                                if (ZNetScene.instance != null)
                                {
                                    ZNetScene.instance.Destroy(itemB.gameObject);
                                }
                                else
                                {
                                    Destroy(itemB.gameObject);
                                }
                                _stackedItemsCount++;
                            }
                            else
                            {
                                itemB.SetStack(itemB.m_itemData.m_stack - transfer);
                            }

                            if (itemA.m_itemData.m_stack >= maxStack)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void ProcessPhysicsSleepAndLOD()
        {
            if (_allItemDrops.Count == 0) return;

            Vector3 refPos = GetReferencePosition();
            float vfxDist = ModConfig.DroppedItemVfxCullDistance != null
                ? ModConfig.DroppedItemVfxCullDistance.Value
                : 15.0f;
            float vfxDistSq = vfxDist * vfxDist;

            bool enableSleep = ModConfig.EnableDroppedItemPhysicsSleep != null &&
                               ModConfig.EnableDroppedItemPhysicsSleep.Value;

            int sleepingCount = 0;

            int count = _allItemDrops.Count;
            int batch = Mathf.Min(100, count);

            for (int i = 0; i < batch; i++)
            {
                _lodIndex++;
                if (_lodIndex >= _allItemDrops.Count)
                {
                    _lodIndex = 0;
                }

                ItemDrop drop = _allItemDrops[_lodIndex];
                if (drop == null || drop.gameObject == null) continue;

                // 1. Rigidbody Physics Sleep on settled items
                if (enableSleep)
                {
                    Rigidbody rb = drop.GetComponent<Rigidbody>();
                    if (rb != null && !rb.isKinematic)
                    {
                        if (rb.IsSleeping())
                        {
                            sleepingCount++;
                        }
                        else if (rb.velocity.sqrMagnitude < 0.04f && rb.angularVelocity.sqrMagnitude < 0.04f)
                        {
                            rb.Sleep();
                            sleepingCount++;
                        }
                    }
                }

                // 2. Sparkle particle & floating script distance culling
                float sqrDist = (drop.transform.position - refPos).sqrMagnitude;
                bool shouldCullVfx = sqrDist > vfxDistSq;

                ParticleSystem[] particles = drop.GetComponentsInChildren<ParticleSystem>(true);
                for (int p = 0; p < particles.Length; p++)
                {
                    ParticleSystem ps = particles[p];
                    if (ps == null) continue;

                    if (shouldCullVfx && ps.isPlaying)
                    {
                        ps.Pause();
                    }
                    else if (!shouldCullVfx && ps.isPaused)
                    {
                        ps.Play();
                    }
                }

                Floating floating = drop.GetComponent<Floating>();
                if (floating != null)
                {
                    floating.enabled = !shouldCullVfx;
                }
            }

            _sleepingRigidbodiesCount = sleepingCount;
        }

        private static Vector3 GetReferencePosition()
        {
            if (Player.m_localPlayer != null)
            {
                return Player.m_localPlayer.transform.position;
            }
            Camera mainCam = Utils.GetMainCamera();
            if (mainCam != null)
            {
                return mainCam.transform.position;
            }
            return Vector3.zero;
        }
    }

    #region Harmony Patches

    [HarmonyPatch(typeof(OptiHousePlugin), "Awake")]
    public static class ItemDropOptimizer_Plugin_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(OptiHousePlugin __instance)
        {
            if (__instance != null && __instance.gameObject.GetComponent<ItemDropOptimizer>() == null)
            {
                __instance.gameObject.AddComponent<ItemDropOptimizer>();
            }
        }
    }

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
