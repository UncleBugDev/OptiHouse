using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// ItemStand and ArmorStand Optimizer for OptiHouse.
    /// Pauses attached particle effects and mounted item point lights beyond ItemStandCullDistance (default 15m).
    /// Snapshots original enabled states when culling so empty/inactive slots stay untouched.
    /// </summary>
    public class ItemStandOptimizer : MonoBehaviour
    {
        public static ItemStandOptimizer? Instance { get; private set; }

        private class TrackedStand
        {
            public int InstanceId;
            public Component Stand = null!;
            public Transform Transform = null!;
            public bool IsCulled;

            public readonly List<ParticleSystem> Particles = new List<ParticleSystem>();
            public readonly List<ParticleSystemRenderer> ParticleRenderers = new List<ParticleSystemRenderer>();
            public readonly List<Light> Lights = new List<Light>();

            public readonly List<ParticleSystem> PausedParticles = new List<ParticleSystem>();
            public readonly List<ParticleSystemRenderer> DisabledRenderers = new List<ParticleSystemRenderer>();
            public readonly List<Light> DisabledLights = new List<Light>();
        }

        private static readonly List<TrackedStand> _trackedStands = new List<TrackedStand>();
        private static readonly Dictionary<int, TrackedStand> _standLookup = new Dictionary<int, TrackedStand>();

        private static int _culledStandCount;
        private int _currentIndex;
        private bool _wasOptimizationActive = true;

        public static int TotalStandCount => _trackedStands.Count;
        public static int CulledStandCount => _culledStandCount;

        private void Awake()
        {
            Instance = this;
            ScanExistingStands();
        }

        private void OnDestroy()
        {
            RestoreAllStands();
            _trackedStands.Clear();
            _standLookup.Clear();
            _culledStandCount = 0;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static void ScanExistingStands()
        {
            ItemStand[] itemStands = FindObjectsByType<ItemStand>(FindObjectsSortMode.None);
            if (itemStands != null)
            {
                for (int i = 0; i < itemStands.Length; i++)
                {
                    if (itemStands[i] != null) Register(itemStands[i]);
                }
            }

            ArmorStand[] armorStands = FindObjectsByType<ArmorStand>(FindObjectsSortMode.None);
            if (armorStands != null)
            {
                for (int i = 0; i < armorStands.Length; i++)
                {
                    if (armorStands[i] != null) Register(armorStands[i]);
                }
            }
        }

        private void Update()
        {
            bool isActive = OptiHousePlugin.IsOptimizationActive &&
                            ModConfig.EnableItemStandOptimization != null &&
                            ModConfig.EnableItemStandOptimization.Value;

            if (!isActive)
            {
                if (_wasOptimizationActive)
                {
                    _wasOptimizationActive = false;
                    RestoreAllStands();
                }
                return;
            }

            _wasOptimizationActive = true;

            int total = _trackedStands.Count;
            if (total == 0) return;

            Vector3 refPos = OptimizationHelpers.GetReferencePosition();
            float cullDistance = ModConfig.ItemStandCullDistance != null
                ? ModConfig.ItemStandCullDistance.Value
                : 15.0f;

            float cullDistSq = cullDistance * cullDistance;
            float restoreDistance = Mathf.Max(0f, cullDistance - 2.5f);
            float restoreDistSq = restoreDistance * restoreDistance;

            int batchSize = Mathf.Min(50, total);
            for (int i = 0; i < batchSize; i++)
            {
                _currentIndex++;
                if (_currentIndex >= _trackedStands.Count)
                {
                    _currentIndex = 0;
                }

                TrackedStand item = _trackedStands[_currentIndex];
                if (item.Stand == null || item.Transform == null)
                {
                    if (item.IsCulled && _culledStandCount > 0) _culledStandCount--;
                    _standLookup.Remove(item.InstanceId);
                    _trackedStands.RemoveAt(_currentIndex);
                    if (_currentIndex > 0) _currentIndex--;
                    continue;
                }

                if (!item.Stand.gameObject.activeInHierarchy)
                {
                    continue;
                }

                float sqrDist = (item.Transform.position - refPos).sqrMagnitude;

                if (sqrDist > cullDistSq)
                {
                    if (!item.IsCulled)
                    {
                        ApplyCull(item);
                        _culledStandCount++;
                    }
                }
                else if (sqrDist < restoreDistSq)
                {
                    if (item.IsCulled)
                    {
                        RestoreEffects(item);
                        if (_culledStandCount > 0) _culledStandCount--;
                    }
                }
            }
        }

        public static void Register(Component stand)
        {
            if (!OptimizationHelpers.IsValidWorldObject(stand)) return;

            int id = stand.GetInstanceID();
            if (_standLookup.ContainsKey(id)) return;

            var item = new TrackedStand
            {
                InstanceId = id,
                Stand = stand,
                Transform = stand.transform,
                IsCulled = false
            };

            CollectEffects(item);

            bool isOptActive = OptiHousePlugin.IsOptimizationActive &&
                               ModConfig.EnableItemStandOptimization != null &&
                               ModConfig.EnableItemStandOptimization.Value;

            Vector3 refPos = OptimizationHelpers.GetReferencePosition();
            float cullDistance = ModConfig.ItemStandCullDistance != null
                ? ModConfig.ItemStandCullDistance.Value
                : 15.0f;

            if (isOptActive && (item.Transform.position - refPos).sqrMagnitude > cullDistance * cullDistance)
            {
                ApplyCull(item);
                _culledStandCount++;
            }

            _trackedStands.Add(item);
            _standLookup[id] = item;
        }

        public static void Unregister(Component stand)
        {
            if (stand == null) return;

            int id = stand.GetInstanceID();
            if (_standLookup.TryGetValue(id, out TrackedStand item))
            {
                if (item.IsCulled)
                {
                    RestoreEffects(item);
                    if (_culledStandCount > 0) _culledStandCount--;
                }
                _trackedStands.Remove(item);
                _standLookup.Remove(id);
            }
        }

        public static void RefreshVisuals(Component stand)
        {
            if (!OptimizationHelpers.IsValidWorldObject(stand)) return;

            int id = stand.GetInstanceID();
            if (_standLookup.TryGetValue(id, out TrackedStand item))
            {
                bool wasCulled = item.IsCulled;
                if (wasCulled) RestoreEffects(item);

                CollectEffects(item);

                if (wasCulled)
                {
                    ApplyCull(item);
                    // IsCulled was already counted before CollectEffects
                }
            }
            else
            {
                Register(stand);
            }
        }

        private static void CollectEffects(TrackedStand item)
        {
            if (item.Stand == null) return;

            item.Particles.Clear();
            item.ParticleRenderers.Clear();
            item.Lights.Clear();

            ParticleSystem[] pss = item.Stand.GetComponentsInChildren<ParticleSystem>(true);
            if (pss != null)
            {
                for (int i = 0; i < pss.Length; i++)
                {
                    ParticleSystem ps = pss[i];
                    if (ps == null) continue;
                    item.Particles.Add(ps);
                    ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
                    if (psr != null)
                    {
                        item.ParticleRenderers.Add(psr);
                    }
                }
            }

            Light[] lights = item.Stand.GetComponentsInChildren<Light>(true);
            if (lights != null)
            {
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i] != null)
                    {
                        item.Lights.Add(lights[i]);
                    }
                }
            }
        }

        private static void ApplyCull(TrackedStand item)
        {
            if (item.IsCulled) return;
            item.IsCulled = true;
            item.PausedParticles.Clear();
            item.DisabledRenderers.Clear();
            item.DisabledLights.Clear();

            for (int i = 0; i < item.Particles.Count; i++)
            {
                ParticleSystem ps = item.Particles[i];
                if (ps != null && ps.isPlaying)
                {
                    ps.Pause(true);
                    item.PausedParticles.Add(ps);
                }
            }

            for (int i = 0; i < item.ParticleRenderers.Count; i++)
            {
                ParticleSystemRenderer psr = item.ParticleRenderers[i];
                if (psr != null && psr.enabled)
                {
                    psr.enabled = false;
                    item.DisabledRenderers.Add(psr);
                }
            }

            for (int i = 0; i < item.Lights.Count; i++)
            {
                Light l = item.Lights[i];
                if (l != null && l.enabled)
                {
                    l.enabled = false;
                    item.DisabledLights.Add(l);
                }
            }
        }

        private static void RestoreEffects(TrackedStand item)
        {
            if (!item.IsCulled) return;
            item.IsCulled = false;

            for (int i = 0; i < item.DisabledRenderers.Count; i++)
            {
                if (item.DisabledRenderers[i] != null)
                {
                    item.DisabledRenderers[i].enabled = true;
                }
            }
            item.DisabledRenderers.Clear();

            for (int i = 0; i < item.PausedParticles.Count; i++)
            {
                ParticleSystem ps = item.PausedParticles[i];
                if (ps != null && ps.isPaused)
                {
                    ps.Play(true);
                }
            }
            item.PausedParticles.Clear();

            for (int i = 0; i < item.DisabledLights.Count; i++)
            {
                if (item.DisabledLights[i] != null)
                {
                    item.DisabledLights[i].enabled = true;
                }
            }
            item.DisabledLights.Clear();
        }

        public static void RestoreAllStands()
        {
            for (int i = 0; i < _trackedStands.Count; i++)
            {
                RestoreEffects(_trackedStands[i]);
            }
            _culledStandCount = 0;
        }
    }

    #region Harmony Patches

    [HarmonyPatch(typeof(ItemStand), "Awake")]
    public static class ItemStand_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemStand __instance)
        {
            ItemStandOptimizer.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(ItemStand), "OnDestroyed")]
    public static class ItemStand_OnDestroyed_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemStand __instance)
        {
            ItemStandOptimizer.Unregister(__instance);
        }
    }

    [HarmonyPatch(typeof(ItemStand), "UpdateVisual")]
    public static class ItemStand_UpdateVisual_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemStand __instance)
        {
            ItemStandOptimizer.RefreshVisuals(__instance);
        }
    }

    [HarmonyPatch(typeof(ItemStand), "DestroyAttachment")]
    public static class ItemStand_DestroyAttachment_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemStand __instance)
        {
            ItemStandOptimizer.RefreshVisuals(__instance);
        }
    }

    [HarmonyPatch(typeof(ArmorStand), "Awake")]
    public static class ArmorStand_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ArmorStand __instance)
        {
            ItemStandOptimizer.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(ArmorStand), "OnDestroyed")]
    public static class ArmorStand_OnDestroyed_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ArmorStand __instance)
        {
            ItemStandOptimizer.Unregister(__instance);
        }
    }

    [HarmonyPatch(typeof(ArmorStand), "UpdateVisual")]
    public static class ArmorStand_UpdateVisual_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ArmorStand __instance)
        {
            ItemStandOptimizer.RefreshVisuals(__instance);
        }
    }

    [HarmonyPatch(typeof(ArmorStand), "DestroyAttachment", typeof(int))]
    public static class ArmorStand_DestroyAttachment_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ArmorStand __instance)
        {
            ItemStandOptimizer.RefreshVisuals(__instance);
        }
    }

    #endregion
}
