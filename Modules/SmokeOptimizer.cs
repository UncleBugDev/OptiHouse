using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Optimizes indoor base smoke by capping active smoke puffs, throttling
    /// spawner and physics raycasts, and optionally disabling smoke particle rendering
    /// while fully preserving heat, comfort, fire, and chimney mechanics.
    /// </summary>
    public static class SmokeOptimizer
    {
        public static int GetConfiguredSmokeCap()
        {
            if (ModConfig.MaxActiveSmokePuffs == null)
                return 25;

            return Mathf.Max(5, ModConfig.MaxActiveSmokePuffs.Value);
        }
    }

    /// <summary>
    /// Enforces the active smoke puff cap when spawning new smoke from fireplaces,
    /// braziers, and hearths.
    /// </summary>
    [HarmonyPatch(typeof(SmokeSpawner), "Spawn")]
    public static class SmokeSpawner_Spawn_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(SmokeSpawner __instance)
        {
            if (!OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableSmokeOptimization == null ||
                !ModConfig.EnableSmokeOptimization.Value)
            {
                return;
            }

            Player localPlayer = Player.m_localPlayer;
            if (localPlayer == null || Vector3.Distance(localPlayer.transform.position, __instance.transform.position) > 64f)
            {
                return;
            }

            int cap = SmokeOptimizer.GetConfiguredSmokeCap();
            // Cull oldest smoke puffs so total count never exceeds configured cap
            while (Smoke.GetTotalSmoke() >= cap && Smoke.GetTotalSmoke() > 0)
            {
                Smoke.FadeOldest();
            }
        }
    }

    /// <summary>
    /// Throttles SmokeSpawner.TestBlocked sphere checks to reduce heavy PhysX queries
    /// in bases with numerous fire sources.
    /// </summary>
    [HarmonyPatch(typeof(SmokeSpawner), "TestBlocked")]
    public static class SmokeSpawner_TestBlocked_Patch
    {
        private class CacheEntry
        {
            public float LastCheckTime;
            public bool IsBlocked;
        }

        private static readonly ConditionalWeakTable<SmokeSpawner, CacheEntry> _cache = new();

        [HarmonyPrefix]
        public static bool Prefix(SmokeSpawner __instance, ref bool __result)
        {
            if (!OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableSmokeOptimization == null ||
                !ModConfig.EnableSmokeOptimization.Value ||
                ModConfig.ThrottleSmokeRaycasts == null ||
                !ModConfig.ThrottleSmokeRaycasts.Value)
            {
                return true;
            }

            float now = Time.time;
            var entry = _cache.GetOrCreateValue(__instance);

            // Throttle to 1 check per second per spawner
            if (now - entry.LastCheckTime < 1.0f)
            {
                __result = entry.IsBlocked;
                return false;
            }

            // Run actual sphere check and update cache
            bool blocked = Physics.CheckSphere(__instance.transform.position, __instance.m_testRadius, __instance.m_testMask.value);
            entry.LastCheckTime = now;
            entry.IsBlocked = blocked;
            __result = blocked;

            return false;
        }
    }

    /// <summary>
    /// Throttles smoke physics calculations when the puff is already fading out.
    /// </summary>
    [HarmonyPatch(typeof(Smoke), nameof(Smoke.CustomUpdate))]
    public static class Smoke_CustomUpdate_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            Smoke __instance,
            float deltaTime,
            float time,
            ref float ___m_time,
            ref float ___m_fadeTimer,
            Rigidbody ___m_body,
            Vector3 ___m_vel,
            float ___m_force,
            float ___m_ttl,
            float ___m_fadetime)
        {
            if (!OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableSmokeOptimization == null ||
                !ModConfig.EnableSmokeOptimization.Value ||
                ModConfig.ThrottleSmokeRaycasts == null ||
                !ModConfig.ThrottleSmokeRaycasts.Value)
            {
                return true;
            }

            ___m_time += deltaTime;
            if (___m_time > ___m_ttl && ___m_fadeTimer < 0f)
            {
                __instance.StartFadeOut();
            }

            // Only perform force calculations on actively rising smoke
            if (___m_fadeTimer < 0f)
            {
                float num = 1f - Mathf.Clamp01(___m_time / ___m_ttl);
                ___m_body.mass = num * num;
                Vector3 linearVelocity = ___m_body.linearVelocity;
                Vector3 vel = ___m_vel;
                vel.y *= num;
                Vector3 vector = vel - linearVelocity;
                ___m_body.AddForce(vector * (___m_force * deltaTime), ForceMode.VelocityChange);
            }
            else
            {
                // Fading puff drifts naturally without continuous AddForce calls
                ___m_fadeTimer += deltaTime;
                if (___m_fadeTimer >= ___m_fadetime)
                {
                    UnityEngine.Object.Destroy(__instance.gameObject);
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Option to disable visual particle rendering for smoke in SmokeRenderer,
    /// while keeping smoke GameObjects, colliders, heat, and comfort intact.
    /// </summary>
    [HarmonyPatch(typeof(SmokeRenderer), "LateUpdate")]
    public static class SmokeRenderer_LateUpdate_Patch
    {
        private static bool _wasDisabled = false;

        [HarmonyPrefix]
        public static bool Prefix(Dictionary<Vector3Int, ParticleSystem> ___m_chunkedParticleSystems)
        {
            if (!OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableSmokeOptimization == null ||
                !ModConfig.EnableSmokeOptimization.Value)
            {
                return true;
            }

            if (ModConfig.DisableSmokeRendering != null && ModConfig.DisableSmokeRendering.Value)
            {
                if (!_wasDisabled)
                {
                    if (___m_chunkedParticleSystems != null)
                    {
                        foreach (var ps in ___m_chunkedParticleSystems.Values)
                        {
                            if (ps != null && ps.particleCount > 0)
                            {
                                ps.Clear();
                            }
                        }
                    }
                    _wasDisabled = true;
                }
                return false;
            }

            _wasDisabled = false;
            return true;
        }
    }

    /// <summary>
    /// Harmony patch on Smoke.Awake to accelerate smoke puff dissipation.
    /// Smoke particles dissolve quickly instead of lingering indefinitely under ceilings.
    /// </summary>
    [HarmonyPatch(typeof(Smoke), "Awake")]
    public static class Smoke_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Smoke __instance)
        {
            if (!OptiHousePlugin.IsOptimizationActive)
                return;

            if (ModConfig.EnableFastParticleFade != null && ModConfig.EnableFastParticleFade.Value &&
                ModConfig.SmokeLifetimeMultiplier != null)
            {
                float mult = ModConfig.SmokeLifetimeMultiplier.Value;
                __instance.m_ttl = Mathf.Max(2.5f, __instance.m_ttl * mult);
                __instance.m_fadetime = Mathf.Max(0.8f, __instance.m_fadetime * mult);
            }
        }
    }
}
