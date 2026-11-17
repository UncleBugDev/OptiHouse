using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Eliminates Valheim's rhythmic 3-second frame-time spike inside mega-bases caused by ReflectionUpdate
    /// rendering all 6 cubemap faces + shadows synchronously on a single frame:
    /// 1. Switches m_probe1 &amp; m_probe2 to ReflectionProbeTimeSlicingMode.IndividualFaces (spread across 9 frames).
    /// 2. Disables shadows inside environment reflection probes (shadowDistance = 0f) and strips micro-layers
    ///    (piece_nonsolid, item, Default_small, character) from probe culling masks.
    /// 3. Dynamically scales m_interval from 3s to ReflectionUpdateInterval (14s) while stationary (&lt;12m movement),
    ///    preserving m_transitionDuration (3s) so dual-probe cross-fades never pop or flicker.
    /// Fully snapshot-backed and reversible via F7.
    /// </summary>
    public class ReflectionProbeOptimizer : MonoBehaviour
    {
        public static ReflectionProbeOptimizer? Instance { get; private set; }

        private struct ProbeSnapshot
        {
            public bool Valid;
            public ReflectionProbeTimeSlicingMode OrigTimeSlicing;
            public float OrigShadowDistance;
            public int OrigCullingMask;
        }

        private static ProbeSnapshot _probe1Backup;
        private static ProbeSnapshot _probe2Backup;
        private static float _origInterval = 3f;
        private static bool _hasIntervalBackup;
        private static bool _appliedToCurrentInstance;
        private static ReflectionUpdate? _trackedInstance;
        private static Vector3 _lastRenderRefPos = new Vector3(-99999f, -99999f, -99999f);

        public static bool IsTimeSlicedActive => _appliedToCurrentInstance;
        public static long ProbeRendersCompleted { get; internal set; }

        private float _checkTimer;
        private bool _wasActive = true;

        private static bool ShouldOptimize()
        {
            return OptiHousePlugin.IsOptimizationActive &&
                   ModConfig.OptimizeReflectionProbes != null &&
                   ModConfig.OptimizeReflectionProbes.Value;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            RestoreVanillaProbes();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            bool active = ShouldOptimize();
            if (!active)
            {
                if (_wasActive)
                {
                    _wasActive = false;
                    RestoreVanillaProbes();
                }
                return;
            }

            if (!_wasActive)
            {
                _wasActive = true;
                if (ReflectionUpdate.instance != null)
                {
                    ApplyToReflectionUpdate(ReflectionUpdate.instance);
                }
            }

            _checkTimer += Time.unscaledDeltaTime;
            if (_checkTimer < 1.0f) return;
            _checkTimer = 0f;

            ReflectionUpdate? ru = ReflectionUpdate.instance;
            if (ru == null) return;

            if (_trackedInstance != ru || !_appliedToCurrentInstance)
            {
                ApplyToReflectionUpdate(ru);
            }

            UpdateDynamicInterval(ru);
        }

        public static void ApplyToReflectionUpdate(ReflectionUpdate? ru)
        {
            if (ru == null || !ShouldOptimize()) return;

            if (_trackedInstance != ru)
            {
                _trackedInstance = ru;
                _probe1Backup.Valid = false;
                _probe2Backup.Valid = false;
                _hasIntervalBackup = false;
            }

            if (!_hasIntervalBackup)
            {
                _origInterval = ru.m_interval > 0.1f ? ru.m_interval : 3f;
                _hasIntervalBackup = true;
            }

            int stripMask = LayerMask.GetMask("piece_nonsolid", "item", "Default_small", "character", "character_net", "viewblock");

            ConfigureProbe(ru.m_probe1, ref _probe1Backup, stripMask);
            ConfigureProbe(ru.m_probe2, ref _probe2Backup, stripMask);

            UpdateDynamicInterval(ru);
            _appliedToCurrentInstance = true;
        }

        private static void ConfigureProbe(ReflectionProbe? probe, ref ProbeSnapshot backup, int stripMask)
        {
            if (probe == null) return;

            if (!backup.Valid)
            {
                backup.OrigTimeSlicing = probe.timeSlicingMode;
                backup.OrigShadowDistance = probe.shadowDistance;
                backup.OrigCullingMask = probe.cullingMask;
                backup.Valid = true;
            }

            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.shadowDistance = 0f;
            probe.cullingMask = backup.OrigCullingMask & ~stripMask;
        }

        internal static void UpdateDynamicInterval(ReflectionUpdate ru)
        {
            if (!OptimizationHelpers.TryGetReferencePosition(out Vector3 refPos))
            {
                return;
            }

            float stationaryInterval = ModConfig.ReflectionUpdateInterval != null
                ? Mathf.Max(3f, ModConfig.ReflectionUpdateInterval.Value)
                : 14.0f;

            float movedSqr = (refPos - _lastRenderRefPos).sqrMagnitude;
            ru.m_interval = movedSqr >= 144f ? Mathf.Min(4.0f, stationaryInterval) : stationaryInterval;
        }

        internal static void NotifyProbeRendered()
        {
            if (OptimizationHelpers.TryGetReferencePosition(out Vector3 refPos))
            {
                _lastRenderRefPos = refPos;
            }
            ProbeRendersCompleted++;
        }

        public static void RestoreVanillaProbes()
        {
            ReflectionUpdate? ru = _trackedInstance != null ? _trackedInstance : ReflectionUpdate.instance;
            if (ru != null)
            {
                if (_hasIntervalBackup)
                {
                    ru.m_interval = _origInterval;
                }
                if (ru.m_probe1 != null && _probe1Backup.Valid)
                {
                    ru.m_probe1.timeSlicingMode = _probe1Backup.OrigTimeSlicing;
                    ru.m_probe1.shadowDistance = _probe1Backup.OrigShadowDistance;
                    ru.m_probe1.cullingMask = _probe1Backup.OrigCullingMask;
                }
                if (ru.m_probe2 != null && _probe2Backup.Valid)
                {
                    ru.m_probe2.timeSlicingMode = _probe2Backup.OrigTimeSlicing;
                    ru.m_probe2.shadowDistance = _probe2Backup.OrigShadowDistance;
                    ru.m_probe2.cullingMask = _probe2Backup.OrigCullingMask;
                }
            }

            _appliedToCurrentInstance = false;
        }
    }

    [HarmonyPatch(typeof(ReflectionUpdate), "Start")]
    public static class ReflectionUpdate_Start_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ReflectionUpdate __instance)
        {
            ReflectionProbeOptimizer.ApplyToReflectionUpdate(__instance);
        }
    }

    [HarmonyPatch(typeof(ReflectionUpdate), nameof(ReflectionUpdate.UpdateReflection))]
    public static class ReflectionUpdate_UpdateReflection_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ReflectionUpdate __instance)
        {
            ReflectionProbeOptimizer.NotifyProbeRendered();
            if (__instance != null && OptiHousePlugin.IsOptimizationActive &&
                ModConfig.OptimizeReflectionProbes != null && ModConfig.OptimizeReflectionProbes.Value)
            {
                ReflectionProbeOptimizer.UpdateDynamicInterval(__instance);
            }
        }
    }
}
