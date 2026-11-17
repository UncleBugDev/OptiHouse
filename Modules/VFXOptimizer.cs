using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;
using UnityEngine.Animations;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Particle & Visual Effect Optimizer for OptiHouse.
    /// - Suppresses heavy hoe/cultivator dust bursts and WearNTear demolition debris chunks.
    /// - Throttles distant (>30m) ambient sparks/dust/smoke bursts.
    /// - Accelerates temporary particle dissipation cleanly ONCE per spawned effect GameObject
    ///   (fixed triple-scaling bug). Never suppresses audio (sfx) or resource drops.
    /// </summary>
    public static class VFXOptimizer
    {
        private sealed class FadeAppliedMarker { }
        private static readonly ConditionalWeakTable<GameObject, FadeAppliedMarker> _fadedObjects = new();

        public static void ApplyFastParticleFade(GameObject? go)
        {
            if (go == null || !OptiHousePlugin.IsOptimizationActive) return;
            if (ModConfig.EnableFastParticleFade == null || !ModConfig.EnableFastParticleFade.Value) return;

            // Ensure fast fade scaling is applied strictly ONCE per effect instance
            if (_fadedObjects.TryGetValue(go, out _)) return;
            _fadedObjects.Add(go, new FadeAppliedMarker());

            float lifetimeMult = ModConfig.ParticleLifetimeMultiplier != null ? ModConfig.ParticleLifetimeMultiplier.Value : 0.45f;
            float maxLifetime = ModConfig.MaxParticleLifetimeSeconds != null ? ModConfig.MaxParticleLifetimeSeconds.Value : 2.5f;
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
            if (string.IsNullOrEmpty(prefabName)) return false;

            // Never suppress audio cues
            if (prefabName.StartsWith("sfx", StringComparison.OrdinalIgnoreCase)) return false;

            if (ModConfig.SuppressCultivationParticles != null &&
                ModConfig.SuppressCultivationParticles.Value &&
                IsCultivationOrHoeEffect(prefabName))
            {
                return true;
            }

            if (ModConfig.SuppressDestructionParticles != null &&
                ModConfig.SuppressDestructionParticles.Value &&
                IsDestructionOrDebrisEffect(prefabName))
            {
                return true;
            }

            if (ModConfig.VfxThrottlingDistance != null &&
                ModConfig.VfxThrottlingDistance.Value > 0f &&
                IsAmbientBuildingVfx(prefabName) &&
                OptimizationHelpers.TryGetReferencePosition(out Vector3 refPos))
            {
                float maxDist = ModConfig.VfxThrottlingDistance.Value;
                if ((effectPos - refPos).sqrMagnitude > maxDist * maxDist)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsCultivationOrHoeEffect(string name)
        {
            if (!OptimizationHelpers.ContainsIgnoreCase(name, "vfx")) return false;

            return OptimizationHelpers.ContainsIgnoreCase(name, "hoe") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "cultivat") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "terrain") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "level") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "smooth") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "raise") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "pave") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "flatten");
        }

        public static bool IsDestructionOrDebrisEffect(string name)
        {
            if (!OptimizationHelpers.ContainsIgnoreCase(name, "vfx")) return false;

            return OptimizationHelpers.ContainsIgnoreCase(name, "wood_break") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "wood_destroyed") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "stone_break") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "stone_destroyed") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "sawdust") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "iron_break") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "marble_break") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "ashstone_break") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "debris") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "rubble") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "wnt_destroyed");
        }

        public static bool IsAmbientBuildingVfx(string name)
        {
            if (!OptimizationHelpers.ContainsIgnoreCase(name, "vfx")) return false;

            return OptimizationHelpers.ContainsIgnoreCase(name, "spark") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "dust") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "smoke") ||
                   OptimizationHelpers.ContainsIgnoreCase(name, "ash");
        }
    }

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
                return true;
            }

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
                        gameObject.transform.localScale = baseParent.localScale * scale;
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

    [HarmonyPatch(typeof(Destructible), nameof(Destructible.CreateFragments))]
    public static class Destructible_CreateFragments_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(GameObject rootObject)
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
                ModConfig.VfxThrottlingDistance.Value > 0f &&
                OptimizationHelpers.TryGetReferencePosition(out Vector3 refPos))
            {
                float maxDist = ModConfig.VfxThrottlingDistance.Value;
                if ((rootObject.transform.position - refPos).sqrMagnitude > maxDist * maxDist)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
