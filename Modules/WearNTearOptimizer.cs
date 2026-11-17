using System.Runtime.CompilerServices;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// WearNTear Weather Cover & Material Loop Optimizer.
    /// 1. Safely throttles WearNTear.UpdateCover (100m physics SphereCastNonAlloc checks during rain/storms)
    ///    using accurate real-time intervals (preventing the WearNTearUpdater single-frame deltaTime bug).
    /// 2. Skips WearNTearUpdater's per-second UpdateAshlandsMaterialValues loop on all non-Ashlands pieces
    ///    with zero burn/ash/lava timers, eliminating tens of thousands of wasted per-second iterations.
    /// NEVER touches UpdateSupport() or piece colliders.
    /// </summary>
    public static class WearNTearOptimizer
    {
        private class CoverThrottleState
        {
            public float LastRaycastTime = -999f;
        }

        private static readonly ConditionalWeakTable<WearNTear, CoverThrottleState> _coverStates = new();

        private static long _raycastsThrottled;
        private static long _ashlandsUpdatesSkipped;

        public static long RaycastsThrottled => _raycastsThrottled;
        public static long AshlandsUpdatesSkipped => _ashlandsUpdatesSkipped;

        public static void ResetStats()
        {
            _raycastsThrottled = 0;
            _ashlandsUpdatesSkipped = 0;
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

                // Forced cover checks (e.g. CanHaveSnow passes dt = 100f) always run immediately
                if (dt >= 50f)
                {
                    return true;
                }

                // Only intercept right before vanilla's > 4f threshold triggers a raycast
                if (___m_updateCoverTimer + dt <= 4f)
                {
                    return true;
                }

                // Damaged pieces (< 99% HP) keep vanilla responsiveness
                if (__instance.GetHealthPercentage() < 0.99f)
                {
                    return true;
                }

                // Outside wet weather / Ashlands / DeepNorth, vanilla UpdateCover does zero raycasts anyway
                bool needsCheck = ___m_inAshlands || EnvMan.IsWet();
                if (!needsCheck)
                {
                    return true;
                }

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
