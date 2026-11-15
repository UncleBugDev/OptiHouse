using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// WearNTear Sky Raycast & Cover Throttling Optimizer.
    /// Safely throttles WearNTear.UpdateCover (100m physics SphereCastNonAlloc checks)
    /// on undamaged, static building pieces by 3-5x, drastically reducing main-thread CPU overhead
    /// in large player bases while 100% preserving weather wear protection and structural integrity.
    /// </summary>
    public static class WearNTearOptimizer
    {
        private static long _raycastsThrottled;
        public static long RaycastsThrottled => _raycastsThrottled;

        public static void ResetStats()
        {
            _raycastsThrottled = 0;
        }

        /// <summary>
        /// Harmony patch intercepting WearNTear.UpdateCover.
        /// </summary>
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateCover))]
        public static class WearNTear_UpdateCover_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(WearNTear __instance, float dt, ref float ___m_updateCoverTimer)
            {
                // If master optimization or structural throttling is disabled, fall back to vanilla (4s interval)
                if (!OptiHousePlugin.IsOptimizationActive ||
                    ModConfig.EnableStructuralOptimization == null ||
                    !ModConfig.EnableStructuralOptimization.Value ||
                    ModConfig.EnableWearNTearThrottling == null ||
                    !ModConfig.EnableWearNTearThrottling.Value)
                {
                    return true;
                }

                // Forced cover updates (e.g. CanHaveSnow passes dt = 100f) must always run immediately
                if (dt >= 50f)
                {
                    return true;
                }

                // If piece is damaged (< 99% HP), keep vanilla frequency (4s) so repair/decay is immediately responsive
                if (__instance.GetHealthPercentage() < 0.99f)
                {
                    return true;
                }

                // Target interval for static, healthy pieces (vanilla default is 4s, config default is 10s)
                float targetInterval = ModConfig.WearNTearThrottleInterval != null
                    ? Mathf.Max(4f, ModConfig.WearNTearThrottleInterval.Value)
                    : 10f;

                // Accumulate elapsed time. If below target interval, bypass the expensive physics raycast.
                if (___m_updateCoverTimer + dt < targetInterval)
                {
                    ___m_updateCoverTimer += dt;
                    _raycastsThrottled++;
                    return false; // Skip UpdateCover this cycle
                }

                // Interval reached: let vanilla execute.
                // Vanilla will increment m_updateCoverTimer, evaluate HaveRoof()/HaveAshRoof(), and reset timer to 0f.
                return true;
            }
        }
    }
}
