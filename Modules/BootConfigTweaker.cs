using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Multi-Threaded Engine & Graphics Jobs Boot Config Tweaker for OptiHouse.
    /// 
    /// Inspects and optimizes the Unity engine configuration (valheim_Data/boot.config).
    /// Injects gfx-enable-gfx-jobs=1 and gfx-enable-native-gfx-jobs=1 to allow Unity's
    /// rendering pipeline to dispatch graphics jobs across multiple worker threads,
    /// significantly eliminating main-thread draw call bottlenecks in dense player bases.
    /// 
    /// Automatically creates boot.config.bak before modifying the file.
    /// Exposes public bool IsGfxJobsActive for diagnostic and HUD reporting.
    /// </summary>
    public static class BootConfigTweaker
    {
        public static bool IsGfxJobsActive { get; internal set; } = false;
        public static bool RestartRequired { get; internal set; } = false;
        public static bool BackupCreated { get; internal set; } = false;
        public static string? BootConfigPath { get; private set; }

        private static bool _initialized = false;

        /// <summary>
        /// Scans for valheim_Data/boot.config and applies multi-threaded graphics jobs tweaks if enabled.
        /// Safe and idempotent: can be called repeatedly without duplicate injections or side effects.
        /// </summary>
        public static void Initialize()
        {
            ApplyBootConfigTweaks();
        }

        /// <summary>
        /// Reads valheim_Data/boot.config and injects gfx job optimization flags if missing.
        /// </summary>
        public static void ApplyBootConfigTweaks()
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;

            try
            {
                string? configPath = FindBootConfigPath();
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
                {
                    OptiHousePlugin.Log?.LogWarning($"[BootConfigTweaker] boot.config could not be located via Application.dataPath ({Application.dataPath}). Engine tweak skipped.");
                    return;
                }

                BootConfigPath = configPath;
                string[] lines = File.ReadAllLines(configPath);

                bool hasGfxJobs = lines.Any(l => l.Trim().Equals("gfx-enable-gfx-jobs=1", StringComparison.OrdinalIgnoreCase));
                bool hasNativeGfxJobs = lines.Any(l => l.Trim().Equals("gfx-enable-native-gfx-jobs=1", StringComparison.OrdinalIgnoreCase));

                if (hasGfxJobs && hasNativeGfxJobs)
                {
                    IsGfxJobsActive = true;
                    RestartRequired = false;
                    OptiHousePlugin.Log?.LogInfo("[BootConfigTweaker] Multi-threaded Gfx Jobs (gfx-enable-gfx-jobs=1, gfx-enable-native-gfx-jobs=1) are already configured and active.");
                    return;
                }

                // If gfx-enable-gfx-jobs=1 was already present at startup, jobs were active in current engine instance
                if (hasGfxJobs)
                {
                    IsGfxJobsActive = true;
                }

                if (ModConfig.EnableGfxJobsTweak == null || !ModConfig.EnableGfxJobsTweak.Value)
                {
                    OptiHousePlugin.Log?.LogInfo("[BootConfigTweaker] EnableGfxJobsTweak is false in ModConfig. Skipping injection.");
                    return;
                }

                // Safely create backup
                string backupPath = configPath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(configPath, backupPath, false);
                    BackupCreated = true;
                    OptiHousePlugin.Log?.LogInfo($"[BootConfigTweaker] Created engine config backup at: {backupPath}");
                }

                // Rebuild config lines with flags injected at the top
                List<string> updatedLines = new List<string>
                {
                    "gfx-enable-gfx-jobs=1",
                    "gfx-enable-native-gfx-jobs=1"
                };

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        continue;
                    }

                    // Remove older/contrary values for these specific keys
                    if (trimmed.StartsWith("gfx-enable-gfx-jobs=", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("gfx-enable-native-gfx-jobs=", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    updatedLines.Add(trimmed);
                }

                File.WriteAllLines(configPath, updatedLines, new UTF8Encoding(false));
                IsGfxJobsActive = true;
                RestartRequired = true;

                OptiHousePlugin.Log?.LogInfo($"[BootConfigTweaker] Successfully injected multi-threaded Gfx Jobs into {configPath}. A game restart is required for changes to take full effect.");
            }
            catch (Exception ex)
            {
                OptiHousePlugin.Log?.LogError($"[BootConfigTweaker] Failed to evaluate or inject boot.config tweaks: {ex.Message}");
            }
        }

        /// <summary>
        /// Locates boot.config across standard Valheim installation and runtime paths.
        /// </summary>
        private static string? FindBootConfigPath()
        {
            // 1. Primary path: Application.dataPath points directly to valheim_Data in standalone builds
            if (!string.IsNullOrEmpty(Application.dataPath))
            {
                string directPath = Path.Combine(Application.dataPath, "boot.config");
                if (File.Exists(directPath))
                {
                    return directPath;
                }

                string nestedPath = Path.Combine(Application.dataPath, "valheim_Data", "boot.config");
                if (File.Exists(nestedPath))
                {
                    return nestedPath;
                }
            }

            // 2. Fallback via BepInEx Paths.GameRootPath
            try
            {
                string rootPath = Paths.GameRootPath;
                if (!string.IsNullOrEmpty(rootPath))
                {
                    string rootDataPath = Path.Combine(rootPath, "valheim_Data", "boot.config");
                    if (File.Exists(rootDataPath))
                    {
                        return rootDataPath;
                    }
                }
            }
            catch
            {
                // Ignored if Paths.GameRootPath is not initialized yet
            }

            // 3. Fallback via current working directory
            string cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "valheim_Data", "boot.config");
            if (File.Exists(cwdPath))
            {
                return cwdPath;
            }

            return null;
        }
    }

    #region Harmony Patches

    /// <summary>
    /// Ensures boot.config tweaks are evaluated at earliest plugin startup.
    /// </summary>
    [HarmonyPatch(typeof(OptiHousePlugin), "Awake")]
    public static class BootConfigTweaker_Plugin_Patch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyPostfix]
        public static void Postfix()
        {
            BootConfigTweaker.Initialize();
        }
    }

    #endregion
}
