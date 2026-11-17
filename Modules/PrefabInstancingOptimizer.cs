using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Standalone Engine Prefab & Audio Optimizer for OptiHouse v2.0.
    /// 1. GPU Instancing: Enables Material.enableInstancing across ZNetScene prefabs (pieces, trees, rocks, props)
    ///    so Unity batches identical meshes into single DrawMeshInstanced calls without custom shader glitches.
    /// 2. Native C++ Offscreen Particle Culling: Sets local-space looping ParticleSystems on prefabs to Pause
    ///    when outside camera frustum on native Unity worker threads.
    /// 3. Ambient ZSFX Loop Throttling: Throttles per-frame reverb/curve math on out-of-range looping sounds.
    /// </summary>
    public static class PrefabInstancingOptimizer
    {
        public static int InstancedMaterialsCount { get; private set; }
        public static int OffscreenParticleSystemsCount { get; private set; }
        public static long AudioUpdatesSkipped { get; internal set; }

        public static void OptimizeAllPrefabs(ZNetScene scene)
        {
            if (scene == null || scene.m_prefabs == null) return;

            bool enableInstancing = OptiHousePlugin.IsOptimizationActive &&
                                    ModConfig.EnableGpuInstancing != null &&
                                    ModConfig.EnableGpuInstancing.Value;

            bool enableParticleCull = OptiHousePlugin.IsOptimizationActive &&
                                      ModConfig.EnableOffscreenParticleCulling != null &&
                                      ModConfig.EnableOffscreenParticleCulling.Value;

            if (!enableInstancing && !enableParticleCull) return;

            HashSet<Material> visitedMaterials = new HashSet<Material>();
            int instancedCount = 0;
            int particleCount = 0;

            List<GameObject> prefabs = scene.m_prefabs;
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject prefab = prefabs[i];
                if (prefab == null) continue;

                if (enableInstancing)
                {
                    Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        Renderer ren = renderers[r];
                        if (ren == null || ren is SkinnedMeshRenderer) continue;

                        Material[] mats = ren.sharedMaterials;
                        if (mats == null) continue;

                        for (int m = 0; m < mats.Length; m++)
                        {
                            Material mat = mats[m];
                            if (mat == null || !visitedMaterials.Add(mat)) continue;

                            if (!mat.enableInstancing && mat.shader != null)
                            {
                                mat.enableInstancing = true;
                                instancedCount++;
                            }
                        }
                    }
                }

                if (enableParticleCull)
                {
                    ParticleSystem[] particles = prefab.GetComponentsInChildren<ParticleSystem>(true);
                    for (int p = 0; p < particles.Length; p++)
                    {
                        ParticleSystem ps = particles[p];
                        if (ps == null) continue;

                        var main = ps.main;
                        if (main.loop &&
                            main.simulationSpace == ParticleSystemSimulationSpace.Local &&
                            main.cullingMode == ParticleSystemCullingMode.Automatic)
                        {
                            main.cullingMode = ParticleSystemCullingMode.Pause;
                            particleCount++;
                        }
                    }
                }
            }

            InstancedMaterialsCount = instancedCount;
            OffscreenParticleSystemsCount = particleCount;

            if (ModConfig.EnableLogging != null && ModConfig.EnableLogging.Value)
            {
                OptiHousePlugin.Log.LogInfo(
                    $"[PrefabInstancingOptimizer] Enabled GPU Instancing on {instancedCount} shared materials and C++ offscreen culling on {particleCount} particle prefabs.");
            }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    public static class ZNetScene_Awake_PrefabInstancing_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ZNetScene __instance)
        {
            PrefabInstancingOptimizer.OptimizeAllPrefabs(__instance);
        }
    }

    [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.CustomUpdate))]
    public static class ZSFX_CustomUpdate_AudioCull_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ZSFX __instance, AudioSource ___m_audioSource)
        {
            if (__instance == null ||
                ___m_audioSource == null ||
                OptimizationHelpers.IsVpoInstalled ||
                !OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableAmbientAudioCulling == null ||
                !ModConfig.EnableAmbientAudioCulling.Value)
            {
                return true;
            }

            if (!___m_audioSource.loop || ___m_audioSource.spatialBlend < 0.5f)
            {
                return true;
            }

            if (!OptimizationHelpers.TryGetReferencePosition(out Vector3 refPos))
            {
                return true;
            }

            float maxDist = ___m_audioSource.maxDistance + 6.0f;
            if ((__instance.transform.position - refPos).sqrMagnitude <= maxDist * maxDist)
            {
                return true;
            }

            // Out of audible range: mute volume and throttle expensive curve/reverb updates by 8x
            if (((Time.frameCount + (__instance.GetInstanceID() & 7)) & 7) != 0)
            {
                if (___m_audioSource.volume > 0f)
                {
                    ___m_audioSource.volume = 0f;
                }
                PrefabInstancingOptimizer.AudioUpdatesSkipped++;
                return false;
            }

            return true;
        }
    }
}
