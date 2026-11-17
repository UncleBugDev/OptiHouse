using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;
using OptiHouse.Configuration;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Engine Gfx Jobs inspector and opt-in boot.config tweaker for OptiHouse.
    /// Reads valheim_Data/boot.config to report multi-threaded Gfx Jobs status in HUD.
    /// Only modifies boot.config when EnableGfxJobsTweak is explicitly enabled (opt-in).
    /// </summary>
    public static class BootConfigTweaker
    {
        public static bool IsGfxJobsActive { get; internal set; } = false;
        public static bool RestartRequired { get; internal set; } = false;
        public static bool BackupCreated { get; internal set; } = false;
        public static string? BootConfigPath { get; private set; }

        private static bool _initialized = false;

        public static void Initialize()
        {
            ApplyBootConfigTweaks();
        }

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
                    if (ModConfig.EnableLogging != null && ModConfig.EnableLogging.Value)
                    {
                        OptiHousePlugin.Log?.LogWarning($"[BootConfigTweaker] boot.config could not be located via Application.dataPath ({Application.dataPath}).");
                    }
                    return;
                }

                BootConfigPath = configPath;
                string[] lines = File.ReadAllLines(configPath);

                bool hasGfxJobs = lines.Any(l => l.Trim().Equals("gfx-enable-gfx-jobs=1", StringComparison.OrdinalIgnoreCase));
                bool hasNativeGfxJobs = lines.Any(l => l.Trim().Equals("gfx-enable-native-gfx-jobs=1", StringComparison.OrdinalIgnoreCase));

                if (hasGfxJobs)
                {
                    IsGfxJobsActive = true;
                }

                if (hasGfxJobs && hasNativeGfxJobs)
                {
                    RestartRequired = false;
                    return;
                }

                // Opt-in only: do not mutate external game files unless explicitly enabled
                if (ModConfig.EnableGfxJobsTweak == null || !ModConfig.EnableGfxJobsTweak.Value)
                {
                    return;
                }

                string backupPath = configPath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(configPath, backupPath, false);
                    BackupCreated = true;
                    OptiHousePlugin.Log?.LogInfo($"[BootConfigTweaker] Created engine config backup at: {backupPath}");
                }

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

                OptiHousePlugin.Log?.LogInfo($"[BootConfigTweaker] Injected multi-threaded Gfx Jobs into {configPath}. Restart required.");
            }
            catch (Exception ex)
            {
                OptiHousePlugin.Log?.LogError($"[BootConfigTweaker] Failed to read/update boot.config: {ex.Message}");
            }
        }

        private static string? FindBootConfigPath()
        {
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
            }

            string cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "valheim_Data", "boot.config");
            if (File.Exists(cwdPath))
            {
                return cwdPath;
            }

            return null;
        }
    }
}
