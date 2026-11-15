using System;
using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Dynamic Light & Shadow Optimizer for OptiHouse.
    /// Manages the real-time shadow budget for point lights in Valheim bases.
    /// - Sets LightLod.m_shadowLimit to cap simultaneous shadow-casting lights around the player.
    /// - Strips heavy dynamic shadows from wall torches, sconces, and candles.
    /// - Tracks scene point lights dynamically and falls back to LightShadows.None beyond the budget.
    /// </summary>
    public class LightShadowOptimizer : MonoBehaviour
    {
        private static LightShadowOptimizer? _instance;
        public static LightShadowOptimizer? Instance => _instance;

        // Track point lights that do not have LightLod or need manual budget enforcement
        private static readonly HashSet<Light> _registeredLights = new HashSet<Light>();
        private static readonly List<LightDistanceEntry> _lightSortBuffer = new List<LightDistanceEntry>();
        private static readonly Dictionary<Light, LightShadows> _originalShadowModes = new Dictionary<Light, LightShadows>();

        private static readonly System.Reflection.FieldInfo? _lightLodLightsField =
            typeof(LightLod).GetField("m_lights", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        public static int TotalLights
        {
            get
            {
                int lodCount = (_lightLodLightsField?.GetValue(null) as HashSet<LightLod>)?.Count ?? 0;
                return lodCount + _registeredLights.Count;
            }
        }

        public static int ActiveShadowLights
        {
            get
            {
                int total = TotalLights;
                if (!OptiHousePlugin.IsOptimizationActive || ModConfig.EnableLightOptimization == null || !ModConfig.EnableLightOptimization.Value)
                {
                    return total;
                }
                int maxShadows = ModConfig.MaxShadowCastingLights != null ? ModConfig.MaxShadowCastingLights.Value : 6;
                return Mathf.Min(maxShadows, total);
            }
        }

        public static int CulledShadowLights => Mathf.Max(0, TotalLights - ActiveShadowLights);

        private float _updateTimer;
        private bool _wasOptimizationActive = true;

        private struct LightDistanceEntry
        {
            public Light Light;
            public float SqrDistance;
        }

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            RestoreAllLights();
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            bool isActive = OptiHousePlugin.IsOptimizationActive &&
                            ModConfig.EnableLightOptimization != null &&
                            ModConfig.EnableLightOptimization.Value;

            if (!isActive)
            {
                if (_wasOptimizationActive)
                {
                    _wasOptimizationActive = false;
                    RestoreAllLights();
                }
                return;
            }

            _wasOptimizationActive = true;

            // Update manual shadow budget at 1.0 second intervals (synchronized with LightLod)
            _updateTimer += Time.deltaTime;
            if (_updateTimer < 1.0f)
            {
                return;
            }
            _updateTimer = 0f;

            ManageScenePointLights();
        }

        /// <summary>
        /// Registers a point light for dynamic shadow budget management.
        /// </summary>
        public static void RegisterLight(Light light)
        {
            if (light == null || light.type != LightType.Point) return;
            if (!_registeredLights.Contains(light))
            {
                _registeredLights.Add(light);
                if (!_originalShadowModes.ContainsKey(light))
                {
                    _originalShadowModes[light] = light.shadows;
                }
            }
        }

        /// <summary>
        /// Unregisters a point light.
        /// </summary>
        public static void UnregisterLight(Light light)
        {
            if (light == null) return;
            _registeredLights.Remove(light);
            _originalShadowModes.Remove(light);
        }

        /// <summary>
        /// Enforces the shadow budget across registered non-LightLod point lights in base vicinity.
        /// </summary>
        private static void ManageScenePointLights()
        {
            if (_registeredLights.Count == 0) return;

            Vector3 refPos = GetReferencePosition();
            int maxShadowLights = ModConfig.MaxShadowCastingLights != null
                ? ModConfig.MaxShadowCastingLights.Value
                : 6;

            _lightSortBuffer.Clear();
            _registeredLights.RemoveWhere(l => l == null);

            // Collect active lights
            foreach (Light light in _registeredLights)
            {
                if (!light.enabled || !light.gameObject.activeInHierarchy)
                {
                    continue;
                }

                float sqrDist = (light.transform.position - refPos).sqrMagnitude;
                _lightSortBuffer.Add(new LightDistanceEntry
                {
                    Light = light,
                    SqrDistance = sqrDist
                });
            }

            if (_lightSortBuffer.Count == 0) return;

            // Sort closest first
            _lightSortBuffer.Sort((a, b) => a.SqrDistance.CompareTo(b.SqrDistance));

            for (int i = 0; i < _lightSortBuffer.Count; i++)
            {
                Light light = _lightSortBuffer[i].Light;
                if (light == null) continue;

                if (i < maxShadowLights)
                {
                    // Allow original shadow mode
                    if (_originalShadowModes.TryGetValue(light, out LightShadows orig) && orig != LightShadows.None)
                    {
                        light.shadows = orig;
                    }
                }
                else
                {
                    // Strip shadow for lights beyond the budget
                    if (light.shadows != LightShadows.None)
                    {
                        light.shadows = LightShadows.None;
                    }
                }
            }
        }

        /// <summary>
        /// Restores all managed lights to their default shadow casting settings.
        /// </summary>
        public static void RestoreAllLights()
        {
            LightLod.m_shadowLimit = -1;

            foreach (var kvp in _originalShadowModes)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.shadows = kvp.Value;
                }
            }
            _registeredLights.Clear();
            _originalShadowModes.Clear();
            _lightSortBuffer.Clear();
        }

        private static Vector3 GetReferencePosition()
        {
            if (Player.m_localPlayer != null)
            {
                return Player.m_localPlayer.transform.position;
            }
            Camera mainCam = Utils.GetMainCamera();
            if (mainCam != null)
            {
                return mainCam.transform.position;
            }
            return Vector3.zero;
        }

        /// <summary>
        /// Determines whether a GameObject represents a small torch, sconce, candle, or small light.
        /// </summary>
        public static bool IsSmallTorchOrSconce(GameObject go)
        {
            if (go == null) return false;

            string name = go.name.ToLowerInvariant();
            if (name.Contains("torch") ||
                name.Contains("sconce") ||
                name.Contains("candle") ||
                name.Contains("lantern") ||
                name.Contains("brazier") ||
                name.Contains("jackoturnip"))
            {
                return true;
            }

            Piece piece = go.GetComponentInParent<Piece>();
            if (piece != null && !string.IsNullOrEmpty(piece.m_name))
            {
                string pieceName = piece.m_name.ToLowerInvariant();
                if (pieceName.Contains("torch") ||
                    pieceName.Contains("sconce") ||
                    pieceName.Contains("candle") ||
                    pieceName.Contains("lantern") ||
                    pieceName.Contains("brazier"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Disables shadow casting on LightLod and its associated Light component.
        /// </summary>
        public static void StripShadowsFromLod(LightLod lod)
        {
            if (lod == null) return;
            lod.m_shadowLod = false;
            lod.m_shadowDistance = 0f;

            Light light = lod.GetComponent<Light>();
            if (light != null)
            {
                light.shadows = LightShadows.None;
                light.shadowStrength = 0f;
            }
        }

        /// <summary>
        /// Disables shadow casting recursively across all child lights of an object.
        /// </summary>
        public static void StripShadowsFromObject(GameObject go)
        {
            if (go == null) return;

            LightLod[] lods = go.GetComponentsInChildren<LightLod>(true);
            for (int i = 0; i < lods.Length; i++)
            {
                StripShadowsFromLod(lods[i]);
            }

            Light[] lights = go.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                {
                    lights[i].shadows = LightShadows.None;
                    lights[i].shadowStrength = 0f;
                }
            }
        }
    }

    #region Harmony Patches

    /// <summary>
    /// Attach LightShadowOptimizer to the mod plugin GameObject on startup.
    /// </summary>
    [HarmonyPatch(typeof(OptiHousePlugin), "Awake")]
    public static class LightShadowOptimizer_Plugin_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(OptiHousePlugin __instance)
        {
            if (__instance != null && __instance.gameObject.GetComponent<LightShadowOptimizer>() == null)
            {
                __instance.gameObject.AddComponent<LightShadowOptimizer>();
            }
        }
    }

    /// <summary>
    /// Enforces LightLod.m_shadowLimit dynamically based on OptiHouse configuration.
    /// </summary>
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
                int maxShadows = ModConfig.MaxShadowCastingLights != null
                    ? ModConfig.MaxShadowCastingLights.Value
                    : 6;

                LightLod.m_shadowLimit = maxShadows;
            }
            else
            {
                LightLod.m_shadowLimit = -1;
            }
        }
    }

    /// <summary>
    /// Strips shadows from small torches/sconces/candles when LightLod awakes.
    /// Also applies maximum light cull distance.
    /// </summary>
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

            // Strip torch and sconce shadows if configured
            if (ModConfig.DisableTorchShadows != null &&
                ModConfig.DisableTorchShadows.Value &&
                LightShadowOptimizer.IsSmallTorchOrSconce(__instance.gameObject))
            {
                LightShadowOptimizer.StripShadowsFromLod(__instance);
            }

            // Apply light cull distance limit
            if (ModConfig.LightCullDistance != null && ModConfig.LightCullDistance.Value > 0f)
            {
                __instance.m_lightDistance = Mathf.Min(__instance.m_lightDistance, ModConfig.LightCullDistance.Value);
            }
        }
    }

    /// <summary>
    /// Strips shadows from Fireplace pieces (torches, sconces, wall braziers) when they initialize.
    /// </summary>
    [HarmonyPatch(typeof(Fireplace), "Start")]
    public static class Fireplace_Start_Patch
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

            if (LightShadowOptimizer.IsSmallTorchOrSconce(__instance.gameObject))
            {
                LightShadowOptimizer.StripShadowsFromObject(__instance.gameObject);
            }
        }
    }

    /// <summary>
    /// Re-applies shadow stripping when a Fireplace toggles or updates state (e.g. refueled).
    /// </summary>
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

            if (LightShadowOptimizer.IsSmallTorchOrSconce(__instance.gameObject))
            {
                LightShadowOptimizer.StripShadowsFromObject(__instance.gameObject);
            }
        }
    }

    #endregion
}
