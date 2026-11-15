using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Piece Shadow Culler for OptiHouse.
    /// Automatically manages real-time shadow casting on small structural and decorative elements
    /// (beams &lt;= 2m, iron bars, grates, decorative furniture, poles).
    /// Disables shadow casting beyond a configurable distance (default 15m), drastically slashing
    /// shadow map draw calls in high-density bases while preserving visual quality up close.
    /// </summary>
    public class PieceShadowCuller : MonoBehaviour
    {
        private static PieceShadowCuller? _instance;
        public static PieceShadowCuller? Instance => _instance;

        private class TrackedPiece
        {
            public Piece Piece = null!;
            public Transform Transform = null!;
            public MeshRenderer Renderer = null!;
            public bool ShadowsOff;
        }

        private static readonly List<TrackedPiece> _trackedPieces = new List<TrackedPiece>();
        private static readonly Dictionary<int, List<TrackedPiece>> _pieceLookup = new Dictionary<int, List<TrackedPiece>>();

        private int _currentIndex;
        private bool _wasOptimizationActive = true;

        public static int TrackedPieceCount => _trackedPieces.Count;

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            RestoreAllShadows();
            _trackedPieces.Clear();
            _pieceLookup.Clear();
            if (_instance == this)
            {
                _instance = null;
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

            Vector3 refPos = GetReferencePosition();
            float cullDistance = ModConfig.PieceShadowCullDistance != null
                ? ModConfig.PieceShadowCullDistance.Value
                : 15.0f;

            float cullDistSq = cullDistance * cullDistance;
            // 2.5m hysteresis margin prevents rapid shadow toggle flickering at boundary
            float restoreDistance = Mathf.Max(0f, cullDistance - 2.5f);
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
                    }
                }
                else if (sqrDist < restoreDistSq)
                {
                    if (item.ShadowsOff)
                    {
                        item.Renderer.shadowCastingMode = ShadowCastingMode.On;
                        item.ShadowsOff = false;
                    }
                }
            }
        }

        public static void RegisterPiece(Piece piece)
        {
            if (piece == null) return;

            MeshRenderer[] renderers = piece.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            int pieceId = piece.GetInstanceID();
            List<TrackedPiece>? list = null;

            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer mr = renderers[i];
                if (mr == null) continue;

                if (IsSmallStructuralPiece(piece, mr))
                {
                    var item = new TrackedPiece
                    {
                        Piece = piece,
                        Transform = mr.transform,
                        Renderer = mr,
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
                    _trackedPieces.Remove(list[i]);
                }
                _pieceLookup.Remove(pieceId);
            }
        }

        public static void RestoreAllShadows()
        {
            for (int i = 0; i < _trackedPieces.Count; i++)
            {
                TrackedPiece item = _trackedPieces[i];
                if (item.Renderer != null)
                {
                    item.Renderer.shadowCastingMode = ShadowCastingMode.On;
                    item.ShadowsOff = false;
                }
            }
        }

        public static bool IsSmallStructuralPiece(Piece piece, MeshRenderer renderer)
        {
            if (piece == null || renderer == null) return false;

            string name = piece.gameObject.name.ToLowerInvariant();

            // Explicit exclusions: Large walls, roofs, floors, ground pieces, pillars
            if (name.Contains("wall") ||
                name.Contains("roof") ||
                name.Contains("floor") ||
                name.Contains("gate") ||
                name.Contains("stone_") ||
                name.Contains("blackmarble_") ||
                name.Contains("portal") ||
                name.Contains("pillar") ||
                name.Contains("chimney") ||
                name.Contains("stair"))
            {
                return false;
            }

            // Small structural pieces by name:
            if (name.Contains("beam") ||
                name.Contains("pole") ||
                name.Contains("post") ||
                name.Contains("bar") ||
                name.Contains("cage") ||
                name.Contains("grate") ||
                name.Contains("fence") ||
                name.Contains("ladder") ||
                name.Contains("chain") ||
                name.Contains("stand") ||
                name.Contains("decor") ||
                name.Contains("chair") ||
                name.Contains("stool") ||
                name.Contains("bench") ||
                name.Contains("table") ||
                name.Contains("sign") ||
                name.Contains("rug") ||
                name.Contains("sconce") ||
                name.Contains("lantern") ||
                name.Contains("banner"))
            {
                return true;
            }

            // Fallback bounding box size check: max dimension <= 2.8m (1m, 2m beams, grates)
            Bounds bounds = renderer.bounds;
            float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            return maxDim <= 2.8f;
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

    /// <summary>
    /// Attach PieceShadowCuller to the mod plugin GameObject on startup.
    /// </summary>
    [HarmonyPatch(typeof(OptiHousePlugin), "Awake")]
    public static class PieceShadowCuller_Plugin_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(OptiHousePlugin __instance)
        {
            if (__instance != null && __instance.gameObject.GetComponent<PieceShadowCuller>() == null)
            {
                __instance.gameObject.AddComponent<PieceShadowCuller>();
            }
        }
    }

    /// <summary>
    /// Intercepts Piece.Awake to register small structural elements for shadow culling.
    /// </summary>
    [HarmonyPatch(typeof(Piece), "Awake")]
    public static class Piece_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Piece __instance)
        {
            PieceShadowCuller.RegisterPiece(__instance);
        }
    }

    /// <summary>
    /// Intercepts Piece.OnDestroy to cleanly unregister deleted pieces.
    /// </summary>
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
