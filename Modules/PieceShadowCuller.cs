using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Piece Shadow Culler for OptiHouse.
    /// Disables real-time shadow casting on small structural pieces and props beyond PieceShadowCullDistance
    /// while preserving original ShadowCastingMode up close. Never disables mesh renderers or colliders.
    /// </summary>
    public class PieceShadowCuller : MonoBehaviour
    {
        public static PieceShadowCuller? Instance { get; private set; }

        private class TrackedPiece
        {
            public int PieceId;
            public Piece Piece = null!;
            public Transform Transform = null!;
            public MeshRenderer Renderer = null!;
            public ShadowCastingMode OrigShadowMode;
            public bool ShadowsOff;
        }

        private static readonly List<TrackedPiece> _trackedPieces = new List<TrackedPiece>();
        private static readonly Dictionary<int, List<TrackedPiece>> _pieceLookup = new Dictionary<int, List<TrackedPiece>>();

        private static int _culledShadowCount;
        private int _currentIndex;
        private bool _wasOptimizationActive = true;

        public static int TrackedPieceCount => _trackedPieces.Count;
        public static int CulledShadowCount => _culledShadowCount;

        private void Awake()
        {
            Instance = this;
            ScanExistingPieces();
        }

        private void OnDestroy()
        {
            RestoreAllShadows();
            _trackedPieces.Clear();
            _pieceLookup.Clear();
            _culledShadowCount = 0;
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
                            ModConfig.EnableStructuralOptimization != null &&
                            ModConfig.EnableStructuralOptimization.Value &&
                            ModConfig.EnablePieceShadowCulling != null &&
                            ModConfig.EnablePieceShadowCulling.Value;

            if (!isActive)
            {
                if (_wasOptimizationActive)
                {
                    _wasOptimizationActive = false;
                    RestoreAllShadows();
                }
                return;
            }

            _wasOptimizationActive = true;

            int total = _trackedPieces.Count;
            if (total == 0) return;

            Vector3 refPos = OptimizationHelpers.GetReferencePosition();
            float cullDistance = ModConfig.PieceShadowCullDistance != null
                ? ModConfig.PieceShadowCullDistance.Value
                : 18.0f;

            float cullDistSq = cullDistance * cullDistance;
            float restoreDistance = Mathf.Max(0f, cullDistance - 3.0f);
            float restoreDistSq = restoreDistance * restoreDistance;

            int batchSize = Mathf.Min(300, total);
            for (int i = 0; i < batchSize; i++)
            {
                _currentIndex++;
                if (_currentIndex >= _trackedPieces.Count)
                {
                    _currentIndex = 0;
                }

                TrackedPiece item = _trackedPieces[_currentIndex];
                if (item.Renderer == null || item.Transform == null || item.Piece == null)
                {
                    if (item.ShadowsOff && _culledShadowCount > 0) _culledShadowCount--;
                    _pieceLookup.Remove(item.PieceId);
                    _trackedPieces.RemoveAt(_currentIndex);
                    if (_currentIndex > 0) _currentIndex--;
                    continue;
                }

                if (!item.Renderer.enabled || !item.Renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                float sqrDist = (item.Transform.position - refPos).sqrMagnitude;

                if (sqrDist > cullDistSq)
                {
                    if (!item.ShadowsOff)
                    {
                        item.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                        item.ShadowsOff = true;
                        _culledShadowCount++;
                    }
                }
                else if (sqrDist < restoreDistSq)
                {
                    if (item.ShadowsOff)
                    {
                        item.Renderer.shadowCastingMode = item.OrigShadowMode;
                        item.ShadowsOff = false;
                        if (_culledShadowCount > 0) _culledShadowCount--;
                    }
                }
            }
        }

        public static void RegisterPiece(Piece piece)
        {
            if (!OptimizationHelpers.IsValidWorldPiece(piece)) return;

            int pieceId = piece.GetInstanceID();
            if (_pieceLookup.ContainsKey(pieceId)) return;

            MeshRenderer[] renderers = piece.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            List<TrackedPiece>? list = null;

            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer mr = renderers[i];
                if (mr == null || mr.shadowCastingMode == ShadowCastingMode.Off) continue;

                if (IsSmallStructuralPiece(piece, mr))
                {
                    var item = new TrackedPiece
                    {
                        PieceId = pieceId,
                        Piece = piece,
                        Transform = mr.transform,
                        Renderer = mr,
                        OrigShadowMode = mr.shadowCastingMode,
                        ShadowsOff = false
                    };

                    _trackedPieces.Add(item);

                    if (list == null)
                    {
                        list = new List<TrackedPiece>();
                        _pieceLookup[pieceId] = list;
                    }
                    list.Add(item);
                }
            }
        }

        public static void UnregisterPiece(Piece piece)
        {
            if (piece == null) return;

            int pieceId = piece.GetInstanceID();
            if (_pieceLookup.TryGetValue(pieceId, out List<TrackedPiece> list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    TrackedPiece item = list[i];
                    if (item.ShadowsOff && _culledShadowCount > 0) _culledShadowCount--;
                    if (item.Renderer != null && item.ShadowsOff)
                    {
                        item.Renderer.shadowCastingMode = item.OrigShadowMode;
                        item.ShadowsOff = false;
                    }
                    _trackedPieces.Remove(item);
                }
                _pieceLookup.Remove(pieceId);
            }
        }

        public static void RestoreAllShadows()
        {
            for (int i = 0; i < _trackedPieces.Count; i++)
            {
                TrackedPiece item = _trackedPieces[i];
                if (item.ShadowsOff && item.Renderer != null)
                {
                    item.Renderer.shadowCastingMode = item.OrigShadowMode;
                    item.ShadowsOff = false;
                }
            }
            _culledShadowCount = 0;
        }

        public static bool IsSmallStructuralPiece(Piece piece, MeshRenderer renderer)
        {
            if (piece == null || renderer == null) return false;

            string name = piece.gameObject.name;

            if (OptimizationHelpers.ContainsIgnoreCase(name, "wall") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "roof") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "floor") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "gate") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "stone_") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "blackmarble_") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "portal") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "pillar") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "chimney") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "stair"))
            {
                return false;
            }

            if (OptimizationHelpers.ContainsIgnoreCase(name, "beam") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "pole") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "post") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "bar") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "cage") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "grate") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "fence") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "ladder") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "chain") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "stand") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "decor") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "chair") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "stool") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "bench") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "table") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "sign") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "rug") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "sconce") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "lantern") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "banner") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "chest"))
            {
                return true;
            }

            Bounds bounds = renderer.bounds;
            float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            return maxDim <= 2.5f;
        }
    }

    #region Harmony Patches

    [HarmonyPatch(typeof(Piece), "Awake")]
    public static class Piece_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Piece __instance)
        {
            PieceShadowCuller.RegisterPiece(__instance);
        }
    }

    [HarmonyPatch(typeof(Piece), "OnDestroy")]
    public static class Piece_OnDestroy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Piece __instance)
        {
            PieceShadowCuller.UnregisterPiece(__instance);
        }
    }

    #endregion
}
