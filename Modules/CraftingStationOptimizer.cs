using System;
using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Crafting Station Optimizer for OptiHouse.
    /// Optimizes crafting, smelting, and processing stations in Valheim bases:
    /// - Smelter, Blast Furnace, Charcoal Kiln, Spinning Wheel, Eitr Refinery (Smelter)
    /// - Fermenter
    /// - Cooking Station, Iron Cooking Station, Cauldron (CookingStation)
    /// - Workbench, Forge, Stonecutter, Artisan Table, Black Forge, Galdr Table (CraftingStation)
    /// - Windmill
    /// 
    /// When distance > StationCullDistance (default 18m), pauses attached ParticleSystems
    /// (sparks, bubbling liquids, grinding particles, smoke bursts) and looping ambient AudioSources.
    /// Completely preserves core game logic, fuel consumption, cooking timers, and item yields.
    /// Smoothly restores full visuals and sound as the player approaches.
    /// 
    /// Exposes public counters for HUD: TotalStations, CulledStations.
    /// </summary>
    public class CraftingStationOptimizer : MonoBehaviour
    {
        public static CraftingStationOptimizer? Instance { get; private set; }

        public static int TotalStations { get; private set; }
        public static int CulledStations { get; private set; }

        private const float UpdateInterval = 0.35f;
        private const float HysteresisMargin = 3.0f;

        private float _timer;
        private bool _wasActive = true;

        private class TrackedStation
        {
            public int InstanceId;
            public Component Station = null!;
            public Transform Transform = null!;
            public ParticleSystem[] ParticleSystems = Array.Empty<ParticleSystem>();
            public AudioSource[] AudioSources = Array.Empty<AudioSource>();
            public bool IsCulled;
            public readonly HashSet<ParticleSystem> PausedParticles = new();
            public readonly HashSet<AudioSource> PausedAudio = new();
        }

        private static readonly List<TrackedStation> _stations = new();
        private static readonly Dictionary<int, TrackedStation> _stationLookup = new();

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            ScanExistingStations();
        }

        private void OnDestroy()
        {
            RestoreAllStations();
            _stations.Clear();
            _stationLookup.Clear();
            TotalStations = 0;
            CulledStations = 0;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            bool isActive = OptiHousePlugin.IsOptimizationActive &&
                            ModConfig.EnableStationOptimization != null &&
                            ModConfig.EnableStationOptimization.Value;

            if (!isActive)
            {
                if (_wasActive)
                {
                    _wasActive = false;
                    RestoreAllStations();
                    CulledStations = 0;
                }
                return;
            }

            _wasActive = true;

            _timer += Time.deltaTime;
            if (_timer < UpdateInterval)
            {
                return;
            }
            _timer = 0f;

            EvaluateStations();
        }

        /// <summary>
        /// Scans scene for any stations initialized before the plugin or during scene loading.
        /// </summary>
        public static void ScanExistingStations()
        {
            try
            {
                foreach (var s in FindObjectsByType<Smelter>(FindObjectsSortMode.None)) RegisterStation(s);
                foreach (var s in FindObjectsByType<Fermenter>(FindObjectsSortMode.None)) RegisterStation(s);
                foreach (var s in FindObjectsByType<CookingStation>(FindObjectsSortMode.None)) RegisterStation(s);
                foreach (var s in FindObjectsByType<CraftingStation>(FindObjectsSortMode.None)) RegisterStation(s);
                foreach (var s in FindObjectsByType<Windmill>(FindObjectsSortMode.None)) RegisterStation(s);
            }
            catch (Exception ex)
            {
                if (ModConfig.EnableLogging != null && ModConfig.EnableLogging.Value)
                {
                    OptiHousePlugin.Log.LogWarning($"[CraftingStationOptimizer] ScanExistingStations error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Registers a crafting/processing station for distance-based culling.
        /// </summary>
        public static void RegisterStation(Component station)
        {
            if (station == null) return;

            int id = station.GetInstanceID();
            if (_stationLookup.ContainsKey(id))
            {
                return;
            }

            var entry = new TrackedStation
            {
                InstanceId = id,
                Station = station,
                Transform = station.transform,
                ParticleSystems = station.GetComponentsInChildren<ParticleSystem>(true),
                AudioSources = station.GetComponentsInChildren<AudioSource>(true),
                IsCulled = false
            };

            _stations.Add(entry);
            _stationLookup[id] = entry;
            TotalStations = _stations.Count;
        }

        /// <summary>
        /// Unregisters a station when dismantled or destroyed.
        /// </summary>
        public static void UnregisterStation(Component station)
        {
            if (station == null) return;

            int id = station.GetInstanceID();
            if (_stationLookup.TryGetValue(id, out var entry))
            {
                // Restore any paused systems before unregistering
                RestoreStation(entry);
                _stationLookup.Remove(id);
                _stations.Remove(entry);
                TotalStations = _stations.Count;
            }
        }

        /// <summary>
        /// Evaluates all registered stations, culling distant particles and looping audio.
        /// </summary>
        private void EvaluateStations()
        {
            // 1. Clean up destroyed or dismantled stations
            for (int i = _stations.Count - 1; i >= 0; i--)
            {
                var entry = _stations[i];
                if (entry.Station == null || entry.Transform == null)
                {
                    _stationLookup.Remove(entry.InstanceId);
                    _stations.RemoveAt(i);
                }
            }

            TotalStations = _stations.Count;
            if (_stations.Count == 0)
            {
                CulledStations = 0;
                return;
            }

            Vector3 refPos = Vector3.zero;
            bool hasRefPos = false;

            if (Player.m_localPlayer != null)
            {
                refPos = Player.m_localPlayer.transform.position;
                hasRefPos = true;
            }
            else if (Camera.main != null)
            {
                refPos = Camera.main.transform.position;
                hasRefPos = true;
            }

            if (!hasRefPos)
            {
                return;
            }

            float cullDist = ModConfig.StationCullDistance != null
                ? Mathf.Max(5f, ModConfig.StationCullDistance.Value)
                : 18f;

            float cullDistSq = cullDist * cullDist;
            float uncullDist = Mathf.Max(0f, cullDist - HysteresisMargin);
            float uncullDistSq = uncullDist * uncullDist;

            int culledCount = 0;

            for (int i = 0; i < _stations.Count; i++)
            {
                var entry = _stations[i];
                float sqrDist = (entry.Transform.position - refPos).sqrMagnitude;

                if (sqrDist > cullDistSq)
                {
                    // Distant: cull particle emission & pause looping ambient audio
                    CullStation(entry);
                    culledCount++;
                }
                else if (sqrDist < uncullDistSq)
                {
                    // Near: restore full visual particles & audio
                    if (entry.IsCulled)
                    {
                        RestoreStation(entry);
                    }
                }
                else
                {
                    // In hysteresis zone: retain previous state
                    if (entry.IsCulled)
                    {
                        culledCount++;
                    }
                }
            }

            CulledStations = culledCount;
        }

        private static void CullStation(TrackedStation entry)
        {
            entry.IsCulled = true;

            // 1. Pause active ParticleSystems
            for (int i = 0; i < entry.ParticleSystems.Length; i++)
            {
                var ps = entry.ParticleSystems[i];
                if (ps != null && ps.isPlaying && !entry.PausedParticles.Contains(ps))
                {
                    ps.Pause();
                    entry.PausedParticles.Add(ps);
                }
            }

            // 2. Pause looping ambient AudioSources
            for (int i = 0; i < entry.AudioSources.Length; i++)
            {
                var audio = entry.AudioSources[i];
                if (audio != null && audio.loop && audio.isPlaying && !entry.PausedAudio.Contains(audio))
                {
                    audio.Pause();
                    entry.PausedAudio.Add(audio);
                }
            }
        }

        private static void RestoreStation(TrackedStation entry)
        {
            entry.IsCulled = false;

            // 1. Resume paused ParticleSystems
            foreach (var ps in entry.PausedParticles)
            {
                if (ps != null && ps.isPaused)
                {
                    ps.Play();
                }
            }
            entry.PausedParticles.Clear();

            // 2. Resume paused AudioSources
            foreach (var audio in entry.PausedAudio)
            {
                if (audio != null)
                {
                    audio.UnPause();
                }
            }
            entry.PausedAudio.Clear();
        }

        /// <summary>
        /// Restores all tracked stations to full un-culled operation.
        /// </summary>
        public static void RestoreAllStations()
        {
            for (int i = 0; i < _stations.Count; i++)
            {
                RestoreStation(_stations[i]);
            }
            CulledStations = 0;
        }
    }

    #region Harmony Patches

    /// <summary>
    /// Auto-attach CraftingStationOptimizer to OptiHousePlugin GameObject on Awake.
    /// </summary>
    [HarmonyPatch(typeof(OptiHousePlugin), "Awake")]
    public static class CraftingStationOptimizer_Plugin_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(OptiHousePlugin __instance)
        {
            if (__instance != null && __instance.gameObject.GetComponent<CraftingStationOptimizer>() == null)
            {
                __instance.gameObject.AddComponent<CraftingStationOptimizer>();
            }
        }
    }

    /// <summary>
    /// Registers Smelter (Smelter, Blast Furnace, Kiln, Spinning Wheel, Eitr Refinery) on Awake.
    /// </summary>
    [HarmonyPatch(typeof(Smelter), "Awake")]
    public static class Smelter_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Smelter __instance)
        {
            CraftingStationOptimizer.RegisterStation(__instance);
        }
    }

    [HarmonyPatch(typeof(Smelter), "OnDestroyed")]
    public static class Smelter_OnDestroyed_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Smelter __instance)
        {
            CraftingStationOptimizer.UnregisterStation(__instance);
        }
    }

    /// <summary>
    /// Registers Fermenter on Awake.
    /// </summary>
    [HarmonyPatch(typeof(Fermenter), "Awake")]
    public static class Fermenter_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Fermenter __instance)
        {
            CraftingStationOptimizer.RegisterStation(__instance);
        }
    }

    [HarmonyPatch(typeof(Fermenter), "OnDestroyed")]
    public static class Fermenter_OnDestroyed_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Fermenter __instance)
        {
            CraftingStationOptimizer.UnregisterStation(__instance);
        }
    }

    /// <summary>
    /// Registers CookingStation (Wooden Cooking Station, Iron Cooking Station, Cauldron) on Awake.
    /// </summary>
    [HarmonyPatch(typeof(CookingStation), "Awake")]
    public static class CookingStation_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CookingStation __instance)
        {
            CraftingStationOptimizer.RegisterStation(__instance);
        }
    }

    [HarmonyPatch(typeof(CookingStation), "OnDestroyed")]
    public static class CookingStation_OnDestroyed_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CookingStation __instance)
        {
            CraftingStationOptimizer.UnregisterStation(__instance);
        }
    }

    /// <summary>
    /// Registers CraftingStation (Workbench, Forge, Stonecutter, Artisan Table, etc.) on Start.
    /// </summary>
    [HarmonyPatch(typeof(CraftingStation), "Start")]
    public static class CraftingStation_Start_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CraftingStation __instance)
        {
            CraftingStationOptimizer.RegisterStation(__instance);
        }
    }

    [HarmonyPatch(typeof(CraftingStation), "OnDestroy")]
    public static class CraftingStation_OnDestroy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CraftingStation __instance)
        {
            CraftingStationOptimizer.UnregisterStation(__instance);
        }
    }

    /// <summary>
    /// Registers Windmill on Start.
    /// </summary>
    [HarmonyPatch(typeof(Windmill), "Start")]
    public static class Windmill_Start_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Windmill __instance)
        {
            CraftingStationOptimizer.RegisterStation(__instance);
        }
    }

    #endregion
}
