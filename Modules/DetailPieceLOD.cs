using System;
using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Structural Detail Piece LOD System for OptiHouse.
    /// Culls renderers of purely ornamental micro-pieces (carvings, dragonheads, brackets,
    /// cross beams, 1m micro-poles/beams) beyond DetailPieceCullDistance (default 38m).
    ///
    /// VISUAL & SAFETY GUARANTEES:
    /// 1. NEVER culls walls, floors, stairs, doors, portals, pillars, or ANY roof/ridge tiles
    ///    so castle silhouettes and roof peaks remain 100% solid and hole-free.
    /// 2. Snapshots only enabled renderers when culling so WearNTear damage meshes remain intact.
    /// 3. Automatically restores all detail renderers when holding a hammer, hoe, or build tool.
    /// </summary>
    public class DetailPieceLOD : MonoBehaviour
    {
        public static DetailPieceLOD? Instance { get; private set; }

        private class TrackedDetailPiece
        {
            public int InstanceId;
            public Piece Piece = null!;
            public Transform Transform = null!;
            public Renderer[] AllRenderers = Array.Empty<Renderer>();
            public readonly List<Renderer> CulledRenderers = new List<Renderer>();
            public bool IsCulled;

            public void Cull()
            {
                if (IsCulled) return;
                CulledRenderers.Clear();
                for (int i = 0; i < AllRenderers.Length; i++)
                {
                    Renderer r = AllRenderers[i];
                    if (r != null && r.enabled)
                    {
                        r.enabled = false;
                        CulledRenderers.Add(r);
                    }
                }
                IsCulled = true;
            }

            public void Restore()
            {
                if (!IsCulled) return;
                for (int i = 0; i < CulledRenderers.Count; i++)
                {
                    Renderer r = CulledRenderers[i];
                    if (r != null)
                    {
                        r.enabled = true;
                    }
                }
                CulledRenderers.Clear();
                IsCulled = false;
            }
        }

        private static readonly List<TrackedDetailPiece> _trackedPieces = new List<TrackedDetailPiece>();
        private static readonly Dictionary<int, TrackedDetailPiece> _pieceLookup = new Dictionary<int, TrackedDetailPiece>();

        private static int _culledDetailCount;
        private int _currentIndex;
        private bool _wasOptimizationActive = true;
        private bool _wasBuilding;

        public static int TotalDetailPieces => _trackedPieces.Count;
        public static int CulledDetailPieces => _culledDetailCount;

        private void Awake()
        {
            Instance = this;
            ScanExistingPieces();
        }

        private void OnDestroy()
        {
            RestoreAllRenderers();
            _trackedPieces.Clear();
            _pieceLookup.Clear();
            _culledDetailCount = 0;
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
                            ModConfig.EnableDetailPieceLOD != null &&
                            ModConfig.EnableDetailPieceLOD.Value;

            if (!isActive)
            {
                if (_wasOptimizationActive)
                {
                    _wasOptimizationActive = false;
                    RestoreAllRenderers();
                }
                return;
            }

            _wasOptimizationActive = true;

            bool isBuilding = IsBuildToolEquipped();
            if (isBuilding)
            {
                if (!_wasBuilding || _culledDetailCount > 0)
                {
                    _wasBuilding = true;
                    RestoreAllRenderers();
                }
                return;
            }

            _wasBuilding = false;

            int total = _trackedPieces.Count;
            if (total == 0) return;

            Vector3 refPos = OptimizationHelpers.GetReferencePosition();
            float cullDistance = ModConfig.DetailPieceCullDistance != null
                ? ModConfig.DetailPieceCullDistance.Value
                : 38.0f;

            float restoreDistance = ModConfig.DetailPieceRestoreDistance != null
                ? Mathf.Min(ModConfig.DetailPieceRestoreDistance.Value, cullDistance - 2.0f)
                : Mathf.Max(5f, cullDistance - 8.0f);

            float cullDistSq = cullDistance * cullDistance;
            float restoreDistSq = restoreDistance * restoreDistance;

            int batchSize = Mathf.Min(350, total);
            for (int i = 0; i < batchSize; i++)
            {
                _currentIndex++;
                if (_currentIndex >= _trackedPieces.Count)
                {
                    _currentIndex = 0;
                }

                TrackedDetailPiece item = _trackedPieces[_currentIndex];
                if (item.Piece == null || item.Transform == null)
                {
                    if (item.IsCulled && _culledDetailCount > 0)
                    {
                        _culledDetailCount--;
                    }
                    _pieceLookup.Remove(item.InstanceId);
                    _trackedPieces.RemoveAt(_currentIndex);
                    if (_currentIndex > 0) _currentIndex--;
                    continue;
                }

                if (!item.Piece.gameObject.activeInHierarchy)
                {
                    continue;
                }

                float sqrDist = (item.Transform.position - refPos).sqrMagnitude;

                if (sqrDist > cullDistSq)
                {
                    if (!item.IsCulled)
                    {
                        item.Cull();
                        _culledDetailCount++;
                    }
                }
                else if (sqrDist < restoreDistSq)
                {
                    if (item.IsCulled)
                    {
                        item.Restore();
                        if (_culledDetailCount > 0) _culledDetailCount--;
                    }
                }
            }
        }

        public static void RegisterPiece(Piece piece)
        {
            if (!OptimizationHelpers.IsValidWorldPiece(piece)) return;

            int id = piece.GetInstanceID();
            if (_pieceLookup.ContainsKey(id)) return;

            if (!IsSmallDetailPiece(piece)) return;

            Renderer[] renderers = piece.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            var item = new TrackedDetailPiece
            {
                InstanceId = id,
                Piece = piece,
                Transform = piece.transform,
                AllRenderers = renderers,
                IsCulled = false
            };

            _trackedPieces.Add(item);
            _pieceLookup[id] = item;
        }

        public static void UnregisterPiece(Piece piece)
        {
            if (piece == null) return;

            int id = piece.GetInstanceID();
            if (_pieceLookup.TryGetValue(id, out TrackedDetailPiece item))
            {
                if (item.IsCulled)
                {
                    item.Restore();
                    if (_culledDetailCount > 0) _culledDetailCount--;
                }
                _trackedPieces.Remove(item);
                _pieceLookup.Remove(id);
            }
        }

        public static void RestoreAllRenderers()
        {
            for (int i = 0; i < _trackedPieces.Count; i++)
            {
                TrackedDetailPiece item = _trackedPieces[i];
                if (item.IsCulled)
                {
                    item.Restore();
                }
            }
            _culledDetailCount = 0;
        }

        /// <summary>
        /// Identifies strictly small decorative details, micro-beams, crosses, and carvings.
        /// NEVER includes walls, floors, foundations, pillars, or any roof/ridge tiles.
        /// </summary>
        public static bool IsSmallDetailPiece(Piece piece)
        {
            if (piece == null || piece.gameObject == null) return false;

            string name = piece.gameObject.name;

            // 1. HARD EXCLUSIONS: main silhouettes, roofs, ridge caps, walls, floors, stone/marble
            if (OptimizationHelpers.ContainsIgnoreCase(name, "wall") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "floor") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "roof") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "ridge") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "stair") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "door") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "gate") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "portal") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "chest") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "bed") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "chair") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "table") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "ladder") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "pillar") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "stone_") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "blackmarble_") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "ashwood_"))
            {
                return false;
            }

            // 2. Short 1m micro-beams & 1m poles (never matches 2m wood_pole2 or 2m/4m beams)
            if (OptimizationHelpers.ContainsIgnoreCase(name, "beam_1") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "pole_1") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "wood_pole(") ||
                name.Equals("wood_pole", StringComparison.OrdinalIgnoreCase) ||
                OptimizationHelpers.ContainsIgnoreCase(name, "pole_core_1") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "beam_core_1") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "darkwood_beam_1") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "darkwood_pole_1") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "iron_beam_1") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "iron_pole_1"))
            {
                return true;
            }

            // 3. Cross beams (26° and 45°)
            if (OptimizationHelpers.ContainsIgnoreCase(name, "cross_26") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "cross_45") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "beam_cross") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "darkwood_cross"))
            {
                return true;
            }

            // 4. Aesthetic carvings, dragonheads, brackets, arches, crests, and finials
            if (OptimizationHelpers.ContainsIgnoreCase(name, "dragonhead") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "carving_") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "finial") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "wood_crest") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "wood_trim") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "bracket") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "wood_arch") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "darkwood_arch"))
            {
                return true;
            }

            return false;
        }

        public static bool IsBuildToolEquipped()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return false;

            if (player.InPlaceMode() || player.InRepairMode())
            {
                return true;
            }

            if (player.GetBuildTool() != null)
            {
                return true;
            }

            return false;
        }
    }

    #region Harmony Patches

    [HarmonyPatch(typeof(Piece), "Awake")]
    public static class DetailPieceLOD_Piece_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Piece __instance)
        {
            DetailPieceLOD.RegisterPiece(__instance);
        }
    }

    [HarmonyPatch(typeof(Piece), "OnDestroy")]
    public static class DetailPieceLOD_Piece_OnDestroy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Piece __instance)
        {
            DetailPieceLOD.UnregisterPiece(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    public static class DetailPieceLOD_UpdatePlacementGhost_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Player __instance)
        {
            if (__instance == Player.m_localPlayer && DetailPieceLOD.CulledDetailPieces > 0)
            {
                DetailPieceLOD.RestoreAllRenderers();
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), "EquipItem")]
    public static class DetailPieceLOD_Humanoid_EquipItem_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Humanoid __instance, ItemDrop.ItemData item)
        {
            if (__instance == Player.m_localPlayer && item?.m_shared?.m_buildPieces != null)
            {
                if (DetailPieceLOD.CulledDetailPieces > 0)
                {
                    DetailPieceLOD.RestoreAllRenderers();
                }
            }
        }
    }

    #endregion
}
