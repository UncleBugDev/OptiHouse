using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Optimizes Valheim's MonoUpdaters ZSyncTransform hot loops and HUD comfort localization:
    /// 1. OwnerSync (CustomLateUpdate, every frame): Throttles per-frame C++ Rigidbody/Transform marshaling
    ///    by 16x on physically sleeping (Rigidbody.IsSleeping()) non-character, non-projectile world objects
    ///    (ground loot, logs, moored ships, parked carts) while running immediately the instant PhysX wakes them.
    /// 2. ClientSync (CustomFixedUpdate, 50 Hz multiplayer): Throttles redundant ZDO dictionary lookups by 4x
    ///    on distant (&gt;35m) un-updated (DataRevision unchanged) non-character objects.
    /// 3. SE_Cozy.GetIconText(): Eliminates per-frame string concatenation + Localization.Localize() regex GC
    ///    allocations while resting near base fires.
    /// </summary>
    public static class ZSyncTransformOptimizer
    {
        public static long SleepingSyncsSkipped { get; internal set; }
        public static long DistantClientSyncsSkipped { get; internal set; }

        internal static int CachedComfortLevel = int.MinValue;
        internal static string CachedComfortText = string.Empty;
    }

    [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.CustomLateUpdate))]
    public static class ZSyncTransform_CustomLateUpdate_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            ZSyncTransform __instance,
            Rigidbody ___m_body,
            Character ___m_character,
            Projectile ___m_projectile,
            bool ___m_isKinematicBody,
            bool ___m_characterParentSync,
            bool ___m_wasOwner)
        {
            if (__instance == null ||
                ___m_body == null ||
                ___m_character != null ||
                ___m_projectile != null ||
                ___m_isKinematicBody ||
                ___m_characterParentSync ||
                !___m_wasOwner ||
                !OptiHousePlugin.IsOptimizationActive ||
                ModConfig.OptimizeZSyncTransform == null ||
                !ModConfig.OptimizeZSyncTransform.Value)
            {
                return true;
            }

            if (!___m_body.IsSleeping())
            {
                return true;
            }

            //Physically asleep Rigidbody: throttle C++ position/velocity/gravity sync checks by 16x
            if (((Time.frameCount + (__instance.GetInstanceID() & 15)) & 15) != 0)
            {
                ZSyncTransformOptimizer.SleepingSyncsSkipped++;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.CustomFixedUpdate))]
    public static class ZSyncTransform_CustomFixedUpdate_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            ZSyncTransform __instance,
            ZNetView ___m_nview,
            Character ___m_character,
            Projectile ___m_projectile,
            bool ___m_isKinematicBody,
            bool ___m_characterParentSync,
            uint ___m_posRevision)
        {
            if (__instance == null ||
                ___m_nview == null ||
                ___m_character != null ||
                ___m_projectile != null ||
                ___m_isKinematicBody ||
                ___m_characterParentSync ||
                !OptiHousePlugin.IsOptimizationActive ||
                ModConfig.OptimizeZSyncTransform == null ||
                !ModConfig.OptimizeZSyncTransform.Value)
            {
                return true;
            }

            ZDO zdo = ___m_nview.GetZDO();
            if (zdo == null || zdo.IsOwner() || zdo.DataRevision != ___m_posRevision)
            {
                return true;
            }

            if (!OptimizationHelpers.TryGetReferencePosition(out Vector3 refPos))
            {
                return true;
            }

            // Distant (>35m) multiplayer object with unchanged ZDO revision: throttle 50 Hz ZDO lookups by 4x
            if ((__instance.transform.position - refPos).sqrMagnitude > 1225f)
            {
                int tick = (int)(Time.fixedUnscaledTime * 50f);
                if (((tick + (__instance.GetInstanceID() & 3)) & 3) != 0)
                {
                    ZSyncTransformOptimizer.DistantClientSyncsSkipped++;
                    return false;
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(SE_Cozy), nameof(SE_Cozy.GetIconText))]
    public static class SE_Cozy_GetIconText_ZeroGC_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Character ___m_character, ref string __result)
        {
            if (!OptiHousePlugin.IsOptimizationActive ||
                ___m_character is not Player player ||
                Localization.instance == null)
            {
                return true;
            }

            int comfort = player.GetComfortLevel();
            if (comfort != ZSyncTransformOptimizer.CachedComfortLevel ||
                string.IsNullOrEmpty(ZSyncTransformOptimizer.CachedComfortText))
            {
                ZSyncTransformOptimizer.CachedComfortLevel = comfort;
                ZSyncTransformOptimizer.CachedComfortText =
                    Localization.instance.Localize("$se_rested_comfort:" + comfort);
            }

            __result = ZSyncTransformOptimizer.CachedComfortText;
            return false;
        }
    }
}
