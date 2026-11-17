using System;
using System.Collections.Generic;
using System.Reflection;
using OptiHouse.Configuration;
using OptiHouse.Modules;
using UnityEngine;

namespace OptiHouse.UI
{
    /// <summary>
    /// Zero-GC Real-time In-Game Diagnostics &amp; Benchmark HUD Overlay for OptiHouse v2.0.
    /// Pre-formats all strings once every 0.25s inside CollectModuleMetrics() so OnGUI() allocates 0 bytes per frame.
    /// Toggled via Shift+F7 (configurable).
    /// </summary>
    public class OptiHouseHUD : MonoBehaviour
    {
        public static OptiHouseHUD? Instance { get; private set; }
        public static bool IsVisible { get; set; } = false;

        private const int FrameBufferSize = 180;
        private readonly float[] _frameTimeBuffer = new float[FrameBufferSize];
        private readonly float[] _sortedBuffer = new float[FrameBufferSize];
        private int _frameBufferIndex;
        private int _frameBufferCount;

        private float _metricTimer;
        private float _liveFps;
        private float _avgFps;
        private float _onePercentLowFps;
        private float _avgFrameTimeMs;

        // Pre-formatted zero-GC HUD strings (updated at 4 Hz only)
        private string _titleLine = $"<color=#55CCFF><b>⚡ OptiHouse v{OptiHousePlugin.ModVersion}</b></color>";
        private string _statusLine = "<color=#00FF66><b>[ACTIVE]</b></color>";
        private string _fpsLine = "<b>FPS:</b> --";
        private string _frametimeLine = "<b>Frametime:</b> -- ms";
        private string _instancingLine = "🧊  GPU Instanced Mats: <b>0</b>";
        private string _lightsLine = "💡  Point Lights: <b>0</b>";
        private string _propShadowsLine = "🌒  Prop Shadows: <b>0</b>";
        private string _supportLine = "🏛️  WNT Support Cache: <b>0</b>";
        private string _signsLine = "🪧  Warehouse Signs: <b>0</b>";
        private string _smokeLine = "💨  Smoke Puffs: <b>0</b>";
        private string _clothLine = "🚩  Banner Cloths: <b>0</b>";
        private string _standsLine = "📦  Item & Trophy Stands: <b>0</b>";
        private string _tamedLine = "🐗  Farm & Turrets: <b>0</b>";
        private string _stationsLine = "🔨  Stations & Hives: <b>0</b>";
        private string _interiorLine = "🚪  Interior Occlusion: <b>0</b>";
        private string _detailLine = "📐  Ornamental Detail LOD: <b>0</b>";
        private string _lootLine = "💎  Ground Loot: <b>0</b>";

        private const string DividerLine = "<color=#2F4156>──────────────────────────────────────────────</color>";
        private const string FooterControls = "<size=10><color=#7E94A8><b>[Shift+F7]</b> Toggle HUD  |  <b>[F7]</b> Toggle Optimizations</color></size>";
        private const string FooterCredits = "<size=9><color=#FFAA44>Special thanks to <b>DaymonMontage</b> (twitch.tv/daymonmontage)</color></size>";

        private Texture2D? _bgTexture;
        private Texture2D? _borderTexture;
        private Texture2D? _headerTexture;
        private GUIStyle? _labelStyle;
        private GUIStyle? _titleStyle;
        private GUIStyle? _subTitleStyle;
        private GUIStyle? _dimStyle;
        private int _lastFontSize = -1;

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
            _bgTexture = new Texture2D(1, 1);
            _bgTexture.SetPixel(0, 0, new Color(0.06f, 0.08f, 0.11f, 0.90f));
            _bgTexture.Apply();

            _borderTexture = new Texture2D(1, 1);
            _borderTexture.SetPixel(0, 0, new Color(0.22f, 0.32f, 0.44f, 0.95f));
            _borderTexture.Apply();

            _headerTexture = new Texture2D(1, 1);
            _headerTexture.SetPixel(0, 0, new Color(0.11f, 0.15f, 0.22f, 0.95f));
            _headerTexture.Apply();
        }

        private void Update()
        {
            CheckToggleHotkey();

            float dt = Mathf.Max(0.00001f, Time.unscaledDeltaTime);
            _frameTimeBuffer[_frameBufferIndex] = dt;
            _frameBufferIndex = (_frameBufferIndex + 1) % FrameBufferSize;
            if (_frameBufferCount < FrameBufferSize)
            {
                _frameBufferCount++;
            }

            if (!IsVisible) return;

            _metricTimer += dt;
            if (_metricTimer >= 0.25f)
            {
                _metricTimer = 0f;
                CalculateFramerateMetrics();
                BuildFormattedStrings();
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
                    if (IsVisible)
                    {
                        CalculateFramerateMetrics();
                        BuildFormattedStrings();
                    }
                }
            }
        }

        private void CalculateFramerateMetrics()
        {
            if (_frameBufferCount == 0) return;

            int sampleCount = Mathf.Min(5, _frameBufferCount);
            float recentSum = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                int idx = (_frameBufferIndex - 1 - i + FrameBufferSize) % FrameBufferSize;
                recentSum += _frameTimeBuffer[idx];
            }
            _liveFps = sampleCount / Mathf.Max(0.0001f, recentSum);

            float totalDuration = 0f;
            for (int i = 0; i < _frameBufferCount; i++)
            {
                totalDuration += _frameTimeBuffer[i];
                _sortedBuffer[i] = _frameTimeBuffer[i];
            }
            _avgFps = _frameBufferCount / Mathf.Max(0.0001f, totalDuration);
            _avgFrameTimeMs = (totalDuration / _frameBufferCount) * 1000f;

            Array.Sort(_sortedBuffer, 0, _frameBufferCount);
            int low1PercentIndex = Mathf.Clamp((int)(_frameBufferCount * 0.99f), 0, _frameBufferCount - 1);
            float low1PercentFrameTime = _sortedBuffer[low1PercentIndex];
            _onePercentLowFps = 1.0f / Mathf.Max(0.0001f, low1PercentFrameTime);
        }

        private void BuildFormattedStrings()
        {
            _titleLine = $"<color=#55CCFF><b>⚡ OptiHouse v{OptiHousePlugin.ModVersion}</b></color>";
            _statusLine = OptiHousePlugin.IsOptimizationActive
                ? "<color=#00FF66><b>[OptiHouse: ACTIVE]</b></color>"
                : "<color=#FF4444><b>[OptiHouse: DISABLED]</b></color>";

            string lowColor = _onePercentLowFps >= 45f ? "#00FF66" : (_onePercentLowFps >= 30f ? "#FFFF55" : "#FF5555");
            string fpsColor = _liveFps >= 55f ? "#00FF66" : (_liveFps >= 35f ? "#FFFF55" : "#FF5555");

            int totalBasePieces = GetTotalBasePieces();
            _fpsLine = $"<b>FPS:</b> <color={fpsColor}>{_liveFps:F1}</color>  |  <b>Avg:</b> {_avgFps:F1}  |  <b>1% Low:</b> <color={lowColor}>{_onePercentLowFps:F1}</color>";
            _frametimeLine = $"<b>Frametime:</b> {_avgFrameTimeMs:F1} ms  |  <b>Base Pieces:</b> {totalBasePieces:N0}";

            _instancingLine =
                $"🧊  GPU Instanced: <color=#00FF66><b>{PrefabInstancingOptimizer.InstancedMaterialsCount}</b> mats</color>  |  <color=#55CCFF><b>{PrefabInstancingOptimizer.OffscreenParticleSystemsCount}</b></color> C++ VFX";

            _lightsLine =
                $"💡  Point Lights: <color=#00FF66><b>{LightShadowOptimizer.ActivePointLights}</b> active</color> / <color=#FFAA44><b>{LightShadowOptimizer.CulledPointLights}</b> capped</color> ({LightShadowOptimizer.ActiveShadowLights} shd / {LightShadowOptimizer.TotalLights} tot)";

            _propShadowsLine =
                $"🌒  Prop Shadows: <color=#00FF66><b>{PieceShadowCuller.CulledShadowCount}</b> off</color> / {PieceShadowCuller.TrackedPieceCount} tracked";

            _supportLine =
                $"🏛️  PhysX Skips: <color=#00FF66><b>{WearNTearOptimizer.SupportSweepsSkipped:N0}</b></color> Support  |  <color=#55CCFF><b>{WearNTearOptimizer.AshlandsUpdatesSkipped:N0}</b></color> Ash";

            _signsLine =
                $"🪧  Signs UI: <color=#00FF66><b>{SignOptimizer.CulledSigns}</b> culled</color> / {SignOptimizer.TotalSigns} tot (<color=#55CCFF><b>{SignOptimizer.PermissionChecksSkipped:N0}</b></color> UGC skips)";

            int smokePuffs = 0;
            try { smokePuffs = Smoke.GetTotalSmoke(); } catch { }
            _smokeLine = $"💨  Smoke Puffs: <b>{smokePuffs}</b> active / {SmokeOptimizer.GetConfiguredSmokeCap()} cap";

            _clothLine = $"🚩  Banner Cloths: <color=#00FF66><b>{ClothOptimizer.CulledClothCount}</b> paused</color> / {ClothOptimizer.TotalClothCount} total";
            _standsLine = $"📦  Item & Trophy Stands: <color=#00FF66><b>{ItemStandOptimizer.CulledStandCount}</b> culled</color> / {ItemStandOptimizer.TotalStandCount} total";
            _tamedLine = $"🐗  Animals & Turrets: <color=#00FF66><b>{TamedAnimalOptimizer.CulledTamedAnimals}</b></color>/{TamedAnimalOptimizer.TotalTamedAnimals} LOD  |  <color=#55CCFF><b>{TurretOptimizer.IdleTicksSkipped:N0}</b></color> Turret";
            _stationsLine = $"🔨  Stations & Hives: <color=#00FF66><b>{CraftingStationOptimizer.CulledStations}</b> paused</color> / {CraftingStationOptimizer.TotalStations} total";
            _interiorLine = $"🚪  Interior Occlusion: <color=#00FF66><b>{InteriorOcclusionOptimizer.CulledInteriorPieces}</b> culled</color> / {InteriorOcclusionOptimizer.TotalInteriorPieces} total";
            _detailLine = $"📐  Ornamental Detail LOD: <color=#00FF66><b>{DetailPieceLOD.CulledDetailPieces}</b> culled</color> / {DetailPieceLOD.TotalDetailPieces} total";
            _lootLine = $"💎  Ground Loot: <b>{ItemDropOptimizer.TotalDroppedItems}</b> (<color=#00FF66><b>{ItemDropOptimizer.SleepingItemsCount}</b> sleeping</color>)";
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

        private void OnGUI()
        {
            if (!IsVisible) return;

            int currentFontSize = ModConfig.HudTextSize != null ? ModConfig.HudTextSize.Value : 13;
            UpdateStyles(currentFontSize);

            float width = 425f;
            float height = 575f;
            float margin = 16f;

            HudAnchorPosition anchor = ModConfig.HudPosition != null ? ModConfig.HudPosition.Value : HudAnchorPosition.TopLeft;
            float x = margin;
            float y = margin;

            switch (anchor)
            {
                case HudAnchorPosition.TopRight:
                    x = Screen.width - width - margin;
                    break;
                case HudAnchorPosition.BottomLeft:
                    y = Screen.height - height - margin;
                    break;
                case HudAnchorPosition.BottomRight:
                    x = Screen.width - width - margin;
                    y = Screen.height - height - margin;
                    break;
            }

            Rect panelRect = new Rect(x, y, width, height);

            if (_borderTexture != null) GUI.DrawTexture(panelRect, _borderTexture);
            if (_bgTexture != null) GUI.DrawTexture(new Rect(panelRect.x + 1f, panelRect.y + 1f, panelRect.width - 2f, panelRect.height - 2f), _bgTexture);
            if (_headerTexture != null) GUI.DrawTexture(new Rect(panelRect.x + 1f, panelRect.y + 1f, panelRect.width - 2f, 32f), _headerTexture);

            GUILayout.BeginArea(new Rect(panelRect.x + 12f, panelRect.y + 7f, panelRect.width - 24f, panelRect.height - 14f));

            GUILayout.BeginHorizontal();
            GUILayout.Label(_titleLine, _titleStyle!);
            GUILayout.FlexibleSpace();
            GUILayout.Label(_statusLine, _subTitleStyle!);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            GUILayout.Label(_fpsLine, _labelStyle!);
            GUILayout.Label(_frametimeLine, _labelStyle!);
            GUILayout.Label(DividerLine, _dimStyle!);

            GUILayout.Label(_instancingLine, _labelStyle!);
            GUILayout.Label(_lightsLine, _labelStyle!);
            GUILayout.Label(_propShadowsLine, _labelStyle!);
            GUILayout.Label(_supportLine, _labelStyle!);
            GUILayout.Label(_signsLine, _labelStyle!);
            GUILayout.Label(_smokeLine, _labelStyle!);
            GUILayout.Label(_clothLine, _labelStyle!);
            GUILayout.Label(_standsLine, _labelStyle!);
            GUILayout.Label(_tamedLine, _labelStyle!);
            GUILayout.Label(_stationsLine, _labelStyle!);
            GUILayout.Label(_interiorLine, _labelStyle!);
            GUILayout.Label(_detailLine, _labelStyle!);
            GUILayout.Label(_lootLine, _labelStyle!);

            GUILayout.Label(DividerLine, _dimStyle!);
            GUILayout.Label(FooterControls, _dimStyle!);
            GUILayout.Label(FooterCredits, _dimStyle!);

            GUILayout.EndArea();
        }

        private void UpdateStyles(int fontSize)
        {
            if (_labelStyle != null && _lastFontSize == fontSize) return;
            _lastFontSize = fontSize;

            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, richText = true, wordWrap = false };
            _labelStyle.normal.textColor = Color.white;

            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize + 1, richText = true, wordWrap = false };
            _titleStyle.normal.textColor = Color.white;

            _subTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, richText = true, wordWrap = false };
            _subTitleStyle.normal.textColor = Color.white;

            _dimStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize - 1, richText = true, wordWrap = false };
            _dimStyle.normal.textColor = new Color(0.6f, 0.7f, 0.8f, 1f);
        }
    }
}
