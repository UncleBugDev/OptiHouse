using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using OptiHouse.Configuration;
using OptiHouse.Modules;
using OptiHouse.UI;
using UnityEngine;

namespace OptiHouse
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class OptiHousePlugin : BaseUnityPlugin
    {
        public const string ModGUID = "com.unclebug.optihouse";
        public const string ModName = "OptiHouse";
        public const string ModVersion = "2.2.0";

        public static OptiHousePlugin Instance { get; private set; } = null!;
        public static ManualLogSource Log => Instance.Logger;

        public static bool IsOptimizationActive =>
            ModConfig.MasterOptimizationEnabled != null && ModConfig.MasterOptimizationEnabled.Value;

        private Harmony _harmony = null!;

        private void Awake()
        {
            Instance = this;

            // 1. Initialize configuration bindings
            ModConfig.Init(Config);

            // 2. Inspect/apply engine boot.config settings (strictly opt-in)
            BootConfigTweaker.Initialize();

            // 3. Apply all Harmony patches first
            _harmony = new Harmony(ModGUID);
            _harmony.PatchAll();

            // 4. Attach zero-GC benchmark HUD and all standalone optimization subsystems
            gameObject.AddComponent<OptiHouseHUD>();
            gameObject.AddComponent<CameraLayerCullOptimizer>();
            gameObject.AddComponent<ReflectionProbeOptimizer>();
            gameObject.AddComponent<LightShadowOptimizer>();
            gameObject.AddComponent<PieceShadowCuller>();
            gameObject.AddComponent<ClothOptimizer>();
            gameObject.AddComponent<ItemStandOptimizer>();
            gameObject.AddComponent<SignOptimizer>();
            gameObject.AddComponent<TamedAnimalOptimizer>();
            gameObject.AddComponent<CraftingStationOptimizer>();
            gameObject.AddComponent<InteriorOcclusionOptimizer>();
            gameObject.AddComponent<DetailPieceLOD>();
            gameObject.AddComponent<ItemDropOptimizer>();
            gameObject.AddComponent<FarmPlantOptimizer>();
            gameObject.AddComponent<ClutterOptimizer>();
            gameObject.AddComponent<TerrainOptimizationHelper>();

            // 5. Scan any pre-existing scene pieces/prefabs/camera if hot-reloaded mid-session
            PieceRegistry.ScanExistingScenePieces();
            if (ZNetScene.instance != null)
            {
                PrefabInstancingOptimizer.OptimizeAllPrefabs(ZNetScene.instance);
            }
            if (GameCamera.instance != null)
            {
                CameraLayerCullOptimizer.ApplyToGameCamera(GameCamera.instance);
            }

            Log.LogInfo($"[{ModName}] v{ModVersion} Standalone Engine & Base Edition initialized! Special thanks to DaymonMontage (https://twitch.tv/daymonmontage)!");
        }

        private void Update()
        {
            if (ModConfig.ToggleHotkey != null &&
                ModConfig.ToggleHotkey.Value != KeyCode.None &&
                Input.GetKeyDown(ModConfig.ToggleHotkey.Value))
            {
                bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool hudUsesSameKeyWithShift = ModConfig.BenchmarkHudHotkey != null &&
                                               ModConfig.BenchmarkHudHotkey.Value == ModConfig.ToggleHotkey.Value &&
                                               ModConfig.BenchmarkHudRequireShift != null &&
                                               ModConfig.BenchmarkHudRequireShift.Value;

                if (!shiftHeld || !hudUsesSameKeyWithShift)
                {
                    ToggleOptimizations();
                }
            }
        }

        public void ToggleOptimizations()
        {
            bool newState = !ModConfig.MasterOptimizationEnabled.Value;
            ModConfig.MasterOptimizationEnabled.Value = newState;

            string statusMsg = newState
                ? "<color=#00FF66><b>OPTIMIZATIONS ENABLED</b></color>"
                : "<color=#FF4444><b>OPTIMIZATIONS DISABLED</b></color>";

            if (ModConfig.ShowNotificationOnToggle.Value && MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"OptiHouse: {statusMsg}");
            }

            if (ModConfig.EnableLogging.Value)
            {
                Log.LogInfo($"[{ModName}] Optimizations runtime toggle: {(newState ? "ENABLED" : "DISABLED")}");
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
