using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// WearNTear Equilibrium Support Cache, Weather Cover &amp; Ashlands Loop Optimizer (OptiHouse v2.0).
    /// 1. Equilibrium Support Cache: Fixes Iron Gate's ground/foundation/rock cache-miss bug in WearNTear.UpdateSupport()
    ///    where pieces touching terrain or rocks re-run Physics.OverlapBoxNonAlloc every single second forever.
    ///    Once a piece's support converges on two consecutive vanilla passes and HaveSupport() is true, skips redundant
    ///    OverlapBoxNonAlloc sweeps until ANY structural, terrain, rock, or tree change increments _structureWorldEpoch.
    /// 2. Weather Cover Throttling: Real-time throttles UpdateCover 100m SphereCastNonAlloc during rain/storms.
    /// 3. Non-Ashlands Shader Loop Skip: Skips per-second UpdateAshlandsMaterialValues outside Ashlands.
    /// NEVER disables or modifies any Collider components.
    /// </summary>
    public static class WearNTearOptimizer
    {
        public struct SupportPrefixState
        {
            public bool RanVanilla;
            public float PrevSupport;
        }

        private class CoverThrottleState
        {
            public float LastRaycastTime = -999f;
        }

        private class SupportCacheEntry
        {
            public int SettledEpoch = -1;
            public float ExpireTime;
            public Vector3 LastPos;
        }

        private static readonly ConditionalWeakTable<WearNTear, CoverThrottleState> _coverStates = new();
        private static readonly ConditionalWeakTable<WearNTear, SupportCacheEntry> _supportCache = new();

        private static readonly Func<WearNTear, bool>? _haveSupportDelegate = CreateHaveSupportDelegate();

        private static int _structureWorldEpoch = 1;
        [ThreadStatic]
        private static bool _insideUpdateSupport;

        private static long _raycastsThrottled;
        private static long _ashlandsUpdatesSkipped;
        private static long _supportSweepsSkipped;

        public static long RaycastsThrottled => _raycastsThrottled;
        public static long AshlandsUpdatesSkipped => _ashlandsUpdatesSkipped;
        public static long SupportSweepsSkipped => _supportSweepsSkipped;

        private static Func<WearNTear, bool>? CreateHaveSupportDelegate()
        {
            try
            {
                var method = AccessTools.Method(typeof(WearNTear), "HaveSupport");
                if (method != null)
                {
                    return AccessTools.MethodDelegate<Func<WearNTear, bool>>(method);
                }
            }
            catch { }
            return null;
        }

        public static void InvalidateStructureEquilibrium()
        {
            unchecked
            {
                _structureWorldEpoch++;
                if (_structureWorldEpoch <= 0) _structureWorldEpoch = 1;
            }
        }

        public static void ResetStats()
        {
            _raycastsThrottled = 0;
            _ashlandsUpdatesSkipped = 0;
            _supportSweepsSkipped = 0;
        }

        [HarmonyPatch(typeof(WearNTear), "UpdateSupport")]
        public static class WearNTear_UpdateSupport_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(
                WearNTear __instance,
                bool ___m_clearCachedSupport,
                float ___m_support,
                out SupportPrefixState __state)
            {
                __state = new SupportPrefixState
                {
                    RanVanilla = false,
                    PrevSupport = ___m_support
                };

                if (__instance == null ||
                    ___m_clearCachedSupport ||
                    ___m_support <= 0f ||
                    !OptiHousePlugin.IsOptimizationActive ||
                    ModConfig.EnableStructuralOptimization == null ||
                    !ModConfig.EnableStructuralOptimization.Value ||
                    ModConfig.EnableSupportEquilibriumCache == null ||
                    !ModConfig.EnableSupportEquilibriumCache.Value)
                {
                    __state.RanVanilla = true;
                    _insideUpdateSupport = true;
                    return true;
                }

                var entry = _supportCache.GetOrCreateValue(__instance);
                if (entry.SettledEpoch == _structureWorldEpoch &&
                    Time.time < entry.ExpireTime &&
                    (__instance.transform.position - entry.LastPos).sqrMagnitude < 0.001f)
                {
                    _supportSweepsSkipped++;
                    return false;
                }

                __state.RanVanilla = true;
                _insideUpdateSupport = true;
                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(
                WearNTear __instance,
                bool ___m_clearCachedSupport,
                float ___m_support,
                SupportPrefixState __state)
            {
                if (!__state.RanVanilla)
                {
                    return;
                }

                _insideUpdateSupport = false;
                if (__instance == null) return;

                var entry = _supportCache.GetOrCreateValue(__instance);

                // If this piece's support value changed, keep converging neighboring pieces
                if (!___m_support.Equals(__state.PrevSupport))
                {
                    InvalidateStructureEquilibrium();
                    entry.SettledEpoch = -1;
                    return;
                }

                bool hasSupport = _haveSupportDelegate != null ? _haveSupportDelegate(__instance) : ___m_support >= 10f;
                if (!___m_clearCachedSupport && hasSupport)
                {
                    entry.SettledEpoch = _structureWorldEpoch;
                    entry.LastPos = __instance.transform.position;
                    entry.ExpireTime = Time.time + UnityEngine.Random.Range(22f, 34f);
                }
                else
                {
                    entry.SettledEpoch = -1;
                }
            }
        }

        [HarmonyPatch(typeof(WearNTear), "ClearCachedSupport")]
        public static class WearNTear_ClearCachedSupport_Patch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                // External cache clear (terrain dig/level via Heightmap, neighbor piece event, or RPC)
                if (!_insideUpdateSupport)
                {
                    InvalidateStructureEquilibrium();
                }
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnPlaced))]
        public static class WearNTear_OnPlaced_Patch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                InvalidateStructureEquilibrium();
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy))]
        public static class ZNetScene_Destroy_Equilibrium_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(GameObject go)
            {
                if (go == null) return;

                // Invalidate structural cache if any potential support-bearing world object (tree, rock, piece) is destroyed
                if (go.GetComponent<ItemDrop>() == null && go.GetComponent<Character>() == null)
                {
                    InvalidateStructureEquilibrium();
                }
            }
        }

        [HarmonyPatch(typeof(MineRock5), "RPC_SetAreaHealth")]
        public static class MineRock5_SetAreaHealth_Equilibrium_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(float health)
            {
                if (health <= 0f)
                {
                    InvalidateStructureEquilibrium();
                }
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateCover))]
        public static class WearNTear_UpdateCover_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(WearNTear __instance, float dt, ref float ___m_updateCoverTimer, bool ___m_inAshlands)
            {
                if (!OptiHousePlugin.IsOptimizationActive ||
                    ModConfig.EnableStructuralOptimization == null ||
                    !ModConfig.EnableStructuralOptimization.Value ||
                    ModConfig.EnableWearNTearThrottling == null ||
                    !ModConfig.EnableWearNTearThrottling.Value)
                {
                    return true;
                }

                if (dt >= 50f) return true;
                if (___m_updateCoverTimer + dt <= 4f) return true;
                if (__instance.GetHealthPercentage() < 0.99f) return true;

                bool needsCheck = ___m_inAshlands || EnvMan.IsWet();
                if (!needsCheck) return true;

                float targetInterval = ModConfig.WearNTearThrottleInterval != null
                    ? Mathf.Max(4f, ModConfig.WearNTearThrottleInterval.Value)
                    : 10f;

                var state = _coverStates.GetOrCreateValue(__instance);
                float now = Time.time;

                if (state.LastRaycastTime >= 0f && now - state.LastRaycastTime < targetInterval)
                {
                    ___m_updateCoverTimer = 0f;
                    _raycastsThrottled++;
                    return false;
                }

                state.LastRaycastTime = now;
                return true;
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateAshlandsMaterialValues))]
        public static class WearNTear_UpdateAshlandsMaterialValues_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(
                float time,
                bool ___m_inAshlands,
                float ___m_lavaTimer,
                float ___m_ashDamageTime,
                float ___m_burnDamageTime,
                float ___m_ashMaterialValue,
                ref float ___m_lastMaterialValueTimeCheck)
            {
                if (!OptiHousePlugin.IsOptimizationActive ||
                    ModConfig.EnableStructuralOptimization == null ||
                    !ModConfig.EnableStructuralOptimization.Value ||
                    ModConfig.SkipNonAshlandsMaterialUpdates == null ||
                    !ModConfig.SkipNonAshlandsMaterialUpdates.Value)
                {
                    return true;
                }

                if (!___m_inAshlands &&
                    ___m_lavaTimer <= 0f &&
                    ___m_ashDamageTime <= 0f &&
                    ___m_burnDamageTime <= 0f &&
                    ___m_ashMaterialValue == 0f)
                {
                    ___m_lastMaterialValueTimeCheck = time;
                    _ashlandsUpdatesSkipped++;
                    return false;
                }

                return true;
            }
        }
    }
}
