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
        // 1. General & Engine
        public static ConfigEntry<bool> MasterOptimizationEnabled = null!;
        public static ConfigEntry<KeyCode> ToggleHotkey = null!;
        public static ConfigEntry<bool> ShowNotificationOnToggle = null!;
        public static ConfigEntry<bool> EnableLogging = null!;
        public static ConfigEntry<bool> EnableGpuInstancing = null!;
        public static ConfigEntry<bool> FixMaterialManInstancingBug = null!;
        public static ConfigEntry<bool> StripPieceRandomMaterialBlocks = null!;
        public static ConfigEntry<bool> OptimizeReflectionProbes = null!;
        public static ConfigEntry<float> ReflectionUpdateInterval = null!;
        public static ConfigEntry<bool> OptimizeZSyncTransform = null!;
        public static ConfigEntry<bool> EnableOffscreenParticleCulling = null!;
        public static ConfigEntry<bool> EnableAmbientAudioCulling = null!;
        public static ConfigEntry<bool> EnableGfxJobsTweak = null!;

        // 2. Lighting
        public static ConfigEntry<bool> EnableLightOptimization = null!;
        public static ConfigEntry<int> MaxActivePointLights = null!;
        public static ConfigEntry<int> MaxShadowCastingLights = null!;
        public static ConfigEntry<bool> EnableAdaptiveShadowCascades = null!;
        public static ConfigEntry<int> MegaBaseCascadeThreshold = null!;
        public static ConfigEntry<bool> DisableTorchShadows = null!;
        public static ConfigEntry<float> LightCullDistance = null!;
        public static ConfigEntry<bool> ThrottleFireplaceEnvChecks = null!;
        public static ConfigEntry<bool> CullDistantLightFlicker = null!;

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
        public static ConfigEntry<bool> EnableSupportEquilibriumCache = null!;
        public static ConfigEntry<bool> EnableWearNTearThrottling = null!;
        public static ConfigEntry<float> WearNTearThrottleInterval = null!;
        public static ConfigEntry<bool> SkipNonAshlandsMaterialUpdates = null!;
        public static ConfigEntry<bool> EnablePieceShadowCulling = null!;
        public static ConfigEntry<float> PieceShadowCullDistance = null!;

        // 6. Cloth & Banners
        public static ConfigEntry<bool> EnableClothOptimization = null!;
        public static ConfigEntry<float> BannerClothCullDistance = null!;

        // 7. Storage, Signs & ItemStands
        public static ConfigEntry<bool> EnableItemStandOptimization = null!;
        public static ConfigEntry<float> ItemStandCullDistance = null!;
        public static ConfigEntry<bool> EnableSignOptimization = null!;
        public static ConfigEntry<float> SignCullDistance = null!;

        // 8. Farm Animals & Turrets
        public static ConfigEntry<bool> EnableTamedAnimalLod = null!;
        public static ConfigEntry<float> TamedAnimalLodDistance = null!;
        public static ConfigEntry<bool> EnableTurretOptimization = null!;

        // 9. Crafting Stations & Beehives
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
        public static ConfigEntry<bool> EnableSmartInteriorCulling = null!;

        // 12. Terrain & Clutter Helpers
        public static ConfigEntry<bool> EnableTerrainSanityCheck = null!;
        public static ConfigEntry<bool> SuppressUnderFloorGrass = null!;

        // 13. Item Drops & Loot Clutter
        public static ConfigEntry<bool> EnableItemDropOptimization = null!;
        public static ConfigEntry<bool> EnableDroppedItemPhysicsSleep = null!;
        public static ConfigEntry<float> DroppedItemVfxCullDistance = null!;

        // 14. Structural Detail LOD
        public static ConfigEntry<bool> EnableDetailPieceLOD = null!;
        public static ConfigEntry<float> DetailPieceCullDistance = null!;
        public static ConfigEntry<float> DetailPieceRestoreDistance = null!;

        public static void Init(ConfigFile config)
        {
            // [1. General]
            MasterOptimizationEnabled = config.Bind(
                "1. General",
                "MasterOptimizationEnabled",
                true,
                new ConfigDescription("Master switch for all OptiHouse optimizations. Can also be toggled in-game with F7.")
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

            EnableGpuInstancing = config.Bind(
                "1. General",
                "EnableGpuInstancing",
                true,
                new ConfigDescription("Automatically enables GPU Instancing on shared prefab materials across pieces, trees, rocks, and props to batch identical meshes into single DrawMeshInstanced calls.")
            );

            FixMaterialManInstancingBug = config.Bind(
                "1. General",
                "FixMaterialManInstancingBug",
                true,
                new ConfigDescription("Fixes Valheim's MaterialMan.UpdateBlock bug where cleared Hammer highlights leave an empty C++ MaterialPropertyBlock attached to renderers, permanently breaking GPU Instancing.")
            );

            StripPieceRandomMaterialBlocks = config.Bind(
                "1. General",
                "StripPieceRandomMaterialBlocks",
                true,
                new ConfigDescription("Skips per-block RandomMaterialValues (RandMatSeed) MaterialPropertyBlocks on Grausten, Black Marble, and Ashwood pieces so entire stone/marble castles batch via DrawMeshInstanced.")
            );

            OptimizeReflectionProbes = config.Bind(
                "1. General",
                "OptimizeReflectionProbes",
                true,
                new ConfigDescription("Time-slices ReflectionUpdate 6-face cubemap renders across 9 frames, strips probe shadows/micro-layers, and throttles stationary re-renders to eliminate the 3-second stutter spike.")
            );

            ReflectionUpdateInterval = config.Bind(
                "1. General",
                "ReflectionUpdateInterval",
                14.0f,
                new ConfigDescription(
                    "Interval in seconds between environment reflection cubemap renders when stationary inside a base (vanilla default is 3.0s).",
                    new AcceptableValueRange<float>(3f, 60f)
                )
            );

            OptimizeZSyncTransform = config.Bind(
                "1. General",
                "OptimizeZSyncTransform",
                true,
                new ConfigDescription("Throttles redundant per-frame C++ physics/ZDO sync checks on physically sleeping ZSyncTransform Rigidbodies and unchanged distant network objects.")
            );

            EnableOffscreenParticleCulling = config.Bind(
                "1. General",
                "EnableOffscreenParticleCulling",
                true,
                new ConfigDescription("Configures local-space looping ParticleSystems on prefabs to pause natively on C++ worker threads when off-screen.")
            );

            EnableAmbientAudioCulling = config.Bind(
                "1. General",
                "EnableAmbientAudioCulling",
                true,
                new ConfigDescription("Skips per-frame ZSFX audio reverb/distance math on out-of-range looping sounds (fires, portals, torches).")
            );

            EnableGfxJobsTweak = config.Bind(
                "1. General",
                "EnableGfxJobsTweak",
                false,
                new ConfigDescription("Opt-in: injects gfx-enable-native-gfx-jobs=1 into valheim_Data/boot.config (creates boot.config.bak). Requires game restart.")
            );

            // [2. Lighting]
            EnableLightOptimization = config.Bind(
                "2. Lighting",
                "EnableLightOptimization",
                true,
                new ConfigDescription("Enable dynamic point light caps, shadow budget, flicker frustum/distance culling, and fireplace raycast throttling.")
            );

            MaxActivePointLights = config.Bind(
                "2. Lighting",
                "MaxActivePointLights",
                28,
                new ConfigDescription(
                    "Maximum active point lights via LightLod.m_lightLimit (0 = vanilla unlimited). Cuts multi-pass ForwardAdd draw calls in torch-lit bases.",
                    new AcceptableValueRange<int>(0, 128)
                )
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

            EnableAdaptiveShadowCascades = config.Bind(
                "2. Lighting",
                "EnableAdaptiveShadowCascades",
                true,
                new ConfigDescription("Automatically drops directional sun shadow cascades from 4 to 2 when inside a dense mega-base (cutting sun shadow draw calls by 50%), restoring 4 cascades upon leaving.")
            );

            MegaBaseCascadeThreshold = config.Bind(
                "2. Lighting",
                "MegaBaseCascadeThreshold",
                2500,
                new ConfigDescription(
                    "Minimum loaded building piece count to trigger 2-cascade mega-base directional shadow mode.",
                    new AcceptableValueRange<int>(500, 20000)
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
                    "Distance (in meters) at which small interior base lights fade out.",
                    new AcceptableValueRange<float>(10f, 100f)
                )
            );

            ThrottleFireplaceEnvChecks = config.Bind(
                "2. Lighting",
                "ThrottleFireplaceEnvChecks",
                true,
                new ConfigDescription("Throttle periodic Fireplace.CheckEnv terrain/cover physics raycasts from 4s to 20s on settled torches and hearths.")
            );

            CullDistantLightFlicker = config.Bind(
                "2. Lighting",
                "CullDistantLightFlicker",
                true,
                new ConfigDescription("Pause CPU LightFlicker sin/cos & transform jitter on off-screen or distant (>20m) lights.")
            );

            // [3. VFX]
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
                new ConfigDescription("Suppress heavy debris chunk bursts and wood/stone dust particle spam when demolishing building pieces.")
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
                new ConfigDescription("Accelerates the dissipation of temporary particles (debris, dust, smoke, sparks) once per effect.")
            );

            ParticleLifetimeMultiplier = config.Bind(
                "3. VFX",
                "ParticleLifetimeMultiplier",
                0.45f,
                new ConfigDescription(
                    "Multiplier for temporary particle lifetimes.",
                    new AcceptableValueRange<float>(0.15f, 1.0f)
                )
            );

            MaxParticleLifetimeSeconds = config.Bind(
                "3. VFX",
                "MaxParticleLifetimeSeconds",
                2.5f,
                new ConfigDescription(
                    "Hard cap in seconds for the maximum lifetime of temporary effect bursts.",
                    new AcceptableValueRange<float>(0.5f, 10.0f)
                )
            );

            ParticleSimulationSpeed = config.Bind(
                "3. VFX",
                "ParticleSimulationSpeed",
                1.5f,
                new ConfigDescription(
                    "Speed multiplier for particle animations.",
                    new AcceptableValueRange<float>(1.0f, 3.0f)
                )
            );

            // [4. Smoke]
            EnableSmokeOptimization = config.Bind(
                "4. Smoke",
                "EnableSmokeOptimization",
                true,
                new ConfigDescription("Enable indoor smoke puff cap, spawner raycast caching, and fast fadeout.")
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
                new ConfigDescription("Throttle indoor ceiling collision checks for SmokeSpawner and fading smoke puffs.")
            );

            DisableSmokeRendering = config.Bind(
                "4. Smoke",
                "DisableSmokeRendering",
                false,
                new ConfigDescription("Disable visual rendering of smoke particles while keeping warmth, fire, and comfort mechanics intact.")
            );

            SmokeLifetimeMultiplier = config.Bind(
                "4. Smoke",
                "SmokeLifetimeMultiplier",
                0.4f,
                new ConfigDescription(
                    "Multiplier for chimney and hearth smoke puff lifetime (TTL).",
                    new AcceptableValueRange<float>(0.15f, 1.0f)
                )
            );

            // [5. Structural]
            EnableStructuralOptimization = config.Bind(
                "5. Structural",
                "EnableStructuralOptimization",
                true,
                new ConfigDescription("Enable structural support equilibrium cache, shadow culling, WearNTear cover throttling, and Ashlands shader loop skipping. Never touches colliders.")
            );

            EnableSupportEquilibriumCache = config.Bind(
                "5. Structural",
                "EnableSupportEquilibriumCache",
                true,
                new ConfigDescription("Fixes Valheim's WearNTear.UpdateSupport ground/foundation cache-miss bug by skipping redundant Physics.OverlapBoxNonAlloc sweeps once base support reaches equilibrium (instantly invalidates on any build/destroy/terrain/rock/tree change).")
            );

            EnableWearNTearThrottling = config.Bind(
                "5. Structural",
                "EnableWearNTearThrottling",
                true,
                new ConfigDescription("Throttle periodic rain/weather roof SphereCastNonAlloc checks (UpdateCover) on intact building pieces.")
            );

            WearNTearThrottleInterval = config.Bind(
                "5. Structural",
                "WearNTearThrottleInterval",
                10.0f,
                new ConfigDescription(
                    "Minimum real seconds between weather cover raycasts for intact pieces during rain/storms.",
                    new AcceptableValueRange<float>(4f, 30f)
                )
            );

            SkipNonAshlandsMaterialUpdates = config.Bind(
                "5. Structural",
                "SkipNonAshlandsMaterialUpdates",
                true,
                new ConfigDescription("Skips WearNTearUpdater's per-second UpdateAshlandsMaterialValues iteration on all non-Ashlands pieces with zero burn/lava timers.")
            );

            EnablePieceShadowCulling = config.Bind(
                "5. Structural",
                "EnablePieceShadowCulling",
                true,
                new ConfigDescription("Disable shadow casting on small structural/prop elements beyond the culling distance.")
            );

            PieceShadowCullDistance = config.Bind(
                "5. Structural",
                "PieceShadowCullDistance",
                18.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which small structural pieces stop casting shadows.",
                    new AcceptableValueRange<float>(5f, 50f)
                )
            );

            // [6. Cloth & Banners]
            EnableClothOptimization = config.Bind(
                "6. Cloth & Banners",
                "EnableClothOptimization",
                true,
                new ConfigDescription("Pause Unity Cloth vertex physics simulation on off-screen or distant banners, flags, and tapestries.")
            );

            BannerClothCullDistance = config.Bind(
                "6. Cloth & Banners",
                "BannerClothCullDistance",
                16.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which banner cloth physics solvers are disabled.",
                    new AcceptableValueRange<float>(5f, 60f)
                )
            );

            // [7. Storage, Signs & ItemStands]
            EnableItemStandOptimization = config.Bind(
                "7. Storage & ItemStands",
                "EnableItemStandOptimization",
                true,
                new ConfigDescription("Pause particles and lights on off-screen or distant mounted trophies and weapons.")
            );

            ItemStandCullDistance = config.Bind(
                "7. Storage & ItemStands",
                "ItemStandCullDistance",
                15.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which mounted item particles and light sources are culled.",
                    new AcceptableValueRange<float>(4f, 50f)
                )
            );

            EnableSignOptimization = config.Bind(
                "7. Storage & ItemStands",
                "EnableSignOptimization",
                true,
                new ConfigDescription("Culls unreadable distant/off-screen Sign world-space Canvases (TextMeshPro) and stops Valheim's 2-second UGC permission check spam on unchanged signs.")
            );

            SignCullDistance = config.Bind(
                "7. Storage & ItemStands",
                "SignCullDistance",
                16.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which Sign UI Canvases are hidden.",
                    new AcceptableValueRange<float>(5f, 50f)
                )
            );

            // [8. Farm Animals & Turrets]
            EnableTamedAnimalLod = config.Bind(
                "8. Farm Animals",
                "EnableTamedAnimalLod",
                true,
                new ConfigDescription("Enable Animator LOD culling and idle AI throttling for distant calm tamed farm animals.")
            );

            TamedAnimalLodDistance = config.Bind(
                "8. Farm Animals",
                "TamedAnimalLodDistance",
                18.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which tamed animal animators switch to CullUpdateTransforms.",
                    new AcceptableValueRange<float>(5f, 60f)
                )
            );

            EnableTurretOptimization = config.Bind(
                "8. Farm Animals",
                "EnableTurretOptimization",
                true,
                new ConfigDescription("Throttles 50 Hz FixedUpdate idle visual scan rotation on distant targetless ballistas while preserving exact target-detection intervals.")
            );

            // [9. Crafting Stations]
            EnableStationOptimization = config.Bind(
                "9. Crafting Stations",
                "EnableStationOptimization",
                true,
                new ConfigDescription("Pause idle particle emissions and looping ambient audio on distant crafting stations, smelters, kilns, windmills, and beehives.")
            );

            StationCullDistance = config.Bind(
                "9. Crafting Stations",
                "StationCullDistance",
                20.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which station idle particles and looping audio effects are paused.",
                    new AcceptableValueRange<float>(5f, 60f)
                )
            );

            // [10. HUD & Benchmark]
            EnableBenchmarkHUD = config.Bind(
                "10. HUD & Benchmark",
                "EnableBenchmarkHUD",
                true,
                new ConfigDescription("Enable the real-time zero-GC in-game benchmark overlay.")
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
                new ConfigDescription("Cull rendering of distant enclosed interior furniture/chests/decor (never hides fireplaces, light fixtures, walls, or roofs).")
            );

            InteriorCullDistance = config.Bind(
                "11. Interior Occlusion",
                "InteriorCullDistance",
                26.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which enclosed interior furniture and chests are culled.",
                    new AcceptableValueRange<float>(8f, 80f)
                )
            );

            EnableSmartInteriorCulling = config.Bind(
                "11. Interior Occlusion",
                "EnableSmartInteriorCulling",
                true,
                new ConfigDescription("Keeps distant enclosed rooms culled beyond InteriorCullDistance even when player is under another roof.")
            );

            // [12. Terrain Helpers]
            EnableTerrainSanityCheck = config.Bind(
                "12. Terrain Helpers",
                "EnableTerrainSanityCheck",
                true,
                new ConfigDescription("Detect legacy uncompressed terrain modification stacks in base zones.")
            );

            SuppressUnderFloorGrass = config.Bind(
                "12. Terrain Helpers",
                "SuppressUnderFloorGrass",
                true,
                new ConfigDescription("Prevents ClutterSystem from spawning and rendering invisible/clipping grass tufts directly beneath ground-level base floors and stone foundations.")
            );

            // [13. Item Drops & Loot Clutter]
            EnableItemDropOptimization = config.Bind(
                "13. Item Drops & Loot",
                "EnableItemDropOptimization",
                true,
                new ConfigDescription("Put motionless ground item Rigidbodies to sleep and pause distant sparkle VFX (zero item modification/deletion).")
            );

            EnableDroppedItemPhysicsSleep = config.Bind(
                "13. Item Drops & Loot",
                "EnableDroppedItemPhysicsSleep",
                true,
                new ConfigDescription("Forces PhysX Rigidbody to sleep on motionless ground items to stop continuous collision checks.")
            );

            DroppedItemVfxCullDistance = config.Bind(
                "13. Item Drops & Loot",
                "DroppedItemVfxCullDistance",
                18.0f,
                new ConfigDescription(
                    "Distance beyond which item drop sparkle VFX are paused.",
                    new AcceptableValueRange<float>(5f, 50f)
                )
            );

            // [14. Structural Detail LOD]
            EnableDetailPieceLOD = config.Bind(
                "14. Structural Detail LOD",
                "EnableDetailPieceLOD",
                true,
                new ConfigDescription("Culls renderers on purely ornamental micro-pieces (crosses, carvings, dragonheads, 1m poles/beams) beyond distance. Never touches walls or roof tiles.")
            );

            DetailPieceCullDistance = config.Bind(
                "14. Structural Detail LOD",
                "DetailPieceCullDistance",
                38.0f,
                new ConfigDescription(
                    "Distance (in meters) beyond which purely ornamental micro-details are culled.",
                    new AcceptableValueRange<float>(12f, 90f)
                )
            );

            DetailPieceRestoreDistance = config.Bind(
                "14. Structural Detail LOD",
                "DetailPieceRestoreDistance",
                30.0f,
                new ConfigDescription(
                    "Distance (in meters) at which small ornamental details are restored (hysteresis margin).",
                    new AcceptableValueRange<float>(8f, 85f)
                )
            );
        }
    }
}
