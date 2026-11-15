using BepInEx.Configuration;
using UnityEngine;

namespace OptiHouse.Configuration
{
    public enum HudAnchorPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public static class ModConfig
    {
        // 1. General
        public static ConfigEntry<bool> MasterOptimizationEnabled = null!;
        public static ConfigEntry<KeyCode> ToggleHotkey = null!;
        public static ConfigEntry<bool> ShowNotificationOnToggle = null!;
        public static ConfigEntry<bool> EnableLogging = null!;
        public static ConfigEntry<bool> EnableGfxJobsTweak = null!;

        // 2. Lighting
        public static ConfigEntry<bool> EnableLightOptimization = null!;
        public static ConfigEntry<int> MaxShadowCastingLights = null!;
        public static ConfigEntry<bool> DisableTorchShadows = null!;
        public static ConfigEntry<float> LightCullDistance = null!;

        // 3. VFX
        public static ConfigEntry<bool> EnableVfxOptimization = null!;
        public static ConfigEntry<bool> SuppressCultivationParticles = null!;
        public static ConfigEntry<bool> SuppressDestructionParticles = null!;
        public static ConfigEntry<float> VfxThrottlingDistance = null!;
        public static ConfigEntry<bool> EnableFastParticleFade = null!;
        public static ConfigEntry<float> ParticleLifetimeMultiplier = null!;
        public static ConfigEntry<float> MaxParticleLifetimeSeconds = null!;
        public static ConfigEntry<float> ParticleSimulationSpeed = null!;

        // 4. Smoke
        public static ConfigEntry<bool> EnableSmokeOptimization = null!;
        public static ConfigEntry<int> MaxActiveSmokePuffs = null!;
        public static ConfigEntry<bool> ThrottleSmokeRaycasts = null!;
        public static ConfigEntry<bool> DisableSmokeRendering = null!;
        public static ConfigEntry<float> SmokeLifetimeMultiplier = null!;

        // 5. Structural
        public static ConfigEntry<bool> EnableStructuralOptimization = null!;
        public static ConfigEntry<bool> EnableWearNTearThrottling = null!;
        public static ConfigEntry<float> WearNTearThrottleInterval = null!;
        public static ConfigEntry<bool> EnablePieceShadowCulling = null!;
        public static ConfigEntry<float> PieceShadowCullDistance = null!;
        public static ConfigEntry<bool> EnableDecorativeColliderOptimization = null!;

        // 6. Cloth & Banners
        public static ConfigEntry<bool> EnableClothOptimization = null!;
        public static ConfigEntry<float> BannerClothCullDistance = null!;

        // 7. Storage & ItemStands
        public static ConfigEntry<bool> EnableItemStandOptimization = null!;
        public static ConfigEntry<float> ItemStandCullDistance = null!;

        // 8. Farm Animals
        public static ConfigEntry<bool> EnableTamedAnimalLod = null!;
        public static ConfigEntry<float> TamedAnimalLodDistance = null!;

        // 9. Crafting Stations
        public static ConfigEntry<bool> EnableStationOptimization = null!;
        public static ConfigEntry<float> StationCullDistance = null!;

        // 10. HUD & Benchmark
        public static ConfigEntry<bool> EnableBenchmarkHUD = null!;
        public static ConfigEntry<KeyCode> BenchmarkHudHotkey = null!;
        public static ConfigEntry<bool> BenchmarkHudRequireShift = null!;
        public static ConfigEntry<int> HudTextSize = null!;
        public static ConfigEntry<HudAnchorPosition> HudPosition = null!;

        // 11. Interior Occlusion
        public static ConfigEntry<bool> EnableInteriorCulling = null!;
        public static ConfigEntry<float> InteriorCullDistance = null!;

        // 12. Terrain Helpers
        public static ConfigEntry<bool> EnableTerrainSanityCheck = null!;

        public static void Init(ConfigFile config)
        {
            // [General]
            MasterOptimizationEnabled = config.Bind(
                "1. General",
                "MasterOptimizationEnabled",
                true,
                new ConfigDescription("Master switch for all OptiHouse base optimizations. Can also be toggled in-game with the hotkey.")
            );

            ToggleHotkey = config.Bind(
                "1. General",
                "ToggleHotkey",
                KeyCode.F7,
                new ConfigDescription("Hotkey to toggle optimizations on and off at runtime.")
            );

            ShowNotificationOnToggle = config.Bind(
                "1. General",
                "ShowNotificationOnToggle",
                true,
                new ConfigDescription("Display an on-screen MessageHud notification when optimizations are toggled via hotkey.")
            );

            EnableLogging = config.Bind(
                "1. General",
                "EnableLogging",
                false,
                new ConfigDescription("Enable diagnostic debug logging in the BepInEx console.")
            );

            EnableGfxJobsTweak = config.Bind(
                "1. General",
                "EnableGfxJobsTweak",
                true,
                new ConfigDescription("Enables multi-threaded graphics rendering job tweaks in boot.config (gfx-enable-gfx-jobs=1, gfx-enable-native-gfx-jobs=1). Requires game restart to take effect.")
            );

            // [Lighting]
            EnableLightOptimization = config.Bind(
                "2. Lighting",
                "EnableLightOptimization",
                true,
                new ConfigDescription("Enable dynamic point light shadow budget and torch shadow optimization.")
            );

            MaxShadowCastingLights = config.Bind(
                "2. Lighting",
                "MaxShadowCastingLights",
                6,
                new ConfigDescription(
                    "Maximum number of point lights allowed to cast real-time dynamic shadows near the player.",
                    new AcceptableValueRange<int>(0, 32)
                )
            );

            DisableTorchShadows = config.Bind(
                "2. Lighting",
                "DisableTorchShadows",
                true,
                new ConfigDescription("Strip shadows from wall torches, sconces, and standing braziers.")
            );

            LightCullDistance = config.Bind(
                "2. Lighting",
                "LightCullDistance",
                45.0f,
                new ConfigDescription(
                    "Distance (in meters) at which small interior base lights are culled.",
                    new AcceptableValueRange<float>(10f, 100f)
                )
            );

            // [VFX]
            EnableVfxOptimization = config.Bind(
                "3. VFX",
                "EnableVfxOptimization",
                true,
                new ConfigDescription("Enable particle and visual effect throttling/suppression.")
            );

            SuppressCultivationParticles = config.Bind(
                "3. VFX",
                "SuppressCultivationParticles",
                true,
                new ConfigDescription("Suppress heavy particle bursts when cultivating soil, leveling ground, or using a hoe.")
            );

            SuppressDestructionParticles = config.Bind(
                "3. VFX",
                "SuppressDestructionParticles",
                true,
                new ConfigDescription("Suppress heavy debris chunk bursts and wood/stone dust particle spam when demolishing pieces.")
            );

            VfxThrottlingDistance = config.Bind(
                "3. VFX",
                "VfxThrottlingDistance",
                30.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which ambient building particle effects are throttled.",
                    new AcceptableValueRange<float>(5f, 100f)
                )
            );

            EnableFastParticleFade = config.Bind(
                "3. VFX",
                "EnableFastParticleFade",
                true,
                new ConfigDescription("Accelerates the dissipation of particles (debris, dust, smoke, sparks) so they vanish quickly.")
            );

            ParticleLifetimeMultiplier = config.Bind(
                "3. VFX",
                "ParticleLifetimeMultiplier",
                0.35f,
                new ConfigDescription(
                    "Multiplier for particle lifetimes. 0.35 = particles live only 35% of their normal duration and fade out ~3x faster.",
                    new AcceptableValueRange<float>(0.1f, 1.0f)
                )
            );

            MaxParticleLifetimeSeconds = config.Bind(
                "3. VFX",
                "MaxParticleLifetimeSeconds",
                2.0f,
                new ConfigDescription(
                    "Hard cap in seconds for the maximum lifetime of any temporary particle effect or demolition debris.",
                    new AcceptableValueRange<float>(0.5f, 10.0f)
                )
            );

            ParticleSimulationSpeed = config.Bind(
                "3. VFX",
                "ParticleSimulationSpeed",
                1.5f,
                new ConfigDescription(
                    "Speed multiplier for particle animations. Higher values make dust, smoke, and sparks disperse and dissolve quickly.",
                    new AcceptableValueRange<float>(1.0f, 3.0f)
                )
            );

            // [Smoke]
            EnableSmokeOptimization = config.Bind(
                "4. Smoke",
                "EnableSmokeOptimization",
                true,
                new ConfigDescription("Enable indoor smoke puff and physics optimization.")
            );

            MaxActiveSmokePuffs = config.Bind(
                "4. Smoke",
                "MaxActiveSmokePuffs",
                25,
                new ConfigDescription(
                    "Hard cap for simultaneous active smoke puffs in base areas (Valheim vanilla default is 100+).",
                    new AcceptableValueRange<int>(5, 200)
                )
            );

            ThrottleSmokeRaycasts = config.Bind(
                "4. Smoke",
                "ThrottleSmokeRaycasts",
                true,
                new ConfigDescription("Throttle indoor ceiling collision raycasts for rising smoke particles to save CPU cycles.")
            );

            DisableSmokeRendering = config.Bind(
                "4. Smoke",
                "DisableSmokeRendering",
                false,
                new ConfigDescription("Disable visual rendering of smoke particles while keeping warmth, fire, and comfort mechanics fully intact.")
            );

            SmokeLifetimeMultiplier = config.Bind(
                "4. Smoke",
                "SmokeLifetimeMultiplier",
                0.35f,
                new ConfigDescription(
                    "Multiplier for chimney and hearth smoke puff lifetime (TTL). Smoke dissolves quickly instead of lingering indefinitely.",
                    new AcceptableValueRange<float>(0.1f, 1.0f)
                )
            );

            // [Structural]
            EnableStructuralOptimization = config.Bind(
                "5. Structural",
                "EnableStructuralOptimization",
                true,
                new ConfigDescription("Enable structural piece shadow culling and WearNTear raycast throttling.")
            );

            EnableWearNTearThrottling = config.Bind(
                "5. Structural",
                "EnableWearNTearThrottling",
                true,
                new ConfigDescription("Throttle periodic sky/weather raycast checks (UpdateCover) on intact, static building pieces.")
            );

            WearNTearThrottleInterval = config.Bind(
                "5. Structural",
                "WearNTearThrottleInterval",
                10.0f,
                new ConfigDescription(
                    "Seconds between weather protection raycasts for static covered pieces.",
                    new AcceptableValueRange<float>(2f, 30f)
                )
            );

            EnablePieceShadowCulling = config.Bind(
                "5. Structural",
                "EnablePieceShadowCulling",
                true,
                new ConfigDescription("Disable shadow casting on small structural elements beyond the culling distance.")
            );

            PieceShadowCullDistance = config.Bind(
                "5. Structural",
                "PieceShadowCullDistance",
                15.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which small structural pieces stop casting shadows.",
                    new AcceptableValueRange<float>(5f, 50f)
                )
            );

            EnableDecorativeColliderOptimization = config.Bind(
                "5. Structural",
                "EnableDecorativeColliderOptimization",
                true,
                new ConfigDescription("Disable PhysX colliders on non-walkable decorative structural pieces (cross beams, ridge caps, trim elevated > 3.5m) to reduce physics scene query load. Automatically restores colliders when hammer or building tool is equipped.")
            );

            // [6. Cloth & Banners]
            EnableClothOptimization = config.Bind(
                "6. Cloth & Banners",
                "EnableClothOptimization",
                true,
                new ConfigDescription("Enable distance-based culling for Unity Cloth components on banners, flags, and tapestries to stop CPU physics simulations.")
            );

            BannerClothCullDistance = config.Bind(
                "6. Cloth & Banners",
                "BannerClothCullDistance",
                15.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which banner cloth physics solvers are disabled.",
                    new AcceptableValueRange<float>(5f, 60f)
                )
            );

            // [7. Storage & ItemStands]
            EnableItemStandOptimization = config.Bind(
                "7. Storage & ItemStands",
                "EnableItemStandOptimization",
                true,
                new ConfigDescription("Enable particle and light throttling on distant mounted trophies and weapons in item stands / armor stands.")
            );

            ItemStandCullDistance = config.Bind(
                "7. Storage & ItemStands",
                "ItemStandCullDistance",
                12.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which mounted item particles and light sources are culled.",
                    new AcceptableValueRange<float>(4f, 50f)
                )
            );

            // [8. Farm Animals]
            EnableTamedAnimalLod = config.Bind(
                "8. Farm Animals",
                "EnableTamedAnimalLod",
                true,
                new ConfigDescription("Enable animator LOD culling and idle AI throttling for distant tamed farm animals (boars, wolves, hens, loxes).")
            );

            TamedAnimalLodDistance = config.Bind(
                "8. Farm Animals",
                "TamedAnimalLodDistance",
                15.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which tamed animal animators switch to LOD culling mode.",
                    new AcceptableValueRange<float>(5f, 60f)
                )
            );

            // [9. Crafting Stations]
            EnableStationOptimization = config.Bind(
                "9. Crafting Stations",
                "EnableStationOptimization",
                true,
                new ConfigDescription("Enable particle and ambient audio culling on distant crafting, smelting, and processing stations.")
            );

            StationCullDistance = config.Bind(
                "9. Crafting Stations",
                "StationCullDistance",
                18.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which crafting station idle particles and looping audio effects are paused.",
                    new AcceptableValueRange<float>(5f, 60f)
                )
            );

            // [10. HUD & Benchmark]
            EnableBenchmarkHUD = config.Bind(
                "10. HUD & Benchmark",
                "EnableBenchmarkHUD",
                true,
                new ConfigDescription("Enable the real-time in-game benchmark overlay.")
            );

            BenchmarkHudHotkey = config.Bind(
                "10. HUD & Benchmark",
                "BenchmarkHudHotkey",
                KeyCode.F7,
                new ConfigDescription("Hotkey to toggle the benchmark HUD overlay.")
            );

            BenchmarkHudRequireShift = config.Bind(
                "10. HUD & Benchmark",
                "BenchmarkHudRequireShift",
                true,
                new ConfigDescription("Require holding Shift with the hotkey (Shift+F7) to toggle the benchmark HUD.")
            );

            HudTextSize = config.Bind(
                "10. HUD & Benchmark",
                "HudTextSize",
                13,
                new ConfigDescription(
                    "Font size for the benchmark HUD overlay text.",
                    new AcceptableValueRange<int>(10, 24)
                )
            );

            HudPosition = config.Bind(
                "10. HUD & Benchmark",
                "HudPosition",
                HudAnchorPosition.TopLeft,
                new ConfigDescription("Screen anchor position for the benchmark HUD overlay.")
            );

            // [11. Interior Occlusion]
            EnableInteriorCulling = config.Bind(
                "11. Interior Occlusion",
                "EnableInteriorCulling",
                true,
                new ConfigDescription("Cull rendering of enclosed interior base furniture and decor when player is outside base shelter.")
            );

            InteriorCullDistance = config.Bind(
                "11. Interior Occlusion",
                "InteriorCullDistance",
                18.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which interior furniture and pieces are culled when outside shelter.",
                    new AcceptableValueRange<float>(5f, 60f)
                )
            );

            // [12. Terrain Helpers]
            EnableTerrainSanityCheck = config.Bind(
                "12. Terrain Helpers",
                "EnableTerrainSanityCheck",
                true,
                new ConfigDescription("Detect legacy uncompressed terrain modification stacks in base zones and log compaction recommendations.")
            );
        }
    }
}
