using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// ItemStand and ArmorStand Optimizer for OptiHouse.
    /// Manages mounted item effects (trophies, glowing weapons like Frostner, torches, lanterns)
    /// in high-density trophy halls and armory storage rooms.
    /// Pauses attached ParticleSystems and disables point lights beyond a configurable distance (default 12m with 2.5m hysteresis).
    /// </summary>
    public class ItemStandOptimizer : MonoBehaviour
    {
        private static ItemStandOptimizer? _instance;
        public static ItemStandOptimizer? Instance => _instance;

        private class TrackedStand
        {
            public Component Stand = null!; // ItemStand or ArmorStand
            public Transform Transform = null!;
            public bool IsArmorStand;
            public bool IsCulled;

            public readonly List<ParticleSystem> Particles = new List<ParticleSystem>();
            public readonly List<ParticleSystemRenderer> ParticleRenderers = new List<ParticleSystemRenderer>();
            public readonly List<Light> Lights = new List<Light>();
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
            _instance = this;
            ScanExistingStands();
        }

        private void OnDestroy()
        {
            RestoreAllStands();
            _trackedStands.Clear();
            _standLookup.Clear();
            _culledStandCount = 0;
            if (_instance == this)
            {
                _instance = null;
            }
        }

        public static void ScanExistingStands()
        {
            ItemStand[] itemStands = FindObjectsByType<ItemStand>(FindObjectsSortMode.None);
            if (itemStands != null)
            {
                for (int i = 0; i < itemStands.Length; i++)
                {
                    if (itemStands[i] != null)
                    {
                        Register(itemStands[i], false);
                    }
                }
            }

            ArmorStand[] armorStands = FindObjectsByType<ArmorStand>(FindObjectsSortMode.None);
            if (armorStands != null)
            {
                for (int i = 0; i < armorStands.Length; i++)
                {
                    if (armorStands[i] != null)
                    {
                        Register(armorStands[i], true);
                    }
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

            Vector3 refPos = GetReferencePosition();
            float cullDistance = ModConfig.ItemStandCullDistance != null
                ? ModConfig.ItemStandCullDistance.Value
                : 12.0f;

            float cullDistSq = cullDistance * cullDistance;
            // 2.5m hysteresis margin prevents rapid state toggles at boundary
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
                    if (item.IsCulled && _culledStandCount > 0)
                    {
                        _culledStandCount--;
                    }
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
                        item.IsCulled = true;
                        ApplyCull(item);
                        _culledStandCount++;
                    }
                }
                else if (sqrDist < restoreDistSq)
                {
                    if (item.IsCulled)
                    {
                        item.IsCulled = false;
                        RestoreEffects(item);
                        if (_culledStandCount > 0) _culledStandCount--;
                    }
                }
            }
        }

        public static void Register(Component stand, bool isArmorStand)
        {
            if (stand == null) return;

            int id = stand.GetInstanceID();
            if (_standLookup.ContainsKey(id)) return;

            var item = new TrackedStand
            {
                Stand = stand,
                Transform = stand.transform,
                IsArmorStand = isArmorStand,
                IsCulled = false
            };

            CollectEffects(item);

            bool isOptActive = OptiHousePlugin.IsOptimizationActive &&
                               ModConfig.EnableItemStandOptimization != null &&
                               ModConfig.EnableItemStandOptimization.Value;

            Vector3 refPos = GetReferencePosition();
            float cullDistance = ModConfig.ItemStandCullDistance != null
                ? ModConfig.ItemStandCullDistance.Value
                : 12.0f;
            float cullDistSq = cullDistance * cullDistance;

            if (isOptActive && (item.Transform.position - refPos).sqrMagnitude > cullDistSq)
            {
                item.IsCulled = true;
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
            if (stand == null) return;

            int id = stand.GetInstanceID();
            if (_standLookup.TryGetValue(id, out TrackedStand item))
            {
                CollectEffects(item);
            }
            else
            {
                Register(stand, stand is ArmorStand);
            }
        }

        private static void CollectEffects(TrackedStand item)
        {
            if (item.Stand == null) return;

            if (item.IsCulled)
            {
                RestoreEffects(item);
            }

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
                    Light l = lights[i];
                    if (l != null)
                    {
                        item.Lights.Add(l);
                    }
                }
            }

            if (item.IsCulled)
            {
                ApplyCull(item);
            }
        }

        private static void ApplyCull(TrackedStand item)
        {
            for (int i = 0; i < item.Particles.Count; i++)
            {
                ParticleSystem ps = item.Particles[i];
                if (ps != null && ps.isPlaying)
                {
                    ps.Pause(true);
                }
            }

            for (int i = 0; i < item.ParticleRenderers.Count; i++)
            {
                ParticleSystemRenderer psr = item.ParticleRenderers[i];
                if (psr != null)
                {
                    psr.enabled = false;
                }
            }

            for (int i = 0; i < item.Lights.Count; i++)
            {
                Light l = item.Lights[i];
                if (l != null)
                {
                    l.enabled = false;
                }
            }
        }

        private static void RestoreEffects(TrackedStand item)
        {
            for (int i = 0; i < item.ParticleRenderers.Count; i++)
            {
                ParticleSystemRenderer psr = item.ParticleRenderers[i];
                if (psr != null)
                {
                    psr.enabled = true;
                }
            }

            for (int i = 0; i < item.Particles.Count; i++)
            {
                ParticleSystem ps = item.Particles[i];
                if (ps != null && ps.isPaused)
                {
                    ps.Play(true);
                }
            }

            for (int i = 0; i < item.Lights.Count; i++)
            {
                Light l = item.Lights[i];
                if (l != null)
                {
                    l.enabled = true;
                }
            }
        }

        public static void RestoreAllStands()
        {
            for (int i = 0; i < _trackedStands.Count; i++)
            {
                TrackedStand item = _trackedStands[i];
                if (item.IsCulled)
                {
                    RestoreEffects(item);
                    item.IsCulled = false;
                }
            }
            _culledStandCount = 0;
        }

        private static Vector3 GetReferencePosition()
        {
            if (Player.m_localPlayer != null)
            {
                return Player.m_localPlayer.transform.position;
            }
            Camera mainCam = Utils.GetMainCamera();
            if (mainCam != null)
            {
                return mainCam.transform.position;
            }
            return Vector3.zero;
        }
    }

    #region Harmony Patches

    [HarmonyPatch(typeof(OptiHousePlugin), "Awake")]
    public static class ItemStandOptimizer_Plugin_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(OptiHousePlugin __instance)
        {
            if (__instance != null && __instance.gameObject.GetComponent<ItemStandOptimizer>() == null)
            {
                __instance.gameObject.AddComponent<ItemStandOptimizer>();
            }
        }
    }

    [HarmonyPatch(typeof(ItemStand), "Awake")]
    public static class ItemStand_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemStand __instance)
        {
            ItemStandOptimizer.Register(__instance, false);
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
            ItemStandOptimizer.Register(__instance, true);
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
