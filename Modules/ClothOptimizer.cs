using System.Collections.Generic;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Cloth Optimizer for OptiHouse v2.0.
    /// Pauses Unity Cloth vertex physics solvers on banners, flags, and tapestries when beyond
    /// BannerClothCullDistance (default 16m) OR behind the player's camera frustum (&gt;6m behind).
    /// Uses O(1) swap-back unregistration via PieceRegistry.
    /// </summary>
    public class ClothOptimizer : MonoBehaviour
    {
        public static ClothOptimizer? Instance { get; private set; }

        private class TrackedClothPiece
        {
            public int PieceId;
            public Piece Piece = null!;
            public Transform Transform = null!;
            public Cloth[] Cloths = null!;
            public readonly List<Cloth> DisabledCloths = new List<Cloth>(2);
            public bool IsCulled;

            public void Cull()
            {
                if (IsCulled) return;
                DisabledCloths.Clear();
                for (int i = 0; i < Cloths.Length; i++)
                {
                    Cloth c = Cloths[i];
                    if (c != null && c.enabled)
                    {
                        c.enabled = false;
                        DisabledCloths.Add(c);
                    }
                }
                IsCulled = true;
            }

            public void Restore()
            {
                if (!IsCulled) return;
                for (int i = 0; i < DisabledCloths.Count; i++)
                {
                    if (DisabledCloths[i] != null)
                    {
                        DisabledCloths[i].enabled = true;
                    }
                }
                DisabledCloths.Clear();
                IsCulled = false;
            }
        }

        private static readonly List<TrackedClothPiece> _pieces = new List<TrackedClothPiece>();
        private static readonly Dictionary<int, int> _indexMap = new Dictionary<int, int>();

        private static int _culledClothCount;
        private int _currentIndex;
        private bool _wasOptimizationActive = true;

        public static int TotalClothCount => _pieces.Count;
        public static int CulledClothCount => _culledClothCount;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            RestoreAllCloths();
            _pieces.Clear();
            _indexMap.Clear();
            _culledClothCount = 0;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            bool isActive = OptiHousePlugin.IsOptimizationActive &&
                            ModConfig.EnableClothOptimization != null &&
                            ModConfig.EnableClothOptimization.Value;

            if (!isActive)
            {
                if (_wasOptimizationActive)
                {
                    _wasOptimizationActive = false;
                    RestoreAllCloths();
                }
                return;
            }

            _wasOptimizationActive = true;

            int total = _pieces.Count;
            if (total == 0) return;

            if (!OptimizationHelpers.TryGetCameraView(out Vector3 camPos, out Vector3 camForward))
            {
                return;
            }

            float cullDistance = ModConfig.BannerClothCullDistance != null
                ? ModConfig.BannerClothCullDistance.Value
                : 16.0f;

            float cullDistSq = cullDistance * cullDistance;
            float restoreDistance = Mathf.Max(0f, cullDistance - 3.0f);
            float restoreDistSq = restoreDistance * restoreDistance;

            int batchSize = Mathf.Min(60, total);
            for (int i = 0; i < batchSize; i++)
            {
                if (_pieces.Count == 0) break;
                _currentIndex++;
                if (_currentIndex >= _pieces.Count) _currentIndex = 0;

                TrackedClothPiece item = _pieces[_currentIndex];
                if (item.Piece == null || item.Transform == null)
                {
                    SwapRemoveAt(_currentIndex);
                    if (_currentIndex > 0) _currentIndex--;
                    continue;
                }

                if (!item.Piece.gameObject.activeInHierarchy) continue;

                Vector3 pos = item.Transform.position;
                float sqrDist = (pos - camPos).sqrMagnitude;
                bool behindCam = OptimizationHelpers.IsBehindCamera(pos, camPos, camForward, 36f);

                if (sqrDist > cullDistSq || behindCam)
                {
                    if (!item.IsCulled)
                    {
                        item.Cull();
                        _culledClothCount++;
                    }
                }
                else if (sqrDist < restoreDistSq && !behindCam)
                {
                    if (item.IsCulled)
                    {
                        item.Restore();
                        if (_culledClothCount > 0) _culledClothCount--;
                    }
                }
            }
        }

        public static void RegisterPiece(Piece piece)
        {
            int pieceId = piece.GetInstanceID();
            if (_indexMap.ContainsKey(pieceId)) return;

            Cloth[] cloths = piece.GetComponentsInChildren<Cloth>(true);
            if (cloths == null || cloths.Length == 0) return;

            var item = new TrackedClothPiece
            {
                PieceId = pieceId,
                Piece = piece,
                Transform = piece.transform,
                Cloths = cloths,
                IsCulled = false
            };

            bool isOptActive = OptiHousePlugin.IsOptimizationActive &&
                               ModConfig.EnableClothOptimization != null &&
                               ModConfig.EnableClothOptimization.Value;

            if (isOptActive && OptimizationHelpers.TryGetCameraView(out Vector3 camPos, out Vector3 camForward))
            {
                float cullDistance = ModConfig.BannerClothCullDistance != null
                    ? ModConfig.BannerClothCullDistance.Value
                    : 16.0f;
                Vector3 pos = item.Transform.position;
                if ((pos - camPos).sqrMagnitude > cullDistance * cullDistance ||
                    OptimizationHelpers.IsBehindCamera(pos, camPos, camForward, 36f))
                {
                    item.Cull();
                    _culledClothCount++;
                }
            }

            int idx = _pieces.Count;
            _pieces.Add(item);
            _indexMap[pieceId] = idx;
        }

        public static void UnregisterPieceById(int pieceId)
        {
            if (!_indexMap.TryGetValue(pieceId, out int idx)) return;
            SwapRemoveAt(idx);
        }

        private static void SwapRemoveAt(int idx)
        {
            TrackedClothPiece removed = _pieces[idx];
            if (removed.IsCulled)
            {
                removed.Restore();
                if (_culledClothCount > 0) _culledClothCount--;
            }
            _indexMap.Remove(removed.PieceId);

            int lastIdx = _pieces.Count - 1;
            if (idx < lastIdx)
            {
                TrackedClothPiece moved = _pieces[lastIdx];
                _pieces[idx] = moved;
                _indexMap[moved.PieceId] = idx;
            }
            _pieces.RemoveAt(lastIdx);
        }

        public static void RestoreAllCloths()
        {
            for (int i = 0; i < _pieces.Count; i++)
            {
                if (_pieces[i].IsCulled)
                {
                    _pieces[i].Restore();
                }
            }
            _culledClothCount = 0;
        }
    }
}
