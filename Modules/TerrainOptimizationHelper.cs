using System;
using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Terrain Optimization & Compaction Helper for OptiHouse.
    /// 
    /// Inspects active base zones for legacy uncompressed TerrainModifier stacks.
    /// In earlier versions or heavily terraformed worlds, repeated hoeing/leveling left
    /// hundreds of individual TerrainModifier ZDOs stacked in memory, causing severe
    /// CPU stalls during physics queries, heightmap generation, and saving.
    /// 
    /// Provides safe automatic detection and utility compaction (equivalent to the vanilla
    /// optterrain command) that bakes uncompressed modifications into efficient TerrainComp
    /// heightmap buffers and deletes redundant network objects without altering terrain shape.
    /// 
    /// Exposes public counters for HUD: LegacyTerrainModifiersDetected, TerrainCompactedZones.
    /// </summary>
    public class TerrainOptimizationHelper : MonoBehaviour
    {
        public static TerrainOptimizationHelper? Instance { get; private set; }

        /// <summary>
        /// Total number of uncompressed legacy player TerrainModifier instances detected in active base zones.
        /// </summary>
        public static int LegacyTerrainModifiersDetected { get; internal set; } = 0;

        /// <summary>
        /// Total number of base terrain zones (Heightmaps) successfully compacted during this session.
        /// </summary>
        public static int TerrainCompactedZones { get; internal set; } = 0;

        /// <summary>
        /// Total individual TerrainModifier instances eliminated by the most recent compaction pass.
        /// </summary>
        public static int LastSavedModifiers { get; internal set; } = 0;

        private const float InspectionInterval = 2.5f;
        private const float BaseRadius = 150.0f;
        private const int HighDensityThreshold = 25;

        private float _timer;
        private float _lastWarningTime = -300f;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!OptiHousePlugin.IsOptimizationActive)
            {
                return;
            }

            if (ModConfig.EnableTerrainSanityCheck == null || !ModConfig.EnableTerrainSanityCheck.Value)
            {
                return;
            }

            _timer += Time.deltaTime;
            if (_timer >= InspectionInterval)
            {
                _timer = 0f;
                InspectActiveBaseZones();
            }
        }

        /// <summary>
        /// Scans active heightmaps and modifiers surrounding Player.m_localPlayer.
        /// Updates LegacyTerrainModifiersDetected and logs recommendations if density is extreme.
        /// </summary>
        public static void InspectActiveBaseZones()
        {
            if (Player.m_localPlayer == null)
            {
                LegacyTerrainModifiersDetected = 0;
                return;
            }

            Vector3 playerPos = Player.m_localPlayer.transform.position;
            List<TerrainModifier> allModifiers = TerrainModifier.GetAllInstances();
            if (allModifiers == null || allModifiers.Count == 0)
            {
                LegacyTerrainModifiersDetected = 0;
                return;
            }

            int count = 0;
            for (int i = 0; i < allModifiers.Count; i++)
            {
                TerrainModifier mod = allModifiers[i];
                if (mod == null || !mod.m_playerModifiction)
                {
                    continue;
                }

                if (Utils.DistanceXZ(playerPos, mod.transform.position) <= BaseRadius)
                {
                    count++;
                }
            }

            LegacyTerrainModifiersDetected = count;

            // Warn if dense uncompressed stacks are throttling framerate
            if (count >= HighDensityThreshold && Time.time - (Instance?._lastWarningTime ?? 0f) > 300f)
            {
                if (Instance != null)
                {
                    Instance._lastWarningTime = Time.time;
                }

                OptiHousePlugin.Log?.LogWarning(
                    $"[TerrainOptimizationHelper] High density of uncompressed legacy terrain modifiers ({count}) detected near base. " +
                    "Compacting terrain via TerrainOptimizationHelper.CompactNearbyTerrain() will restore lost FPS.");
            }
        }

        /// <summary>
        /// Safely triggers terrain compaction (equivalent to vanilla optterrain) on active base zones.
        /// Bakes legacy player modifier stacks into TerrainComp heightmaps and safely removes obsolete ZDOs.
        /// </summary>
        /// <param name="radius">Search radius around the local player (default 150m).</param>
        /// <param name="minModifierDensity">Minimum uncompressed modifiers required on a heightmap to trigger compaction.</param>
        /// <param name="showHudMessage">Whether to display a center notification via MessageHud.</param>
        /// <returns>Number of base zones (Heightmaps) compacted.</returns>
        public static int CompactNearbyTerrain(float radius = BaseRadius, int minModifierDensity = 3, bool showHudMessage = true)
        {
            if (Player.m_localPlayer == null)
            {
                OptiHousePlugin.Log?.LogWarning("[TerrainOptimizationHelper] Cannot compact terrain: Local player not in world.");
                return 0;
            }

            Vector3 playerPos = Player.m_localPlayer.transform.position;
            List<Heightmap> heightmaps = new List<Heightmap>();
            Heightmap.FindHeightmap(playerPos, radius, heightmaps);

            if (heightmaps.Count == 0)
            {
                OptiHousePlugin.Log?.LogInfo("[TerrainOptimizationHelper] No active heightmaps found within range.");
                return 0;
            }

            List<TerrainModifier> allModifiers = TerrainModifier.GetAllInstances();
            int compactedZones = 0;
            int savedModifiers = 0;

            foreach (Heightmap hm in heightmaps)
            {
                if (hm == null)
                {
                    continue;
                }

                int inHmCount = 0;
                for (int i = 0; i < allModifiers.Count; i++)
                {
                    TerrainModifier mod = allModifiers[i];
                    if (mod != null && mod.m_playerModifiction && hm.CheckTerrainModIsContained(mod))
                    {
                        inHmCount++;
                    }
                }

                if (inHmCount >= minModifierDensity)
                {
                    try
                    {
                        bool success = TerrainComp.UpgradeTerrain(hm);
                        if (success)
                        {
                            compactedZones++;
                            savedModifiers += inHmCount;
                            TerrainCompactedZones++;
                            OptiHousePlugin.Log?.LogInfo($"[TerrainOptimizationHelper] Compacted zone at {hm.transform.position} (saved {inHmCount} modifiers).");
                        }
                    }
                    catch (Exception ex)
                    {
                        OptiHousePlugin.Log?.LogWarning($"[TerrainOptimizationHelper] Failed to compact heightmap at {hm.transform.position}: {ex.Message}");
                    }
                }
            }

            LastSavedModifiers = savedModifiers;

            // Immediate re-inspection of current zone
            InspectActiveBaseZones();

            if (showHudMessage && MessageHud.instance != null)
            {
                if (compactedZones > 0)
                {
                    MessageHud.instance.ShowMessage(
                        MessageHud.MessageType.Center,
                        $"<color=#00FF66><b>OptiHouse:</b> Compacted {compactedZones} base zone(s)! Saved {savedModifiers} modifiers.</color>");
                }
                else
                {
                    MessageHud.instance.ShowMessage(
                        MessageHud.MessageType.Center,
                        "OptiHouse: Base terrain is already compact. No legacy modifier stacks found.");
                }
            }

            OptiHousePlugin.Log?.LogInfo($"[TerrainOptimizationHelper] Compaction pass complete. Compacted {compactedZones} zones, saved {savedModifiers} legacy modifiers.");
            return compactedZones;
        }
    }

    #region Harmony Patches

    /// <summary>
    /// Auto-attaches TerrainOptimizationHelper to OptiHousePlugin GameObject on Awake.
    /// </summary>
    [HarmonyPatch(typeof(OptiHousePlugin), "Awake")]
    public static class TerrainOptimizationHelper_Plugin_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(OptiHousePlugin __instance)
        {
            if (__instance != null && __instance.gameObject.GetComponent<TerrainOptimizationHelper>() == null)
            {
                __instance.gameObject.AddComponent<TerrainOptimizationHelper>();
            }
        }
    }

    #endregion
}
