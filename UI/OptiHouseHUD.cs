using System;
using System.Collections.Generic;
using System.Reflection;
using OptiHouse.Configuration;
using OptiHouse.Modules;
using UnityEngine;

namespace OptiHouse.UI
{
    /// <summary>
    /// Real-time in-game diagnostics and benchmark HUD overlay for OptiHouse.
    /// Provides live FPS, rolling Average FPS, 1% Low framerate, frame times,
    /// and live metrics across all base optimization subsystems.
    /// Toggled via Shift+F7 (configurable).
    /// </summary>
    public class OptiHouseHUD : MonoBehaviour
    {
        public static OptiHouseHUD? Instance { get; private set; }
        public static bool IsVisible { get; set; } = false;

        // Rolling frame timing buffer (180 samples = ~3s at 60 FPS)
        private const int FrameBufferSize = 180;
        private readonly float[] _frameTimeBuffer = new float[FrameBufferSize];
        private readonly float[] _sortedBuffer = new float[FrameBufferSize];
        private int _frameBufferIndex;
        private int _frameBufferCount;

        // Metric refresh timer (update display values 4 times per second for clean readability)
        private float _metricTimer;
        private float _liveFps;
        private float _avgFps;
        private float _onePercentLowFps;
        private float _avgFrameTimeMs;

        // Cached module metric counts
        private int _totalBasePieces;
        private int _activeShadowLights;
        private int _culledShadowLights;
        private int _totalShadowLights;
        private int _smokePuffs;
        private int _smokeCap = 25;
        private int _clothCulled;
        private int _clothTotal;
        private int _standsCulled;
        private int _standsTotal;
        private int _tamedLod;
        private int _tamedTotal;
        private int _stationsCulled;
        private int _stationsTotal;
        private int _interiorCulled;
        private int _interiorTotal;
        private int _collidersCulled;
        private int _collidersTotal;
        private int _detailCulled;
        private int _detailTotal;
        private int _droppedTotal;
        private int _droppedMerged;
        private int _droppedSleeping;
        private bool _gfxJobsActive;

        // IMGUI Drawing textures & styles
        private Texture2D? _bgTexture;
        private Texture2D? _borderTexture;
        private Texture2D? _headerTexture;
        private GUIStyle? _labelStyle;
        private GUIStyle? _titleStyle;
        private GUIStyle? _subTitleStyle;
        private GUIStyle? _dimStyle;
        private int _lastFontSize = -1;

        // Reflection caching for decoupled module interop
        private static readonly FieldInfo? _pieceListField =
            typeof(Piece).GetField("s_allPieces", BindingFlags.NonPublic | BindingFlags.Static);

        private void Awake()
        {
            Instance = this;
            InitTextures();
        }

        private void OnDestroy()
        {
            if (_bgTexture != null) Destroy(_bgTexture);
            if (_borderTexture != null) Destroy(_borderTexture);
            if (_headerTexture != null) Destroy(_headerTexture);
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void InitTextures()
        {
            // Dark sleek translucent panel
            _bgTexture = new Texture2D(1, 1);
            _bgTexture.SetPixel(0, 0, new Color(0.06f, 0.08f, 0.11f, 0.88f));
            _bgTexture.Apply();

            // Subtle modern border
            _borderTexture = new Texture2D(1, 1);
            _borderTexture.SetPixel(0, 0, new Color(0.22f, 0.32f, 0.44f, 0.95f));
            _borderTexture.Apply();

            // Distinct header band
            _headerTexture = new Texture2D(1, 1);
            _headerTexture.SetPixel(0, 0, new Color(0.11f, 0.15f, 0.22f, 0.95f));
            _headerTexture.Apply();
        }

        private void Update()
        {
            // Hotkey detection
            CheckToggleHotkey();

            // Sample unscaled frame delta times continuously
            float dt = Mathf.Max(0.00001f, Time.unscaledDeltaTime);
            _frameTimeBuffer[_frameBufferIndex] = dt;
            _frameBufferIndex = (_frameBufferIndex + 1) % FrameBufferSize;
            if (_frameBufferCount < FrameBufferSize)
            {
                _frameBufferCount++;
            }

            if (!IsVisible)
            {
                return;
            }

            // Refresh displayed metrics 4 times per second (0.25s)
            _metricTimer += dt;
            if (_metricTimer >= 0.25f)
            {
                _metricTimer = 0f;
                CalculateFramerateMetrics();
                CollectModuleMetrics();
            }
        }

        private void CheckToggleHotkey()
        {
            if (ModConfig.EnableBenchmarkHUD == null || !ModConfig.EnableBenchmarkHUD.Value)
            {
                if (IsVisible) IsVisible = false;
                return;
            }

            if (ModConfig.BenchmarkHudHotkey == null || ModConfig.BenchmarkHudHotkey.Value == KeyCode.None)
            {
                return;
            }

            if (Input.GetKeyDown(ModConfig.BenchmarkHudHotkey.Value))
            {
                bool requireShift = ModConfig.BenchmarkHudRequireShift != null && ModConfig.BenchmarkHudRequireShift.Value;
                bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (!requireShift || shiftHeld)
                {
                    IsVisible = !IsVisible;
                }
            }
        }

        private void CalculateFramerateMetrics()
        {
            if (_frameBufferCount == 0) return;

            // 1. Live FPS (average of recent 5 frames for clean readability)
            int sampleCount = Mathf.Min(5, _frameBufferCount);
            float recentSum = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                int idx = (_frameBufferIndex - 1 - i + FrameBufferSize) % FrameBufferSize;
                recentSum += _frameTimeBuffer[idx];
            }
            _liveFps = sampleCount / Mathf.Max(0.0001f, recentSum);

            // 2. Average FPS & Frame Time
            float totalDuration = 0f;
            for (int i = 0; i < _frameBufferCount; i++)
            {
                totalDuration += _frameTimeBuffer[i];
                _sortedBuffer[i] = _frameTimeBuffer[i];
            }
            _avgFps = _frameBufferCount / Mathf.Max(0.0001f, totalDuration);
            _avgFrameTimeMs = (totalDuration / _frameBufferCount) * 1000f;

            // 3. 1% Low FPS
            Array.Sort(_sortedBuffer, 0, _frameBufferCount);
            // In ascending frame times, slowest 1% are at index = count * 0.99
            int low1PercentIndex = Mathf.Clamp((int)(_frameBufferCount * 0.99f), 0, _frameBufferCount - 1);
            float low1PercentFrameTime = _sortedBuffer[low1PercentIndex];
            _onePercentLowFps = 1.0f / Mathf.Max(0.0001f, low1PercentFrameTime);
        }

        private void CollectModuleMetrics()
        {
            // 1. Base Pieces
            _totalBasePieces = GetTotalBasePieces();

            // 2. Light & Shadow
            _activeShadowLights = LightShadowOptimizer.ActiveShadowLights;
            _culledShadowLights = LightShadowOptimizer.CulledShadowLights;
            _totalShadowLights = LightShadowOptimizer.TotalLights;

            // 3. Smoke
            try
            {
                _smokePuffs = Smoke.GetTotalSmoke();
                _smokeCap = SmokeOptimizer.GetConfiguredSmokeCap();
            }
            catch
            {
                _smokePuffs = 0;
            }

            // 4. Banner Cloths
            try
            {
                _clothCulled = ClothOptimizer.CulledClothCount;
                _clothTotal = ClothOptimizer.TotalClothCount;
            }
            catch
            {
                _clothCulled = 0;
                _clothTotal = 0;
            }

            // 5. Item Stands & Trophies
            try
            {
                _standsCulled = ItemStandOptimizer.CulledStandCount;
                _standsTotal = ItemStandOptimizer.TotalStandCount;
            }
            catch
            {
                _standsCulled = 0;
                _standsTotal = 0;
            }

            // 6. Farm Animals LOD
            CollectTamedAnimalMetrics();

            // 7. Crafting Stations
            CollectCraftingStationMetrics();

            // 8. Interior Occlusion
            try
            {
                _interiorCulled = InteriorOcclusionOptimizer.CulledInteriorPieces;
                _interiorTotal = InteriorOcclusionOptimizer.TotalInteriorPieces;
            }
            catch
            {
                _interiorCulled = 0;
                _interiorTotal = 0;
            }

            // 9. Decorative Colliders
            try
            {
                _collidersCulled = DecorativeColliderOptimizer.CulledColliderCount;
                _collidersTotal = DecorativeColliderOptimizer.TotalDecorativePieces;
            }
            catch
            {
                _collidersCulled = 0;
                _collidersTotal = 0;
            }

            // 10. Detail Piece LOD
            try
            {
                _detailCulled = DetailPieceLOD.CulledDetailPieces;
                _detailTotal = DetailPieceLOD.TotalDetailPieces;
            }
            catch
            {
                _detailCulled = 0;
                _detailTotal = 0;
            }

            // 11. Dropped Items & Loot Clutter
            try
            {
                _droppedTotal = ItemDropOptimizer.TotalDroppedItems;
                _droppedMerged = ItemDropOptimizer.MergedDropsCount;
                _droppedSleeping = ItemDropOptimizer.SleepingItemsCount;
            }
            catch
            {
                _droppedTotal = 0;
                _droppedMerged = 0;
                _droppedSleeping = 0;
            }

            // 12. Multi-Threaded Gfx Jobs
            try
            {
                _gfxJobsActive = BootConfigTweaker.IsGfxJobsActive;
            }
            catch
            {
                _gfxJobsActive = false;
            }
        }

        private static int GetTotalBasePieces()
        {
            try
            {
                if (_pieceListField?.GetValue(null) is List<Piece> list)
                {
                    return list.Count;
                }
            }
            catch { }
            return PieceShadowCuller.TrackedPieceCount;
        }

        private void CollectTamedAnimalMetrics()
        {
            try
            {
                _tamedTotal = TamedAnimalOptimizer.TotalTamedAnimals;
                _tamedLod = TamedAnimalOptimizer.CulledTamedAnimals;
            }
            catch
            {
                _tamedTotal = 0;
                _tamedLod = 0;
            }
        }

        private void CollectCraftingStationMetrics()
        {
            try
            {
                _stationsTotal = CraftingStationOptimizer.TotalStations;
                _stationsCulled = CraftingStationOptimizer.CulledStations;
            }
            catch
            {
                _stationsTotal = 0;
                _stationsCulled = 0;
            }
        }

        private void OnGUI()
        {
            if (!IsVisible) return;

            int currentFontSize = ModConfig.HudTextSize != null ? ModConfig.HudTextSize.Value : 13;
            UpdateStyles(currentFontSize);

            float width = 380f;
            float height = 545f;
            float margin = 16f;

            HudAnchorPosition anchor = ModConfig.HudPosition != null ? ModConfig.HudPosition.Value : HudAnchorPosition.TopLeft;
            float x = margin;
            float y = margin;

            switch (anchor)
            {
                case HudAnchorPosition.TopRight:
                    x = Screen.width - width - margin;
                    y = margin;
                    break;
                case HudAnchorPosition.BottomLeft:
                    x = margin;
                    y = Screen.height - height - margin;
                    break;
                case HudAnchorPosition.BottomRight:
                    x = Screen.width - width - margin;
                    y = Screen.height - height - margin;
                    break;
                case HudAnchorPosition.TopLeft:
                default:
                    x = margin;
                    y = margin;
                    break;
            }

            Rect panelRect = new Rect(x, y, width, height);

            // 1. Draw sleek bordered background panel
            if (_borderTexture != null)
            {
                GUI.DrawTexture(panelRect, _borderTexture);
            }
            if (_bgTexture != null)
            {
                Rect innerRect = new Rect(panelRect.x + 1f, panelRect.y + 1f, panelRect.width - 2f, panelRect.height - 2f);
                GUI.DrawTexture(innerRect, _bgTexture);
            }

            // 2. Draw styled header band
            Rect headerRect = new Rect(panelRect.x + 1f, panelRect.y + 1f, panelRect.width - 2f, 32f);
            if (_headerTexture != null)
            {
                GUI.DrawTexture(headerRect, _headerTexture);
            }

            // 3. Render content within panel
            GUILayout.BeginArea(new Rect(panelRect.x + 12f, panelRect.y + 7f, panelRect.width - 24f, panelRect.height - 14f));

            // Header line: Title + Status
            bool isOptimizing = OptiHousePlugin.IsOptimizationActive;
            string statusStr = isOptimizing
                ? "<color=#00FF66><b>[OptiHouse: ACTIVE]</b></color>"
                : "<color=#FF4444><b>[OptiHouse: DISABLED]</b></color>";

            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#55CCFF><b>⚡ OptiHouse v1.4.1</b></color>", _titleStyle!);
            GUILayout.FlexibleSpace();
            GUILayout.Label(statusStr, _subTitleStyle!);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            // Framerate & Timing Benchmark
            string lowColor = _onePercentLowFps >= 45f ? "#00FF66" : (_onePercentLowFps >= 30f ? "#FFFF55" : "#FF5555");
            string fpsColor = _liveFps >= 55f ? "#00FF66" : (_liveFps >= 35f ? "#FFFF55" : "#FF5555");

            GUILayout.Label(
                $"<b>FPS:</b> <color={fpsColor}>{_liveFps:F1}</color>  |  <b>Avg:</b> {_avgFps:F1}  |  <b>1% Low:</b> <color={lowColor}>{_onePercentLowFps:F1}</color>",
                _labelStyle!
            );
            GUILayout.Label($"<b>Frametime:</b> {_avgFrameTimeMs:F1} ms", _labelStyle!);

            GUILayout.Label("<color=#2F4156>────────────────────────────────────────────</color>", _dimStyle!);

            // Base & Subsystem Metrics
            GUILayout.Label($"🏛️  Base Pieces: <b>{_totalBasePieces:N0}</b>", _labelStyle!);

            GUILayout.Label(
                $"💡  Shadow Lights: <color=#00FF66><b>{_activeShadowLights}</b> active</color> / <color=#FFAA44><b>{_culledShadowLights}</b> culled</color> ({_totalShadowLights} total)",
                _labelStyle!
            );

            GUILayout.Label(
                $"💨  Smoke Puffs: <b>{_smokePuffs}</b> active / {_smokeCap} cap",
                _labelStyle!
            );

            string particleStatus = (ModConfig.EnableFastParticleFade != null && ModConfig.EnableFastParticleFade.Value)
                ? "<color=#00FF66>Active (~3x faster fade)</color>"
                : "<color=#FFAA44>Vanilla</color>";
            GUILayout.Label($"✨  Particle Fade: {particleStatus}", _labelStyle!);

            GUILayout.Label(
                $"🚩  Banner Cloths: <color=#00FF66><b>{_clothCulled}</b> culled</color> / {_clothTotal} total",
                _labelStyle!
            );

            GUILayout.Label(
                $"📦  Item & Trophy Stands: <color=#00FF66><b>{_standsCulled}</b> culled</color> / {_standsTotal} total",
                _labelStyle!
            );

            GUILayout.Label(
                $"🐗  Tamed Animals: <color=#00FF66><b>{_tamedLod}</b> in LOD</color> / {_tamedTotal} total",
                _labelStyle!
            );

            GUILayout.Label(
                $"🔨  Crafting Stations: <color=#00FF66><b>{_stationsCulled}</b> culled</color> / {_stationsTotal} total",
                _labelStyle!
            );

            GUILayout.Label(
                $"🚪  Interior Culling: <color=#00FF66><b>{_interiorCulled}</b> culled</color> / {_interiorTotal} total",
                _labelStyle!
            );

            GUILayout.Label(
                $"📐  Detail Pieces LOD: <color=#00FF66><b>{_detailCulled}</b> culled</color> / {_detailTotal} total",
                _labelStyle!
            );

            GUILayout.Label(
                $"💎  Ground Loot Clutter: <b>{_droppedTotal}</b> (<color=#00FF66><b>{_droppedSleeping}</b> sleep</color>, <color=#55CCFF><b>{_droppedMerged}</b> merged</color>)",
                _labelStyle!
            );

            string gfxJobsText = _gfxJobsActive
                ? "<color=#00FF66>Active (Multi-Threaded)</color>"
                : (BootConfigTweaker.RestartRequired
                    ? "<color=#FFFF55>Pending Restart</color>"
                    : "<color=#FFAA44>Inactive</color>");
            GUILayout.Label($"⚡  Gfx Jobs: {gfxJobsText}", _labelStyle!);

            GUILayout.Label("<color=#2F4156>────────────────────────────────────────────</color>", _dimStyle!);

            // Footer controls notice
            GUILayout.Label(
                "<size=10><color=#7E94A8><b>[Shift+F7]</b> Toggle HUD  |  <b>[F7]</b> Toggle Optimizations</color></size>",
                _dimStyle!
            );
            GUILayout.Label(
                "<size=9><color=#FFAA44>Special thanks to <b>DaymonMontage</b> (twitch.tv/daymonmontage)</color></size>",
                _dimStyle!
            );

            GUILayout.EndArea();
        }

        private void UpdateStyles(int fontSize)
        {
            if (_labelStyle != null && _lastFontSize == fontSize) return;

            _lastFontSize = fontSize;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                richText = true,
                wordWrap = false
            };
            _labelStyle.normal.textColor = Color.white;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize + 1,
                richText = true,
                wordWrap = false
            };
            _titleStyle.normal.textColor = Color.white;

            _subTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                richText = true,
                wordWrap = false
            };
            _subTitleStyle.normal.textColor = Color.white;

            _dimStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize - 1,
                richText = true,
                wordWrap = false
            };
            _dimStyle.normal.textColor = new Color(0.6f, 0.7f, 0.8f, 1f);
        }
    }
}
