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

        // Cached metrics
        private int _totalBasePieces;
        private int _activePointLights;
        private int _culledPointLights;
        private int _activeShadowLights;
        private int _totalLights;
        private int _propShadowsCulled;
        private int _propShadowsTotal;
        private long _ashlandsSkips;
        private long _envThrottled;
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
        private int _detailCulled;
        private int _detailTotal;
        private int _droppedTotal;
        private int _droppedSleeping;
        private bool _gfxJobsActive;

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
            _bgTexture.SetPixel(0, 0, new Color(0.06f, 0.08f, 0.11f, 0.88f));
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

        private void CollectModuleMetrics()
        {
            _totalBasePieces = GetTotalBasePieces();

            _activePointLights = LightShadowOptimizer.ActivePointLights;
            _culledPointLights = LightShadowOptimizer.CulledPointLights;
            _activeShadowLights = LightShadowOptimizer.ActiveShadowLights;
            _totalLights = LightShadowOptimizer.TotalLights;
            _envThrottled = LightShadowOptimizer.EnvChecksThrottled;

            _propShadowsCulled = PieceShadowCuller.CulledShadowCount;
            _propShadowsTotal = PieceShadowCuller.TrackedPieceCount;

            _ashlandsSkips = WearNTearOptimizer.AshlandsUpdatesSkipped;

            try
            {
                _smokePuffs = Smoke.GetTotalSmoke();
                _smokeCap = SmokeOptimizer.GetConfiguredSmokeCap();
            }
            catch
            {
                _smokePuffs = 0;
            }

            _clothCulled = ClothOptimizer.CulledClothCount;
            _clothTotal = ClothOptimizer.TotalClothCount;

            _standsCulled = ItemStandOptimizer.CulledStandCount;
            _standsTotal = ItemStandOptimizer.TotalStandCount;

            _tamedTotal = TamedAnimalOptimizer.TotalTamedAnimals;
            _tamedLod = TamedAnimalOptimizer.CulledTamedAnimals;

            _stationsTotal = CraftingStationOptimizer.TotalStations;
            _stationsCulled = CraftingStationOptimizer.CulledStations;

            _interiorCulled = InteriorOcclusionOptimizer.CulledInteriorPieces;
            _interiorTotal = InteriorOcclusionOptimizer.TotalInteriorPieces;

            _detailCulled = DetailPieceLOD.CulledDetailPieces;
            _detailTotal = DetailPieceLOD.TotalDetailPieces;

            _droppedTotal = ItemDropOptimizer.TotalDroppedItems;
            _droppedSleeping = ItemDropOptimizer.SleepingItemsCount;

            _gfxJobsActive = BootConfigTweaker.IsGfxJobsActive;
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

            float width = 410f;
            float height = 560f;
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

            if (_borderTexture != null)
            {
                GUI.DrawTexture(panelRect, _borderTexture);
            }
            if (_bgTexture != null)
            {
                GUI.DrawTexture(new Rect(panelRect.x + 1f, panelRect.y + 1f, panelRect.width - 2f, panelRect.height - 2f), _bgTexture);
            }
            if (_headerTexture != null)
            {
                GUI.DrawTexture(new Rect(panelRect.x + 1f, panelRect.y + 1f, panelRect.width - 2f, 32f), _headerTexture);
            }

            GUILayout.BeginArea(new Rect(panelRect.x + 12f, panelRect.y + 7f, panelRect.width - 24f, panelRect.height - 14f));

            bool isOptimizing = OptiHousePlugin.IsOptimizationActive;
            string statusStr = isOptimizing
                ? "<color=#00FF66><b>[OptiHouse: ACTIVE]</b></color>"
                : "<color=#FF4444><b>[OptiHouse: DISABLED]</b></color>";

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#55CCFF><b>⚡ OptiHouse v{OptiHousePlugin.ModVersion}</b></color>", _titleStyle!);
            GUILayout.FlexibleSpace();
            GUILayout.Label(statusStr, _subTitleStyle!);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            string lowColor = _onePercentLowFps >= 45f ? "#00FF66" : (_onePercentLowFps >= 30f ? "#FFFF55" : "#FF5555");
            string fpsColor = _liveFps >= 55f ? "#00FF66" : (_liveFps >= 35f ? "#FFFF55" : "#FF5555");

            GUILayout.Label(
                $"<b>FPS:</b> <color={fpsColor}>{_liveFps:F1}</color>  |  <b>Avg:</b> {_avgFps:F1}  |  <b>1% Low:</b> <color={lowColor}>{_onePercentLowFps:F1}</color>",
                _labelStyle!
            );
            GUILayout.Label($"<b>Frametime:</b> {_avgFrameTimeMs:F1} ms  |  <b>Base Pieces:</b> {_totalBasePieces:N0}", _labelStyle!);

            GUILayout.Label("<color=#2F4156>─────────────────────────────────────────────</color>", _dimStyle!);

            GUILayout.Label(
                $"💡  Point Lights: <color=#00FF66><b>{_activePointLights}</b> active</color> / <color=#FFAA44><b>{_culledPointLights}</b> capped</color> ({_activeShadowLights} shadows / {_totalLights} total)",
                _labelStyle!
            );

            GUILayout.Label(
                $"🌒  Prop Shadows: <color=#00FF66><b>{_propShadowsCulled}</b> off</color> / {_propShadowsTotal} tracked",
                _labelStyle!
            );

            GUILayout.Label(
                $"⚡  CPU Loop Skips: <color=#00FF66><b>{_ashlandsSkips:N0}</b></color> WNT  |  <color=#55CCFF><b>{_envThrottled:N0}</b></color> FireEnv",
                _labelStyle!
            );

            GUILayout.Label(
                $"💨  Smoke Puffs: <b>{_smokePuffs}</b> active / {_smokeCap} cap",
                _labelStyle!
            );

            string particleStatus = (ModConfig.EnableFastParticleFade != null && ModConfig.EnableFastParticleFade.Value)
                ? "<color=#00FF66>Active (1x clean fade)</color>"
                : "<color=#FFAA44>Vanilla</color>";
            GUILayout.Label($"✨  Particle Fade: {particleStatus}", _labelStyle!);

            GUILayout.Label(
                $"🚩  Banner Cloths: <color=#00FF66><b>{_clothCulled}</b> paused</color> / {_clothTotal} total",
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
                $"🔨  Stations & Hives: <color=#00FF66><b>{_stationsCulled}</b> paused</color> / {_stationsTotal} total",
                _labelStyle!
            );

            GUILayout.Label(
                $"🚪  Interior Occlusion: <color=#00FF66><b>{_interiorCulled}</b> culled</color> / {_interiorTotal} total",
                _labelStyle!
            );

            GUILayout.Label(
                $"📐  Ornamental Detail LOD: <color=#00FF66><b>{_detailCulled}</b> culled</color> / {_detailTotal} total",
                _labelStyle!
            );

            GUILayout.Label(
                $"💎  Ground Loot: <b>{_droppedTotal}</b> (<color=#00FF66><b>{_droppedSleeping}</b> sleeping</color>)",
                _labelStyle!
            );

            string gfxJobsText = _gfxJobsActive
                ? "<color=#00FF66>Active (Native/Gfx)</color>"
                : (BootConfigTweaker.RestartRequired
                    ? "<color=#FFFF55>Pending Restart</color>"
                    : "<color=#FFAA44>Vanilla</color>");
            GUILayout.Label($"🖥️  Engine Gfx Jobs: {gfxJobsText}", _labelStyle!);

            GUILayout.Label("<color=#2F4156>─────────────────────────────────────────────</color>", _dimStyle!);

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
