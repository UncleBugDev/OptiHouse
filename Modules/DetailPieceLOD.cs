using System;
using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Structural Detail Piece LOD System for OptiHouse.
    /// Eliminates massive draw call and vertex bottlenecks in mega-bases by dynamically culling
    /// the renderers of small decorative micro-pieces (1m beams, 26°/45° cross beams, decorative brackets,
    /// trim, dragonheads, roof crests) when beyond the visual detail threshold (default: 32m).
    ///
    /// CRITICAL VISUAL & SAFETY GUARANTEES:
    /// 1. Main structural walls, 2m/4m poles, stone/marble walls, floors, stairs, and full roofs
    ///    are NEVER culled, preserving the magnificent full silhouette of castles and bases from any distance.
    /// 2. Hammer/Build Tool Safety: If the player equips a hammer or enters build/repair mode,
    ///    instantly restores all detail renderers for pixel-perfect snapping and building.
    /// 3. Smooth hysteresis margin (default 6m) prevents rapid render toggling at boundary.
    /// </summary>
    public class DetailPieceLOD : MonoBehaviour
    {
        private static DetailPieceLOD? _instance;
        public static DetailPieceLOD? Instance => _instance;

        private class TrackedDetailPiece
        {
            public int InstanceId;
            public Piece Piece = null!;
            public Transform Transform = null!;
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public bool IsCulled;

            public void SetRenderersEnabled(bool enabled)
            {
                for (int i = 0; i < Renderers.Count; i++)
                {
                    Renderer r = Renderers[i];
                    if (r != null)
                    {
                        r.enabled = enabled;
                    }
                }
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
            _instance = this;
            ScanExistingPieces();
        }

        private void OnDestroy()
        {
            RestoreAllRenderers();
            _trackedPieces.Clear();
            _pieceLookup.Clear();
            _culledDetailCount = 0;
            if (_instance == this)
            {
                _instance = null;
            }
        }

        public static void ScanExistingPieces()
        {
            Piece[] pieces = FindObjectsByType<Piece>(FindObjectsSortMode.None);
            if (pieces != null)
            {
                for (int i = 0; i < pieces.Length; i++)
                {
                    Piece p = pieces[i];
                    if (p != null)
                    {
                        RegisterPiece(p);
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

            Vector3 refPos = GetReferencePosition();
            float cullDistance = ModConfig.DetailPieceCullDistance != null
                ? ModConfig.DetailPieceCullDistance.Value
                : 32.0f;

            float restoreDistance = ModConfig.DetailPieceRestoreDistance != null
                ? ModConfig.DetailPieceRestoreDistance.Value
                : Mathf.Max(5f, cullDistance - 6.0f);

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
                        item.SetRenderersEnabled(false);
                        item.IsCulled = true;
                        _culledDetailCount++;
                    }
                }
                else if (sqrDist < restoreDistSq)
                {
                    if (item.IsCulled)
                    {
                        item.SetRenderersEnabled(true);
                        item.IsCulled = false;
                        if (_culledDetailCount > 0) _culledDetailCount--;
                    }
                }
            }
        }

        public static void RegisterPiece(Piece piece)
        {
            if (piece == null || piece.gameObject == null) return;

            int id = piece.GetInstanceID();
            if (_pieceLookup.ContainsKey(id)) return;

            if (!IsSmallDetailPiece(piece)) return;

            Renderer[] renderers = piece.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            List<Renderer> validRenderers = new List<Renderer>(renderers.Length);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r != null)
                {
                    validRenderers.Add(r);
                }
            }

            if (validRenderers.Count == 0) return;

            var item = new TrackedDetailPiece
            {
                InstanceId = id,
                Piece = piece,
                Transform = piece.transform,
                IsCulled = false
            };
            item.Renderers.AddRange(validRenderers);

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
                    item.SetRenderersEnabled(true);
                    item.IsCulled = false;
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
                    item.SetRenderersEnabled(true);
                    item.IsCulled = false;
                }
            }
            _culledDetailCount = 0;
        }

        /// <summary>
        /// Identifies purely small decorative details, micro-beams, and aesthetic trim.
        /// NEVER includes structural walls, full roof slopes, floors, doors, stairs, or foundations.
        /// </summary>
        public static bool IsSmallDetailPiece(Piece piece)
        {
            if (piece == null || piece.gameObject == null) return false;

            string name = piece.gameObject.name.ToLowerInvariant();

            // 1. HARD SAFETY EXCLUSIONS:
            // Walls, full roofs, floors, stairs, doors, chests, beds, tables, portals, ships
            if (name.Contains("wall") ||
                name.Contains("floor") ||
                name.Contains("stair") ||
                name.Contains("door") ||
                name.Contains("gate") ||
                name.Contains("portal") ||
                name.Contains("chest") ||
                name.Contains("bed") ||
                name.Contains("chair") ||
                name.Contains("table") ||
                name.Contains("ladder") ||
                name.Contains("stone_") ||
                name.Contains("blackmarble_") ||
                name.Contains("ashwood_wall") ||
                name.Contains("ashwood_floor"))
            {
                return false;
            }

            // Exclude normal sloped roof slabs (they form the main building silhouette)
            if (name.Contains("roof") &&
                !name.Contains("roof_ridge") &&
                !name.Contains("roof_top") &&
                !name.Contains("roof_pole") &&
                !name.Contains("roof_crest") &&
                !name.Contains("roof_finial") &&
                !name.Contains("ridge_") &&
                !name.Contains("straw_roof_top") &&
                !name.Contains("straw_roof_ridge"))
            {
                return false;
            }

            // 2. Short 1m beams & poles (wood, darkwood, corewood, iron)
            if (name.Contains("beam_1") ||
                name.Contains("pole_1") ||
                name.Contains("beam_26_1") ||
                name.Contains("beam_45_1") ||
                name.Contains("pole_core_1") ||
                name.Contains("beam_core_1") ||
                name.Contains("darkwood_beam_1") ||
                name.Contains("darkwood_pole_1") ||
                name.Contains("iron_beam_1") ||
                name.Contains("iron_pole_1"))
            {
                return true;
            }

            // 3. Cross beams (26° and 45°)
            if (name.Contains("cross_26") ||
                name.Contains("cross_45") ||
                name.Contains("beam_cross"))
            {
                return true;
            }

            // 4. Decorative roof ridges, crests, and finials
            if (name.Contains("roof_ridge") ||
                name.Contains("roof_top") ||
                name.Contains("shingle_ridge") ||
                name.Contains("straw_roof_top") ||
                name.Contains("straw_roof_ridge") ||
                name.Contains("tarp_roof_ridge") ||
                name.Contains("ridge_26") ||
                name.Contains("ridge_45") ||
                name.Contains("ridge_cap") ||
                name.Contains("roof_crest") ||
                name.Contains("roof_finial"))
            {
                return true;
            }

            // 5. Aesthetic carvings, dragonheads, brackets, trim, and small arches
            if (name.Contains("dragonhead") ||
                name.Contains("carving_raven") ||
                name.Contains("carving_wolf") ||
                name.Contains("finial") ||
                name.Contains("wood_trim") ||
                name.Contains("bracket") ||
                name.Contains("wood_arch") ||
                name.Contains("darkwood_arch"))
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

            ItemDrop.ItemData? rightItem = player.RightItem;
            if (rightItem != null && rightItem.m_shared != null && rightItem.m_shared.m_buildPieces != null)
            {
                return true;
            }

            ItemDrop.ItemData? leftItem = player.LeftItem;
            if (leftItem != null && leftItem.m_shared != null && leftItem.m_shared.m_buildPieces != null)
            {
                return true;
            }

            return false;
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
    public static class DetailPieceLOD_Plugin_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(OptiHousePlugin __instance)
        {
            if (__instance != null && __instance.gameObject.GetComponent<DetailPieceLOD>() == null)
            {
                __instance.gameObject.AddComponent<DetailPieceLOD>();
            }
        }
    }

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

    [HarmonyPatch(typeof(Player), "PieceRayTest")]
    public static class DetailPieceLOD_PieceRayTest_Patch
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
