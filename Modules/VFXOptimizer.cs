using System;
using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;
using UnityEngine.Animations;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Optimizes and suppresses heavy particle bursts, terraforming VFX, and destruction debris.
    /// Accelerates particle dissipation so particles vanish rapidly instead of lingering on screen.
    /// Safely preserves sound effects (sfx), item drops, and game logic.
    /// </summary>
    public static class VFXOptimizer
    {
        /// <summary>
        /// Applies accelerated particle dissipation to any visual effect GameObject.
        /// Scales down startLifetimeMultiplier and scales up simulationSpeed so particles
        /// pop, disperse, and vanish quickly. Also shortens TimedDestruction timeout.
        /// </summary>
        public static void ApplyFastParticleFade(GameObject? go)
        {
            if (go == null || !OptiHousePlugin.IsOptimizationActive)
                return;

            if (ModConfig.EnableFastParticleFade == null || !ModConfig.EnableFastParticleFade.Value)
                return;

            float lifetimeMult = ModConfig.ParticleLifetimeMultiplier != null ? ModConfig.ParticleLifetimeMultiplier.Value : 0.35f;
            float maxLifetime = ModConfig.MaxParticleLifetimeSeconds != null ? ModConfig.MaxParticleLifetimeSeconds.Value : 2.0f;
            float speedMult = ModConfig.ParticleSimulationSpeed != null ? ModConfig.ParticleSimulationSpeed.Value : 1.5f;

            ParticleSystem[] particleSystems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem ps = particleSystems[i];
                if (ps == null) continue;

                var main = ps.main;
                main.startLifetimeMultiplier = Mathf.Min(main.startLifetimeMultiplier * lifetimeMult, maxLifetime);
                main.simulationSpeed *= speedMult;
            }

            TimedDestruction td = go.GetComponent<TimedDestruction>();
            if (td != null && td.m_timeout > 0f)
            {
                td.m_timeout = Mathf.Clamp(td.m_timeout * lifetimeMult, 0.4f, maxLifetime);
            }
        }

        public static bool ShouldSuppressEffect(string prefabName, Vector3 effectPos)
        {
            if (string.IsNullOrEmpty(prefabName))
                return false;

            // Never suppress audio cues
            if (prefabName.StartsWith("sfx", StringComparison.OrdinalIgnoreCase))
                return false;

            // 1. Cultivation, hoeing, terraforming particles
            if (ModConfig.SuppressCultivationParticles != null &&
                ModConfig.SuppressCultivationParticles.Value &&
                IsCultivationOrHoeEffect(prefabName))
            {
                return true;
            }

            // 2. Piece destruction, demolition, debris particles
            if (ModConfig.SuppressDestructionParticles != null &&
                ModConfig.SuppressDestructionParticles.Value &&
                IsDestructionOrDebrisEffect(prefabName))
            {
                return true;
            }

            // 3. Distance throttling for ambient building VFX
            if (ModConfig.VfxThrottlingDistance != null &&
                ModConfig.VfxThrottlingDistance.Value > 0f)
            {
                Camera cam = Utils.GetMainCamera();
                if (cam != null)
                {
                    float distSq = (effectPos - cam.transform.position).sqrMagnitude;
                    float maxDist = ModConfig.VfxThrottlingDistance.Value;
                    if (distSq > maxDist * maxDist && IsAmbientBuildingVfx(prefabName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool IsCultivationOrHoeEffect(string name)
        {
            if (!name.Contains("vfx", StringComparison.OrdinalIgnoreCase))
                return false;

            return name.Contains("hoe", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("cultivat", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("terrain", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("dirt", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("level", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("smooth", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("raise", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("pave", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("grass", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("flatten", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDestructionOrDebrisEffect(string name)
        {
            if (!name.Contains("vfx", StringComparison.OrdinalIgnoreCase))
                return false;

            return name.Contains("wood_break", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("wood_destroyed", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("stone_break", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("stone_destroyed", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("sawdust", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("rock_destroy", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("iron_break", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("marble_break", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("ashstone_break", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("debris", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("fragment", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("fracture", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("rubble", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("wnt_destroyed", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAmbientBuildingVfx(string name)
        {
            if (!name.Contains("vfx", StringComparison.OrdinalIgnoreCase))
                return false;

            return name.Contains("spark", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("dust", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("smoke", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("ash", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("break", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("hit", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Harmony patch on EffectList.Create to filter out unwanted particle bursts
    /// while leaving sound effects and valid visual effects intact.
    /// </summary>
    [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
    public static class EffectList_Create_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            EffectList __instance,
            Vector3 basePos,
            Quaternion baseRot,
            Transform? baseParent,
            float scale,
            int variant,
            ZDOID gamepadEffectsExclusiveToPlayer,
            ref GameObject[] __result)
        {
            if (!OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableVfxOptimization == null ||
                !ModConfig.EnableVfxOptimization.Value)
            {
                return true;
            }

            if (__instance?.m_effectPrefabs == null || __instance.m_effectPrefabs.Length == 0)
            {
                return true;
            }

            // Quick check: do any effects in this list need suppression?
            bool hasSuppressed = false;
            for (int i = 0; i < __instance.m_effectPrefabs.Length; i++)
            {
                var effect = __instance.m_effectPrefabs[i];
                if (effect?.m_prefab != null && VFXOptimizer.ShouldSuppressEffect(effect.m_prefab.name, basePos))
                {
                    hasSuppressed = true;
                    break;
                }
            }

            if (!hasSuppressed)
            {
                return true; // Let vanilla run untouched
            }

            // Selective instantiation: create only non-suppressed effects (e.g. sfx audio)
            List<GameObject> list = new List<GameObject>();
            for (int i = 0; i < __instance.m_effectPrefabs.Length; i++)
            {
                EffectList.EffectData effectData = __instance.m_effectPrefabs[i];
                if (effectData == null || !effectData.m_enabled || effectData.m_prefab == null)
                {
                    continue;
                }

                if (variant >= 0 && effectData.m_variant >= 0 && variant != effectData.m_variant)
                {
                    continue;
                }

                if (VFXOptimizer.ShouldSuppressEffect(effectData.m_prefab.name, basePos))
                {
                    continue;
                }

                Transform? transform = baseParent;
                Vector3 position = basePos;
                Quaternion rotation = baseRot;
                if (!string.IsNullOrEmpty(effectData.m_childTransform) && baseParent != null)
                {
                    Transform? transform2 = Utils.FindChild(transform, effectData.m_childTransform);
                    if (transform2 != null)
                    {
                        transform = transform2;
                        position = transform.position;
                    }
                }
                if (transform != null && effectData.m_inheritParentRotation)
                {
                    rotation = transform.rotation;
                }
                if (effectData.m_randomRotation)
                {
                    rotation = UnityEngine.Random.rotation;
                }
                GameObject gameObject = UnityEngine.Object.Instantiate(effectData.m_prefab, position, rotation);
                if (gamepadEffectsExclusiveToPlayer != ZDOID.None)
                {
                    ZNetView component = gameObject.GetComponent<ZNetView>();
                    if (component != null && component.IsValid() && component.IsOwner())
                    {
                        component.GetZDO().Set(ZDOVars.s_ownerZDOUserId, gamepadEffectsExclusiveToPlayer.UserID);
                        component.GetZDO().Set(ZDOVars.s_ownerZDOId, gamepadEffectsExclusiveToPlayer.ID);
                    }
                }
                if (effectData.m_scale)
                {
                    if (baseParent != null && effectData.m_inheritParentScale)
                    {
                        Vector3 localScale = baseParent.localScale * scale;
                        gameObject.transform.localScale = localScale;
                    }
                    else
                    {
                        gameObject.transform.localScale = new Vector3(scale, scale, scale);
                    }
                }
                else if (baseParent != null)
                {
                    if (effectData.m_multiplyParentVisualScale)
                    {
                        Transform transform3 = baseParent.Find("Visual");
                        if (transform3 != null)
                        {
                            gameObject.transform.localScale = Vector3.Scale(gameObject.transform.localScale, transform3.localScale);
                        }
                    }
                    else if (effectData.m_inheritParentScale)
                    {
                        gameObject.transform.localScale = baseParent.localScale;
                    }
                }

                if (effectData.m_attach && transform != null)
                {
                    gameObject.transform.SetParent(transform);
                }
                if (effectData.m_follow && transform != null)
                {
                    ParentConstraint parentConstraint = gameObject.AddComponent<ParentConstraint>();
                    ConstraintSource source = new ConstraintSource
                    {
                        sourceTransform = transform,
                        weight = 1f
                    };
                    parentConstraint.AddSource(source);
                    parentConstraint.locked = true;
                    parentConstraint.SetTranslationOffset(0, effectData.m_prefab.transform.position);
                    parentConstraint.SetRotationOffset(0, effectData.m_prefab.transform.rotation.eulerAngles);
                    parentConstraint.constraintActive = true;
                }

                // Apply fast particle dissipation
                VFXOptimizer.ApplyFastParticleFade(gameObject);

                list.Add(gameObject);
            }

            __result = list.ToArray();
            return false;
        }

        [HarmonyPostfix]
        public static void Postfix(ref GameObject[]? __result)
        {
            if (__result == null || __result.Length == 0) return;
            for (int i = 0; i < __result.Length; i++)
            {
                if (__result[i] != null)
                {
                    VFXOptimizer.ApplyFastParticleFade(__result[i]);
                }
            }
        }
    }

    /// <summary>
    /// Harmony patch on TimedDestruction.Awake to accelerate the dissipation of all temporary
    /// visual effects in Valheim (combat impacts, ground dust, building debris, sparks).
    /// </summary>
    [HarmonyPatch(typeof(TimedDestruction), "Awake")]
    public static class TimedDestruction_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(TimedDestruction __instance)
        {
            if (__instance != null)
            {
                VFXOptimizer.ApplyFastParticleFade(__instance.gameObject);
            }
        }
    }

    /// <summary>
    /// Harmony patch on Destructible.CreateFragments to prevent massive physics chunks
    /// from lagging the game when building pieces or structures are destroyed.
    /// Item drops and resource refunds are completely unaffected.
    /// </summary>
    [HarmonyPatch(typeof(Destructible), nameof(Destructible.CreateFragments))]
    public static class Destructible_CreateFragments_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(GameObject rootObject, bool visibleOnly)
        {
            if (!OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableVfxOptimization == null ||
                !ModConfig.EnableVfxOptimization.Value)
            {
                return true;
            }

            if (ModConfig.SuppressDestructionParticles != null &&
                ModConfig.SuppressDestructionParticles.Value)
            {
                return false;
            }

            if (rootObject != null &&
                ModConfig.VfxThrottlingDistance != null &&
                ModConfig.VfxThrottlingDistance.Value > 0f)
            {
                Camera cam = Utils.GetMainCamera();
                if (cam != null)
                {
                    float distSq = (rootObject.transform.position - cam.transform.position).sqrMagnitude;
                    float maxDist = ModConfig.VfxThrottlingDistance.Value;
                    if (distSq > maxDist * maxDist)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
