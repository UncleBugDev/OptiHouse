using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Cloth Optimizer for OptiHouse.
    /// Pauses Unity Cloth vertex physics solvers on distant banners, flags, and tapestries
    /// beyond BannerClothCullDistance (default 16m with 3m hysteresis).
    /// </summary>
    public class ClothOptimizer : MonoBehaviour
    {
        public static ClothOptimizer? Instance { get; private set; }

        private class TrackedCloth
        {
            public int PieceId;
            public Piece Piece = null!;
            public Cloth Cloth = null!;
            public Transform Transform = null!;
            public bool IsCulled;
        }

        private static readonly List<TrackedCloth> _trackedCloths = new List<TrackedCloth>();
        private static readonly Dictionary<int, List<TrackedCloth>> _pieceLookup = new Dictionary<int, List<TrackedCloth>>();

        private static int _culledClothCount;
        private int _currentIndex;
        private bool _wasOptimizationActive = true;

        public static int TotalClothCount => _trackedCloths.Count;
        public static int CulledClothCount => _culledClothCount;

        private void Awake()
        {
            Instance = this;
            ScanExistingPieces();
        }

        private void OnDestroy()
        {
            RestoreAllCloths();
            _trackedCloths.Clear();
            _pieceLookup.Clear();
            _culledClothCount = 0;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static void ScanExistingPieces()
        {
            Piece[] pieces = FindObjectsByType<Piece>(FindObjectsSortMode.None);
            if (pieces != null)
            {
                for (int i = 0; i < pieces.Length; i++)
                {
                    if (pieces[i] != null)
                    {
                        RegisterPiece(pieces[i]);
                    }
                }
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

            int total = _trackedCloths.Count;
            if (total == 0) return;

            Vector3 refPos = OptimizationHelpers.GetReferencePosition();
            float cullDistance = ModConfig.BannerClothCullDistance != null
                ? ModConfig.BannerClothCullDistance.Value
                : 16.0f;

            float cullDistSq = cullDistance * cullDistance;
            float restoreDistance = Mathf.Max(0f, cullDistance - 3.0f);
            float restoreDistSq = restoreDistance * restoreDistance;

            int batchSize = Mathf.Min(50, total);
            for (int i = 0; i < batchSize; i++)
            {
                _currentIndex++;
                if (_currentIndex >= _trackedCloths.Count)
                {
                    _currentIndex = 0;
                }

                TrackedCloth item = _trackedCloths[_currentIndex];
                if (item.Cloth == null || item.Transform == null || item.Piece == null)
                {
                    if (item.IsCulled && _culledClothCount > 0) _culledClothCount--;
                    _pieceLookup.Remove(item.PieceId);
                    _trackedCloths.RemoveAt(_currentIndex);
                    if (_currentIndex > 0) _currentIndex--;
                    continue;
                }

                if (!item.Cloth.gameObject.activeInHierarchy)
                {
                    continue;
                }

                float sqrDist = (item.Transform.position - refPos).sqrMagnitude;

                if (sqrDist > cullDistSq)
                {
                    if (!item.IsCulled && item.Cloth.enabled)
                    {
                        item.Cloth.enabled = false;
                        item.IsCulled = true;
                        _culledClothCount++;
                    }
                }
                else if (sqrDist < restoreDistSq)
                {
                    if (item.IsCulled)
                    {
                        item.Cloth.enabled = true;
                        item.IsCulled = false;
                        if (_culledClothCount > 0) _culledClothCount--;
                    }
                }
            }
        }

        public static void RegisterPiece(Piece piece)
        {
            if (!OptimizationHelpers.IsValidWorldPiece(piece)) return;

            int pieceId = piece.GetInstanceID();
            if (_pieceLookup.ContainsKey(pieceId)) return;

            Cloth[] cloths = piece.GetComponentsInChildren<Cloth>(true);
            if (cloths == null || cloths.Length == 0) return;

            List<TrackedCloth> list = new List<TrackedCloth>(cloths.Length);
            Vector3 refPos = OptimizationHelpers.GetReferencePosition();
            float cullDistance = ModConfig.BannerClothCullDistance != null
                ? ModConfig.BannerClothCullDistance.Value
                : 16.0f;
            float cullDistSq = cullDistance * cullDistance;

            bool isOptActive = OptiHousePlugin.IsOptimizationActive &&
                               ModConfig.EnableClothOptimization != null &&
                               ModConfig.EnableClothOptimization.Value;

            for (int i = 0; i < cloths.Length; i++)
            {
                Cloth c = cloths[i];
                if (c == null) continue;

                var item = new TrackedCloth
                {
                    PieceId = pieceId,
                    Piece = piece,
                    Cloth = c,
                    Transform = c.transform,
                    IsCulled = false
                };

                if (isOptActive && c.enabled && (item.Transform.position - refPos).sqrMagnitude > cullDistSq)
                {
                    item.Cloth.enabled = false;
                    item.IsCulled = true;
                    _culledClothCount++;
                }

                _trackedCloths.Add(item);
                list.Add(item);
            }

            if (list.Count > 0)
            {
                _pieceLookup[pieceId] = list;
            }
        }

        public static void UnregisterPiece(Piece piece)
        {
            if (piece == null) return;

            int pieceId = piece.GetInstanceID();
            if (_pieceLookup.TryGetValue(pieceId, out List<TrackedCloth> list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    TrackedCloth item = list[i];
                    if (item.IsCulled)
                    {
                        if (item.Cloth != null) item.Cloth.enabled = true;
                        item.IsCulled = false;
                        if (_culledClothCount > 0) _culledClothCount--;
                    }
                    _trackedCloths.Remove(item);
                }
                _pieceLookup.Remove(pieceId);
            }
        }

        public static void RestoreAllCloths()
        {
            for (int i = 0; i < _trackedCloths.Count; i++)
            {
                TrackedCloth item = _trackedCloths[i];
                if (item.IsCulled)
                {
                    if (item.Cloth != null) item.Cloth.enabled = true;
                    item.IsCulled = false;
                }
            }
            _culledClothCount = 0;
        }
    }

    #region Harmony Patches

    [HarmonyPatch(typeof(Piece), "Awake")]
    public static class ClothOptimizer_Piece_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Piece __instance)
        {
            ClothOptimizer.RegisterPiece(__instance);
        }
    }

    [HarmonyPatch(typeof(Piece), "OnDestroy")]
    public static class ClothOptimizer_Piece_OnDestroy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Piece __instance)
        {
            ClothOptimizer.UnregisterPiece(__instance);
        }
    }

    #endregion
}
