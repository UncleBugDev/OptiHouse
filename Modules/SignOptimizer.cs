using System;
using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Storage Warehouse Sign Optimizer for OptiHouse v2.0.
    /// 1. World-Space Canvas Culling: Unity Canvas / TextMeshProUGUI do NOT inherit from UnityEngine.Renderer,
    ///    so 200-500 warehouse signs normally render through solid walls up to 128m away and trigger UI Canvas batching.
    ///    Disables Canvas.enabled when beyond SignCullDistance (default 16m) or behind the camera!
    /// 2. Stops Valheim's 2-second Sign.UpdateText -> UpdateViewPermission (RelationsManager.CheckPermissionAsync + delegate GC)
    ///    spam whenever the sign's ZDO DataRevision has not changed.
    /// Uses O(1) swap-back unregistration.
    /// </summary>
    public class SignOptimizer : MonoBehaviour
    {
        public static SignOptimizer? Instance { get; private set; }

        private class TrackedSign
        {
            public int SignId;
            public Sign Sign = null!;
            public Transform Transform = null!;
            public Canvas[] Canvases = Array.Empty<Canvas>();
            public readonly List<Canvas> DisabledCanvases = new List<Canvas>();
            public bool IsCulled;

            public void Cull()
            {
                if (IsCulled) return;
                DisabledCanvases.Clear();
                for (int i = 0; i < Canvases.Length; i++)
                {
                    Canvas c = Canvases[i];
                    if (c != null && c.enabled)
                    {
                        c.enabled = false;
                        DisabledCanvases.Add(c);
                    }
                }
                IsCulled = true;
            }

            public void Restore()
            {
                if (!IsCulled) return;
                for (int i = 0; i < DisabledCanvases.Count; i++)
                {
                    if (DisabledCanvases[i] != null)
                    {
                        DisabledCanvases[i].enabled = true;
                    }
                }
                DisabledCanvases.Clear();
                IsCulled = false;
            }
        }

        private static readonly List<TrackedSign> _signs = new List<TrackedSign>();
        private static readonly Dictionary<int, int> _indexMap = new Dictionary<int, int>();

        private static int _culledSignCount;
        private int _currentIndex;
        private bool _wasActive = true;

        public static int TotalSigns => _signs.Count;
        public static int CulledSigns => _culledSignCount;
        public static long PermissionChecksSkipped { get; internal set; }

        private void Awake()
        {
            Instance = this;
            ScanExistingSigns();
        }

        private void OnDestroy()
        {
            RestoreAllSigns();
            _signs.Clear();
            _indexMap.Clear();
            _culledSignCount = 0;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static void ScanExistingSigns()
        {
            Sign[] signs = FindObjectsByType<Sign>(FindObjectsSortMode.None);
            if (signs != null)
            {
                for (int i = 0; i < signs.Length; i++)
                {
                    if (signs[i] != null) RegisterSign(signs[i]);
                }
            }
        }

        public static void RegisterSign(Sign sign)
        {
            if (!OptimizationHelpers.IsValidWorldObject(sign)) return;

            int id = sign.GetInstanceID();
            if (_indexMap.ContainsKey(id)) return;

            Canvas[] canvases = sign.GetComponentsInChildren<Canvas>(true);
            if (canvases == null || canvases.Length == 0) return;

            var entry = new TrackedSign
            {
                SignId = id,
                Sign = sign,
                Transform = sign.transform,
                Canvases = canvases,
                IsCulled = false
            };

            int idx = _signs.Count;
            _signs.Add(entry);
            _indexMap[id] = idx;
        }

        public static void UnregisterFromPiece(Piece piece)
        {
            if (piece == null) return;
            Sign sign = piece.GetComponent<Sign>();
            if (sign != null)
            {
                RemoveById(sign.GetInstanceID());
            }
        }

        private static void RemoveById(int signId)
        {
            if (!_indexMap.TryGetValue(signId, out int idx)) return;
            SwapRemoveAt(idx);
        }

        private static void SwapRemoveAt(int idx)
        {
            TrackedSign removed = _signs[idx];
            if (removed.IsCulled)
            {
                removed.Restore();
                if (_culledSignCount > 0) _culledSignCount--;
            }
            _indexMap.Remove(removed.SignId);

            int lastIdx = _signs.Count - 1;
            if (idx < lastIdx)
            {
                TrackedSign moved = _signs[lastIdx];
                _signs[idx] = moved;
                _indexMap[moved.SignId] = idx;
            }
            _signs.RemoveAt(lastIdx);
        }

        private void Update()
        {
            bool isActive = OptiHousePlugin.IsOptimizationActive &&
                            ModConfig.EnableSignOptimization != null &&
                            ModConfig.EnableSignOptimization.Value;

            if (!isActive)
            {
                if (_wasActive)
                {
                    _wasActive = false;
                    RestoreAllSigns();
                }
                return;
            }

            _wasActive = true;

            int total = _signs.Count;
            if (total == 0) return;

            if (!OptimizationHelpers.TryGetCameraView(out Vector3 camPos, out Vector3 camForward))
            {
                return;
            }

            float cullDist = ModConfig.SignCullDistance != null ? ModConfig.SignCullDistance.Value : 16.0f;
            float cullDistSq = cullDist * cullDist;
            float restoreDist = Mathf.Max(2f, cullDist - 2.5f);
            float restoreDistSq = restoreDist * restoreDist;

            int batchSize = Mathf.Min(150, total);
            for (int i = 0; i < batchSize; i++)
            {
                if (_signs.Count == 0) break;
                _currentIndex++;
                if (_currentIndex >= _signs.Count) _currentIndex = 0;

                TrackedSign item = _signs[_currentIndex];
                if (item.Sign == null || item.Transform == null)
                {
                    SwapRemoveAt(_currentIndex);
                    if (_currentIndex > 0) _currentIndex--;
                    continue;
                }

                if (!item.Sign.gameObject.activeInHierarchy) continue;

                Vector3 pos = item.Transform.position;
                float sqrDist = (pos - camPos).sqrMagnitude;
                bool behindCam = OptimizationHelpers.IsBehindCamera(pos, camPos, camForward, 25f);

                if (sqrDist > cullDistSq || behindCam)
                {
                    if (!item.IsCulled)
                    {
                        item.Cull();
                        _culledSignCount++;
                    }
                }
                else if (sqrDist < restoreDistSq && !behindCam)
                {
                    if (item.IsCulled)
                    {
                        item.Restore();
                        if (_culledSignCount > 0) _culledSignCount--;
                    }
                }
            }
        }

        public static void RestoreAllSigns()
        {
            for (int i = 0; i < _signs.Count; i++)
            {
                if (_signs[i].IsCulled)
                {
                    _signs[i].Restore();
                }
            }
            _culledSignCount = 0;
        }
    }

    [HarmonyPatch(typeof(Sign), "Awake")]
    public static class Sign_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Sign __instance)
        {
            SignOptimizer.RegisterSign(__instance);
        }
    }

    [HarmonyPatch(typeof(Sign), "UpdateText")]
    public static class Sign_UpdateText_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ZNetView ___m_nview, uint ___m_lastRevision)
        {
            if (!OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnableSignOptimization == null ||
                !ModConfig.EnableSignOptimization.Value)
            {
                return true;
            }

            if (___m_nview == null || !___m_nview.IsValid())
            {
                return false;
            }

            // Skip redundant 2s RelationsManager.CheckPermissionAsync + delegate allocation when text ZDO hasn't changed
            if (___m_lastRevision != uint.MaxValue && ___m_lastRevision == ___m_nview.GetZDO().DataRevision)
            {
                SignOptimizer.PermissionChecksSkipped++;
                return false;
            }

            return true;
        }
    }
}
