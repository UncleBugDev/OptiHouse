using System.Collections.Generic;
using OptiHouse.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Piece Shadow Culler for OptiHouse v2.0.
    /// Groups all shadow-casting MeshRenderers per Piece and uses O(1) swap-back unregistration
    /// to eliminate O(N^2) portal/zone-unload freezes. Preserves original ShadowCastingMode.
    /// </summary>
    public class PieceShadowCuller : MonoBehaviour
    {
        public static PieceShadowCuller? Instance { get; private set; }

        private struct RendererShadowBackup
        {
            public MeshRenderer Renderer;
            public ShadowCastingMode OrigMode;
            public ReflectionProbeUsage OrigProbeUsage;
        }

        private class TrackedShadowPiece
        {
            public int PieceId;
            public Piece Piece = null!;
            public Transform Transform = null!;
            public readonly List<RendererShadowBackup> Renderers = new List<RendererShadowBackup>(2);
            public bool ShadowsOff;

            public void SetShadowsOff()
            {
                if (ShadowsOff) return;
                for (int i = 0; i < Renderers.Count; i++)
                {
                    MeshRenderer mr = Renderers[i].Renderer;
                    if (mr != null)
                    {
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    }
                }
                ShadowsOff = true;
            }

            public void RestoreShadows()
            {
                if (!ShadowsOff) return;
                for (int i = 0; i < Renderers.Count; i++)
                {
                    MeshRenderer mr = Renderers[i].Renderer;
                    if (mr != null)
                    {
                        mr.shadowCastingMode = Renderers[i].OrigMode;
                        mr.reflectionProbeUsage = Renderers[i].OrigProbeUsage;
                    }
                }
                ShadowsOff = false;
            }
        }

        private static readonly List<TrackedShadowPiece> _pieces = new List<TrackedShadowPiece>();
        private static readonly Dictionary<int, int> _indexMap = new Dictionary<int, int>();

        private static int _culledShadowCount;
        private int _currentIndex;
        private bool _wasOptimizationActive = true;

        public static int TrackedPieceCount => _pieces.Count;
        public static int CulledShadowCount => _culledShadowCount;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            RestoreAllShadows();
            _pieces.Clear();
            _indexMap.Clear();
            _culledShadowCount = 0;
            if (Instance == this)
            {
                Instance = null;
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

            int total = _pieces.Count;
            if (total == 0) return;

            Vector3 refPos = OptimizationHelpers.GetReferencePosition();
            float cullDistance = ModConfig.PieceShadowCullDistance != null
                ? ModConfig.PieceShadowCullDistance.Value
                : 18.0f;

            float cullDistSq = cullDistance * cullDistance;
            float restoreDistance = Mathf.Max(0f, cullDistance - 3.0f);
            float restoreDistSq = restoreDistance * restoreDistance;

            int batchSize = Mathf.Min(350, total);
            for (int i = 0; i < batchSize; i++)
            {
                if (_pieces.Count == 0) break;
                _currentIndex++;
                if (_currentIndex >= _pieces.Count) _currentIndex = 0;

                TrackedShadowPiece item = _pieces[_currentIndex];
                if (item.Piece == null || item.Transform == null)
                {
                    SwapRemoveAt(_currentIndex);
                    if (_currentIndex > 0) _currentIndex--;
                    continue;
                }

                if (!item.Piece.gameObject.activeInHierarchy) continue;

                float sqrDist = (item.Transform.position - refPos).sqrMagnitude;

                if (sqrDist > cullDistSq)
                {
                    if (!item.ShadowsOff)
                    {
                        item.SetShadowsOff();
                        _culledShadowCount++;
                    }
                }
                else if (sqrDist < restoreDistSq)
                {
                    if (item.ShadowsOff)
                    {
                        item.RestoreShadows();
                        if (_culledShadowCount > 0) _culledShadowCount--;
                    }
                }
            }
        }

        public static void RegisterPiece(Piece piece, Renderer[] allRenderers)
        {
            int pieceId = piece.GetInstanceID();
            if (_indexMap.ContainsKey(pieceId)) return;

            bool? nameEligible = IsSmallPieceByName(piece);
            if (nameEligible == false) return;

            TrackedShadowPiece? tracked = null;

            for (int i = 0; i < allRenderers.Length; i++)
            {
                if (allRenderers[i] is not MeshRenderer mr || mr == null || mr.shadowCastingMode == ShadowCastingMode.Off)
                {
                    continue;
                }

                bool eligible = nameEligible == true;
                if (!eligible)
                {
                    Bounds bounds = mr.bounds;
                    float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                    eligible = maxDim <= 2.5f;
                }

                if (eligible)
                {
                    tracked ??= new TrackedShadowPiece
                    {
                        PieceId = pieceId,
                        Piece = piece,
                        Transform = piece.transform,
                        ShadowsOff = false
                    };
                    tracked.Renderers.Add(new RendererShadowBackup
                    {
                        Renderer = mr,
                        OrigMode = mr.shadowCastingMode,
                        OrigProbeUsage = mr.reflectionProbeUsage
                    });
                }
            }

            if (tracked != null && tracked.Renderers.Count > 0)
            {
                int idx = _pieces.Count;
                _pieces.Add(tracked);
                _indexMap[pieceId] = idx;
            }
        }

        public static void UnregisterPieceById(int pieceId)
        {
            if (!_indexMap.TryGetValue(pieceId, out int idx)) return;
            SwapRemoveAt(idx);
        }

        private static void SwapRemoveAt(int idx)
        {
            TrackedShadowPiece removed = _pieces[idx];
            if (removed.ShadowsOff)
            {
                removed.RestoreShadows();
                if (_culledShadowCount > 0) _culledShadowCount--;
            }
            _indexMap.Remove(removed.PieceId);

            int lastIdx = _pieces.Count - 1;
            if (idx < lastIdx)
            {
                TrackedShadowPiece moved = _pieces[lastIdx];
                _pieces[idx] = moved;
                _indexMap[moved.PieceId] = idx;
            }
            _pieces.RemoveAt(lastIdx);
        }

        public static void RestoreAllShadows()
        {
            for (int i = 0; i < _pieces.Count; i++)
            {
                if (_pieces[i].ShadowsOff)
                {
                    _pieces[i].RestoreShadows();
                }
            }
            _culledShadowCount = 0;
        }

        /// <summary>
        /// Returns true if definitely small prop, false if large silhouette piece, null to fallback to bounds check.
        /// </summary>
        private static bool? IsSmallPieceByName(Piece piece)
        {
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

            return null;
        }
    }
}
