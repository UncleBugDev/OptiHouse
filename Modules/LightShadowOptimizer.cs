using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Dynamic Light, Shadow & Flicker Optimizer for OptiHouse.
    /// - Caps active Forward-rendered point lights via Valheim's native LightLod.m_lightLimit,
    ///   eliminating multi-pass ForwardAdd draw call explosions in torch-lit mega-bases.
    /// - Caps real-time shadow-casting point lights via LightLod.m_shadowLimit + strips torch shadows.
    /// - Throttles periodic Fireplace.CheckEnv physics/terrain raycasts from 4s to 20s.
    /// - Skips CPU LightFlicker trigonometry on distant (>22m) permanent lights (deferring cleanly to VPO if installed).
    /// - Fully reversible on runtime toggle (F7) or unload.
    /// </summary>
    public class LightShadowOptimizer : MonoBehaviour
    {
        public static LightShadowOptimizer? Instance { get; private set; }

        private struct LightDistanceEntry
        {
            public Light Light;
            public float SqrDistance;
        }

        private class LightLodBackup
        {
            public float OrigLightDistance;
            public bool OrigShadowLod;
            public float OrigShadowDistance;
            public bool Modified;
        }

        private class FireplaceEnvCache
        {
            public float LastCheckTime = -999f;
            public Vector3 LastPos;
        }

        private static readonly HashSet<Light> _registeredLights = new HashSet<Light>();
        private static readonly List<LightDistanceEntry> _lightSortBuffer = new List<LightDistanceEntry>();
        private static readonly Dictionary<Light, LightShadows> _originalShadowModes = new Dictionary<Light, LightShadows>();
        private static readonly Dictionary<Light, bool> _originalEnabledStates = new Dictionary<Light, bool>();
        private static readonly ConditionalWeakTable<LightLod, LightLodBackup> _lodBackups = new ConditionalWeakTable<LightLod, LightLodBackup>();
        private static readonly ConditionalWeakTable<Fireplace, FireplaceEnvCache> _envCaches = new ConditionalWeakTable<Fireplace, FireplaceEnvCache>();

        private static readonly FieldInfo? _lightLodLightsField =
            typeof(LightLod).GetField("m_lights", BindingFlags.NonPublic | BindingFlags.Static);

        public static long EnvChecksThrottled { get; private set; }
        public static long FlickersSkipped { get; internal set; }

        public static int TotalLights
        {
            get
            {
                int lodCount = (_lightLodLightsField?.GetValue(null) as HashSet<LightLod>)?.Count ?? 0;
                return lodCount + _registeredLights.Count;
            }
        }

        public static int ActivePointLights
        {
            get
            {
                int total = TotalLights;
                if (!IsActive())
                {
                    return total;
                }

                int maxLights = ModConfig.MaxActivePointLights != null ? ModConfig.MaxActivePointLights.Value : 28;
                return maxLights > 0 ? Mathf.Min(maxLights, total) : total;
            }
        }

        public static int ActiveShadowLights
        {
            get
            {
                int total = TotalLights;
                if (!IsActive())
                {
                    return total;
                }

                int maxShadows = ModConfig.MaxShadowCastingLights != null ? ModConfig.MaxShadowCastingLights.Value : 6;
                return Mathf.Min(maxShadows, total);
            }
        }

        public static int CulledShadowLights => Mathf.Max(0, TotalLights - ActiveShadowLights);
        public static int CulledPointLights => Mathf.Max(0, TotalLights - ActivePointLights);

        private static int _savedShadowCascades = -1;
        private static bool _shadowCascadesReduced;
        public static bool IsMegaBaseShadowCascadeActive => _shadowCascadesReduced;

        private float _updateTimer;
        private bool _wasOptimizationActive = true;

        private static bool IsActive()
        {
            return OptiHousePlugin.IsOptimizationActive &&
                   ModConfig.EnableLightOptimization != null &&
                   ModConfig.EnableLightOptimization.Value;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            RestoreAllLights();
            _registeredLights.Clear();
            _originalShadowModes.Clear();
            _originalEnabledStates.Clear();
            _lightSortBuffer.Clear();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!IsActive())
            {
                if (_wasOptimizationActive)
                {
                    _wasOptimizationActive = false;
                    RestoreAllLights();
                }
                return;
            }

            if (!_wasOptimizationActive)
            {
                _wasOptimizationActive = true;
                ReapplyExistingLightLods();
            }

            _updateTimer += Time.deltaTime;
            if (_updateTimer < 1.0f)
            {
                return;
            }
            _updateTimer = 0f;

            ManageAdaptiveShadowCascades();
            ManageScenePointLights();
        }

        private static void ManageAdaptiveShadowCascades()
        {
            bool allowAdaptive = ModConfig.EnableAdaptiveShadowCascades != null &&
                                 ModConfig.EnableAdaptiveShadowCascades.Value;

            if (!allowAdaptive)
            {
                RestoreShadowCascades();
                return;
            }

            int threshold = ModConfig.MegaBaseCascadeThreshold != null
                ? Mathf.Max(500, ModConfig.MegaBaseCascadeThreshold.Value)
                : 2500;

            int pieceCount = OptimizationHelpers.GetTotalPieceCount();
            if (!_shadowCascadesReduced)
            {
                if (pieceCount >= threshold && QualitySettings.shadowCascades > 2)
                {
                    _savedShadowCascades = QualitySettings.shadowCascades;
                    QualitySettings.shadowCascades = 2;
                    _shadowCascadesReduced = true;
                }
            }
            else
            {
                int restoreThreshold = Mathf.Max(200, threshold - 400);
                if (pieceCount < restoreThreshold)
                {
                    RestoreShadowCascades();
                }
            }
        }

        private static void RestoreShadowCascades()
        {
            if (_shadowCascadesReduced)
            {
                if (_savedShadowCascades > 0)
                {
                    QualitySettings.shadowCascades = _savedShadowCascades;
                }
                _shadowCascadesReduced = false;
                _savedShadowCascades = -1;
            }
        }

        /// <summary>
        /// Registers a non-LightLod point light (e.g. from custom or modded sconces) for distance & shadow capping.
        /// </summary>
        public static void RegisterLight(Light light)
        {
            if (light == null || light.type != LightType.Point) return;
            if (light.GetComponent<LightLod>() != null) return;
            if (!OptimizationHelpers.IsValidWorldObject(light)) return;

            if (_registeredLights.Add(light))
            {
                if (!_originalShadowModes.ContainsKey(light))
                {
                    _originalShadowModes[light] = light.shadows;
                }
                if (!_originalEnabledStates.ContainsKey(light))
                {
                    _originalEnabledStates[light] = light.enabled;
                }
            }
        }

        public static void UnregisterLight(Light light)
        {
            if (light == null) return;
            _registeredLights.Remove(light);
            _originalShadowModes.Remove(light);
            _originalEnabledStates.Remove(light);
        }

        private static void ManageScenePointLights()
        {
            if (_registeredLights.Count == 0) return;

            Vector3 refPos = OptimizationHelpers.GetReferencePosition();
            int maxShadowLights = ModConfig.MaxShadowCastingLights != null ? ModConfig.MaxShadowCastingLights.Value : 6;
            float cullDist = ModConfig.LightCullDistance != null ? ModConfig.LightCullDistance.Value : 45f;
            float cullDistSq = cullDist * cullDist;

            _lightSortBuffer.Clear();
            _registeredLights.RemoveWhere(l => l == null);

            foreach (Light light in _registeredLights)
            {
                if (light == null || !light.gameObject.activeInHierarchy) continue;
                float sqrDist = (light.transform.position - refPos).sqrMagnitude;
                _lightSortBuffer.Add(new LightDistanceEntry { Light = light, SqrDistance = sqrDist });
            }

            if (_lightSortBuffer.Count == 0) return;

            _lightSortBuffer.Sort((a, b) => a.SqrDistance.CompareTo(b.SqrDistance));

            int maxActiveLights = ModConfig.MaxActivePointLights != null && ModConfig.MaxActivePointLights.Value > 0
                ? ModConfig.MaxActivePointLights.Value
                : int.MaxValue;

            for (int i = 0; i < _lightSortBuffer.Count; i++)
            {
                Light light = _lightSortBuffer[i].Light;
                if (light == null) continue;

                bool withinCull = _lightSortBuffer[i].SqrDistance <= cullDistSq && i < maxActiveLights;
                if (_originalEnabledStates.TryGetValue(light, out bool origEnabled))
                {
                    light.enabled = origEnabled && withinCull;
                }

                if (!withinCull || i >= maxShadowLights ||
                    (ModConfig.DisableTorchShadows != null && ModConfig.DisableTorchShadows.Value && IsSmallTorchOrSconce(light.gameObject)))
                {
                    if (light.shadows != LightShadows.None)
                    {
                        light.shadows = LightShadows.None;
                    }
                }
                else if (_originalShadowModes.TryGetValue(light, out LightShadows origShadow) && origShadow != LightShadows.None)
                {
                    light.shadows = origShadow;
                }
            }
        }

        private static void ReapplyExistingLightLods()
        {
            var lods = _lightLodLightsField?.GetValue(null) as HashSet<LightLod>;
            if (lods == null) return;

            foreach (LightLod lod in lods)
            {
                if (lod != null)
                {
                    ApplyToLightLod(lod);
                }
            }
        }

        public static void RestoreAllLights()
        {
            RestoreShadowCascades();
            LightLod.m_shadowLimit = -1;
            LightLod.m_lightLimit = -1;

            var lods = _lightLodLightsField?.GetValue(null) as HashSet<LightLod>;
            if (lods != null)
            {
                foreach (LightLod lod in lods)
                {
                    if (lod != null && _lodBackups.TryGetValue(lod, out LightLodBackup backup) && backup.Modified)
                    {
                        lod.m_lightDistance = backup.OrigLightDistance;
                        lod.m_shadowLod = backup.OrigShadowLod;
                        lod.m_shadowDistance = backup.OrigShadowDistance;
                        backup.Modified = false;
                    }
                }
            }

            foreach (var kvp in _originalShadowModes)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.shadows = kvp.Value;
                }
            }
            foreach (var kvp in _originalEnabledStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                }
            }
        }

        public static bool IsSmallTorchOrSconce(GameObject go)
        {
            if (go == null) return false;

            string name = go.name;
            if (OptimizationHelpers.ContainsIgnoreCase(name, "torch") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "sconce") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "candle") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "lantern") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "brazier") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "jackoturnip"))
            {
                return true;
            }

            Piece piece = go.GetComponentInParent<Piece>();
            if (piece != null && !string.IsNullOrEmpty(piece.m_name))
            {
                string pieceName = piece.m_name;
                if (OptimizationHelpers.ContainsIgnoreCase(pieceName, "torch") ||
                    OptimizationHelpers.ContainsIgnoreCase(pieceName, "sconce") ||
                    OptimizationHelpers.ContainsIgnoreCase(pieceName, "candle") ||
                    OptimizationHelpers.ContainsIgnoreCase(pieceName, "lantern") ||
                    OptimizationHelpers.ContainsIgnoreCase(pieceName, "brazier"))
                {
                    return true;
                }
            }

            return false;
        }

        public static void ApplyToLightLod(LightLod lod)
        {
            if (lod == null || !OptimizationHelpers.IsValidWorldObject(lod)) return;

            var backup = _lodBackups.GetOrCreateValue(lod);
            if (!backup.Modified)
            {
                backup.OrigLightDistance = lod.m_lightDistance;
                backup.OrigShadowLod = lod.m_shadowLod;
                backup.OrigShadowDistance = lod.m_shadowDistance;
                backup.Modified = true;
            }

            if (ModConfig.DisableTorchShadows != null &&
                ModConfig.DisableTorchShadows.Value &&
                IsSmallTorchOrSconce(lod.gameObject))
            {
                lod.m_shadowLod = false;
                lod.m_shadowDistance = 0f;

                Light light = lod.GetComponent<Light>();
                if (light != null)
                {
                    light.shadows = LightShadows.None;
                    light.shadowStrength = 0f;
                }
            }

            if (ModConfig.LightCullDistance != null && ModConfig.LightCullDistance.Value > 0f)
            {
                lod.m_lightDistance = Mathf.Min(backup.OrigLightDistance, ModConfig.LightCullDistance.Value);
            }
        }

        public static void ProcessFireplaceLights(Fireplace fp)
        {
            if (fp == null || !OptimizationHelpers.IsValidWorldObject(fp)) return;

            bool stripTorchShadows = ModConfig.DisableTorchShadows != null &&
                                     ModConfig.DisableTorchShadows.Value &&
                                     IsSmallTorchOrSconce(fp.gameObject);

            Light[] lights = fp.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light l = lights[i];
                if (l == null) continue;

                LightLod lod = l.GetComponent<LightLod>();
                if (lod != null)
                {
                    ApplyToLightLod(lod);
                }
                else
                {
                    RegisterLight(l);
                    if (stripTorchShadows)
                    {
                        l.shadows = LightShadows.None;
                        l.shadowStrength = 0f;
                    }
                }
            }
        }

        public static bool ShouldRunFireplaceEnvCheck(Fireplace fp, bool isBlocked)
        {
            if (!IsActive() ||
                ModConfig.ThrottleFireplaceEnvChecks == null ||
                !ModConfig.ThrottleFireplaceEnvChecks.Value)
            {
                return true;
            }

            if (fp == null || !OptimizationHelpers.IsValidWorldObject(fp))
            {
                return true;
            }

            // If currently blocked, let vanilla re-check at normal 4s rate so unblocking responds fast
            if (isBlocked)
            {
                return true;
            }

            var cache = _envCaches.GetOrCreateValue(fp);
            Vector3 pos = fp.transform.position;
            float now = Time.time;

            if (cache.LastCheckTime < 0f || (pos - cache.LastPos).sqrMagnitude > 0.05f || now - cache.LastCheckTime >= 20.0f)
            {
                cache.LastCheckTime = now;
                cache.LastPos = pos;
                return true;
            }

            EnvChecksThrottled++;
            return false;
        }
    }

    #region Harmony Patches

    [HarmonyPatch(typeof(LightLod), nameof(LightLod.UpdateLights))]
    public static class LightLod_UpdateLights_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (OptiHousePlugin.IsOptimizationActive &&
                ModConfig.EnableLightOptimization != null &&
                ModConfig.EnableLightOptimization.Value)
            {
                LightLod.m_shadowLimit = ModConfig.MaxShadowCastingLights != null
                    ? ModConfig.MaxShadowCastingLights.Value
                    : 6;

                int maxLights = ModConfig.MaxActivePointLights != null
                    ? ModConfig.MaxActivePointLights.Value
                    : 28;
                LightLod.m_lightLimit = maxLights > 0 ? maxLights : -1;
            }
            else
            {
                LightLod.m_shadowLimit = -1;
                LightLod.m_lightLimit = -1;
            }
        }
    }

    [HarmonyPatch(typeof(LightLod), "Awake")]
    public static class LightLod_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(LightLod __instance)
        {
            if (__instance == null) return;
            if (!OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableLightOptimization == null ||
                !ModConfig.EnableLightOptimization.Value)
            {
                return;
            }

            LightShadowOptimizer.ApplyToLightLod(__instance);
        }
    }

    [HarmonyPatch(typeof(Fireplace), "Start")]
    public static class Fireplace_Start_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Fireplace __instance)
        {
            if (__instance == null) return;
            if (!OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableLightOptimization == null ||
                !ModConfig.EnableLightOptimization.Value)
            {
                return;
            }

            LightShadowOptimizer.ProcessFireplaceLights(__instance);
        }
    }

    [HarmonyPatch(typeof(Fireplace), "UpdateFireplace")]
    public static class Fireplace_UpdateFireplace_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Fireplace __instance)
        {
            if (__instance == null) return;
            if (!OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableLightOptimization == null ||
                !ModConfig.EnableLightOptimization.Value ||
                ModConfig.DisableTorchShadows == null ||
                !ModConfig.DisableTorchShadows.Value)
            {
                return;
            }

            LightShadowOptimizer.ProcessFireplaceLights(__instance);
        }
    }

    [HarmonyPatch(typeof(Fireplace), "CheckEnv")]
    public static class Fireplace_CheckEnv_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Fireplace __instance, bool ___m_blocked)
        {
            return LightShadowOptimizer.ShouldRunFireplaceEnvCheck(__instance, ___m_blocked);
        }
    }

    [HarmonyPatch(typeof(LightFlicker), nameof(LightFlicker.CustomUpdate))]
    public static class LightFlicker_CustomUpdate_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(LightFlicker __instance)
        {
            if (__instance == null ||
                !OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableLightOptimization == null ||
                !ModConfig.EnableLightOptimization.Value ||
                ModConfig.CullDistantLightFlicker == null ||
                !ModConfig.CullDistantLightFlicker.Value)
            {
                return true;
            }

            // Defer cleanly to VPO's CullingGroup subclass (VPOLightFlicker) if present
            if (__instance.GetType() != typeof(LightFlicker))
            {
                return true;
            }

            // Never skip timed explosion/effect flashes that destroy themselves via m_ttl
            if (__instance.m_ttl > 0f)
            {
                return true;
            }

            if (OptimizationHelpers.TryGetCameraView(out Vector3 camPos, out Vector3 camForward))
            {
                Vector3 pos = __instance.transform.position;
                float sqrDist = (pos - camPos).sqrMagnitude;
                if (sqrDist > 400f || OptimizationHelpers.IsBehindCamera(pos, camPos, camForward, 36f)) // >20m or >6m behind camera
                {
                    LightShadowOptimizer.FlickersSkipped++;
                    return false;
                }
            }

            return true;
        }
    }

    #endregion
}
