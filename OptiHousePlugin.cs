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
        public const string ModVersion = "1.4.0";

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

            // 2. Initialize boot.config engine tweaks (multi-threaded gfx jobs)
            BootConfigTweaker.Initialize();

            // 3. Initialize real-time diagnostics and benchmark HUD
            gameObject.AddComponent<OptiHouseHUD>();

            // 4. Initialize optimization component modules
            gameObject.AddComponent<InteriorOcclusionOptimizer>();
            gameObject.AddComponent<DecorativeColliderOptimizer>();
            gameObject.AddComponent<DetailPieceLOD>();
            gameObject.AddComponent<ItemDropOptimizer>();
            gameObject.AddComponent<TerrainOptimizationHelper>();

            // 5. Initialize Harmony patches
            _harmony = new Harmony(ModGUID);
            _harmony.PatchAll();

            Log.LogInfo($"[{ModName}] v{ModVersion} initialized successfully. Special thanks to DaymonMontage (https://twitch.tv/daymonmontage)!");
        }

        private void Update()
        {
            if (ModConfig.ToggleHotkey != null &&
                ModConfig.ToggleHotkey.Value != KeyCode.None &&
                Input.GetKeyDown(ModConfig.ToggleHotkey.Value))
            {
                // If Shift+Hotkey is pressed and HUD hotkey uses the same key with RequireShift,
                // do not trigger master optimization toggle
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
