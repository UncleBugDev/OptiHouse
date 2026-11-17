using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Ballista / Turret Idle Scan Optimizer for OptiHouse v2.0.
    /// Throttles redundant 50 Hz FixedUpdate quaternion rotation & visual bolt checks on distant (>28m)
    /// or off-screen targetless turrets while keeping m_updateTargetTimer 100% accurate to the millisecond.
    /// </summary>
    public static class TurretOptimizer
    {
        public static long IdleTicksSkipped { get; internal set; }
    }

    [HarmonyPatch(typeof(Turret), "FixedUpdate")]
    public static class Turret_FixedUpdate_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            Turret __instance,
            ZNetView ___m_nview,
            bool ___m_haveTarget,
            ref float ___m_updateTargetTimer)
        {
            if (__instance == null ||
                ___m_haveTarget ||
                !OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableTurretOptimization == null ||
                !ModConfig.EnableTurretOptimization.Value)
            {
                return true;
            }

            if (___m_nview == null || !___m_nview.IsValid())
            {
                return false;
            }

            // Never skip a tick when target scan is imminent
            if (___m_updateTargetTimer <= 0.12f)
            {
                return true;
            }

            if (!OptimizationHelpers.TryGetCameraView(out Vector3 camPos, out Vector3 camForward))
            {
                return true;
            }

            Vector3 pos = __instance.transform.position;
            float sqrDist = (pos - camPos).sqrMagnitude;
            bool farOrBehind = sqrDist > 784f || OptimizationHelpers.IsBehindCamera(pos, camPos, camForward, 144f);
            if (!farOrBehind)
            {
                return true;
            }

            // Throttle idle quaternion rotation 4x while advancing target scan timer accurately
            if (((Time.frameCount + (__instance.GetInstanceID() & 3)) & 3) != 0)
            {
                if (___m_nview.IsOwner())
                {
                    ___m_updateTargetTimer -= Time.fixedDeltaTime;
                }
                TurretOptimizer.IdleTicksSkipped++;
                return false;
            }

            return true;
        }
    }
}
