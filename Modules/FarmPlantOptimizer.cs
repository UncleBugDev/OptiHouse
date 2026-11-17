using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Mega-Farm &amp; Crop Optimizer (1,000–3,000+ Plant &amp; Pickable fields) for OptiHouse v2.2.
    /// 1. Fixes Iron Gate's vanilla Plant.SUpdate inverted timer bug (`!(time &gt; m_updateTime)` in IL), which causes
    ///    every planted crop to execute Heightmap.FindHeightmap + HaveRoof (100m Raycast) + HaveGrowSpace
    ///    (30-collider OverlapSphereNonAlloc) continuously on every SlowUpdater pass instead of every 10s!
    ///    Preserves instant spawn visuals, exact 10.0s overcrowding/roof status feedback, and 100% exact ZDO harvest times.
    /// 2. Strips real-time ShadowCastingMode and ReflectionProbeUsage on distant (&gt;12m) crops and small forageables
    ///    (Plant &amp; Pickable) using an O(1) swap-back list so Atgeir mass-harvesting causes zero lag.
    /// </summary>
    public class FarmPlantOptimizer : MonoBehaviour
    {
        public static FarmPlantOptimizer? Instance { get; private set; }

        internal sealed class PlantTickState
        {
            public bool InitialVisualDone;
            public bool TenSecondCheckDone;
            public float NextAllowedTime;
        }

        private struct CropRendererBackup
        {
            public MeshRenderer Renderer;
            public ShadowCastingMode OrigShadowMode;
            public ReflectionProbeUsage OrigProbeUsage;
        }

        private sealed class TrackedCrop
        {
            public int InstanceId;
            public GameObject Root = null!;
            public Transform Transform = null!;
            public readonly List<CropRendererBackup> Renderers = new List<CropRendererBackup>(2);
            public bool ShadowsCulled;

            public void CullShadows()
            {
                if (ShadowsCulled) return;
                for (int i = 0; i < Renderers.Count; i++)
                {
                    MeshRenderer mr = Renderers[i].Renderer;
                    if (mr != null)
                    {
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    }
                }
                ShadowsCulled = true;
            }

            public void RestoreShadows()
            {
                if (!ShadowsCulled) return;
                for (int i = 0; i < Renderers.Count; i++)
                {
                    MeshRenderer mr = Renderers[i].Renderer;
                    if (mr != null)
                    {
                        mr.shadowCastingMode = Renderers[i].OrigShadowMode;
                        mr.reflectionProbeUsage = Renderers[i].OrigProbeUsage;
                    }
                }
                ShadowsCulled = false;
            }
        }

        internal static readonly ConditionalWeakTable<Plant, PlantTickState> PlantStates = new();
        private static readonly List<TrackedCrop> _crops = new List<TrackedCrop>(1024);
        private static readonly Dictionary<int, int> _cropIndexMap = new Dictionary<int, int>(1024);

        private static int _culledCropCount;
        private int _scanCursor;
        private bool _wasShadowCullActive = true;

        public static long PlantPhysxChecksSaved { get; internal set; }
        public static int TotalTrackedCrops => _crops.Count;
        public static int CulledCropShadows => _culledCropCount;

        private static bool ShouldCullCropShadows()
        {
            return OptiHousePlugin.IsOptimizationActive &&
                   ModConfig.EnableFarmOptimization != null &&
                   ModConfig.EnableFarmOptimization.Value &&
                   ModConfig.EnableFarmShadowCulling != null &&
                   ModConfig.EnableFarmShadowCulling.Value;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            RestoreAllCropShadows();
            _crops.Clear();
            _cropIndexMap.Clear();
            _culledCropCount = 0;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!ShouldCullCropShadows())
            {
                if (_wasShadowCullActive)
                {
                    _wasShadowCullActive = false;
                    RestoreAllCropShadows();
                }
                return;
            }

            _wasShadowCullActive = true;

            int total = _crops.Count;
            if (total == 0) return;

            Vector3 refPos = OptimizationHelpers.GetReferencePosition();
            float cullDist = ModConfig.FarmShadowCullDistance != null ? ModConfig.FarmShadowCullDistance.Value : 12.0f;
            float cullDistSq = cullDist * cullDist;
            float restoreDist = Mathf.Max(0f, cullDist - 2.5f);
            float restoreDistSq = restoreDist * restoreDist;

            int batchSize = Mathf.Min(450, total);
            for (int i = 0; i < batchSize; i++)
            {
                if (_crops.Count == 0) break;
                _scanCursor++;
                if (_scanCursor >= _crops.Count) _scanCursor = 0;

                TrackedCrop crop = _crops[_scanCursor];
                if (crop.Root == null || crop.Transform == null)
                {
                    SwapRemoveAt(_scanCursor);
                    if (_scanCursor > 0) _scanCursor--;
                    continue;
                }

                if (!crop.Root.activeInHierarchy) continue;

                float sqrDist = (crop.Transform.position - refPos).sqrMagnitude;
                if (sqrDist > cullDistSq)
                {
                    if (!crop.ShadowsCulled)
                    {
                        crop.CullShadows();
                        _culledCropCount++;
                    }
                }
                else if (sqrDist < restoreDistSq)
                {
                    if (crop.ShadowsCulled)
                    {
                        crop.RestoreShadows();
                        if (_culledCropCount > 0) _culledCropCount--;
                    }
                }
            }
        }

        public static void RegisterCropObject(Component? comp, bool checkMaxBounds)
        {
            if (comp == null || !OptimizationHelpers.IsValidWorldObject(comp)) return;

            GameObject go = comp.gameObject;
            int id = go.GetInstanceID();
            if (_cropIndexMap.ContainsKey(id)) return;

            MeshRenderer[] renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            TrackedCrop? tracked = null;
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer mr = renderers[i];
                if (mr == null || mr.shadowCastingMode == ShadowCastingMode.Off) continue;

                if (checkMaxBounds)
                {
                    Bounds b = mr.bounds;
                    float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    if (maxDim > 2.5f)
                    {
                        continue;
                    }
                }

                tracked ??= new TrackedCrop
                {
                    InstanceId = id,
                    Root = go,
                    Transform = go.transform,
                    ShadowsCulled = false
                };

                tracked.Renderers.Add(new CropRendererBackup
                {
                    Renderer = mr,
                    OrigShadowMode = mr.shadowCastingMode,
                    OrigProbeUsage = mr.reflectionProbeUsage
                });
            }

            if (tracked != null && tracked.Renderers.Count > 0)
            {
                int idx = _crops.Count;
                _crops.Add(tracked);
                _cropIndexMap[id] = idx;
            }
        }

        private static void SwapRemoveAt(int idx)
        {
            TrackedCrop removed = _crops[idx];
            if (removed.ShadowsCulled && _culledCropCount > 0)
            {
                _culledCropCount--;
            }
            _cropIndexMap.Remove(removed.InstanceId);

            int lastIdx = _crops.Count - 1;
            if (idx < lastIdx)
            {
                TrackedCrop moved = _crops[lastIdx];
                _crops[idx] = moved;
                _cropIndexMap[moved.InstanceId] = idx;
            }
            _crops.RemoveAt(lastIdx);
        }

        public static void RestoreAllCropShadows()
        {
            for (int i = 0; i < _crops.Count; i++)
            {
                if (_crops[i].ShadowsCulled)
                {
                    _crops[i].RestoreShadows();
                }
            }
            _culledCropCount = 0;
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.Awake))]
    public static class Plant_Awake_Farm_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Plant __instance)
        {
            FarmPlantOptimizer.RegisterCropObject(__instance, checkMaxBounds: false);
        }
    }

    [HarmonyPatch(typeof(Pickable), "Awake")]
    public static class Pickable_Awake_Farm_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pickable __instance)
        {
            FarmPlantOptimizer.RegisterCropObject(__instance, checkMaxBounds: true);
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.SUpdate))]
    public static class Plant_SUpdate_FixBugAndThrottle_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            Plant __instance,
            float time,
            ZNetView ___m_nview,
            float ___m_spawnTime,
            ref float ___m_updateTime)
        {
            if (__instance == null ||
                ___m_nview == null ||
                !___m_nview.IsValid() ||
                !OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableFarmOptimization == null ||
                !ModConfig.EnableFarmOptimization.Value)
            {
                return true;
            }

            var state = FarmPlantOptimizer.PlantStates.GetOrCreateValue(__instance);
            float interval = ModConfig.FarmPlantCheckInterval != null
                ? Mathf.Max(5f, ModConfig.FarmPlantCheckInterval.Value)
                : 14.0f;

            // 1. Immediate initial visual setup pass upon spawn
            if (!state.InitialVisualDone)
            {
                state.InitialVisualDone = true;
                state.NextAllowedTime = Mathf.Min(time + interval, ___m_spawnTime + 10.05f);
                ___m_updateTime = time + 10f;
                return true;
            }

            // 2. Prompt 10-second overcrowding/roof check right after planting (matches vanilla feedback)
            if (!state.TenSecondCheckDone && time - ___m_spawnTime >= 10.02f)
            {
                state.TenSecondCheckDone = true;
                state.NextAllowedTime = time + UnityEngine.Random.Range(interval * 0.85f, interval * 1.15f);
                ___m_updateTime = time + 10f;
                return true;
            }

            // 3. Enforce true interval instead of vanilla's broken `!(time > m_updateTime)` continuous loop
            if (time < state.NextAllowedTime)
            {
                FarmPlantOptimizer.PlantPhysxChecksSaved++;
                return false;
            }

            state.NextAllowedTime = time + UnityEngine.Random.Range(interval * 0.85f, interval * 1.15f);
            ___m_updateTime = time + 10f;
            return true;
        }
    }
}
