using System;
using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// PhysX Decorative Collider Optimization module for OptiHouse.
    /// Eliminates PhysX broadphase bounding-box overload caused by thousands of overlapping decorative beam colliders.
    /// Identifies purely non-walkable decorative pieces (cross beams, decorative ridge caps, trim, and roof poles
    /// elevated > 3.5m above ground/walkable surfaces).
    ///
    /// CRITICAL SAFETY GUARANTEES:
    /// 1. Never touches walkable floors, stairs, doors, chests, or ground foundations.
    /// 2. Hammer/Build Tool Safety: If Player.m_localPlayer equips a hammer or building tool (RightItem has buildData
    ///    or piece list, or InPlaceMode / InRepairMode is active), instantly restores all colliders so hammer snapping
    ///    and piece repairs remain 100% pixel-perfect.
    /// </summary>
    public class DecorativeColliderOptimizer : MonoBehaviour
    {
        private static DecorativeColliderOptimizer? _instance;
        public static DecorativeColliderOptimizer? Instance => _instance;

        private class TrackedDecorativePiece
        {
            public Piece Piece = null!;
            public Transform Transform = null!;
            public Collider[] Colliders = null!;
            public bool CollidersCulled;
        }

        private static readonly List<TrackedDecorativePiece> _trackedPieces = new List<TrackedDecorativePiece>();
        private static readonly Dictionary<int, TrackedDecorativePiece> _pieceLookup = new Dictionary<int, TrackedDecorativePiece>();

        private static int _culledColliderCount;
        private static bool _areCollidersCulled;
        private float _cleanupTimer;

        // Public metrics for HUD & diagnostics
        public static int TotalDecorativePieces => _trackedPieces.Count;
        public static int CulledColliderCount => _culledColliderCount;
        public static bool AreCollidersCulled => _areCollidersCulled;

        private void Awake()
        {
            _instance = this;
        }

        private void Start()
        {
            ScanExistingPieces();
        }

        private void OnDestroy()
        {
            RestoreAllColliders();
            _trackedPieces.Clear();
            _pieceLookup.Clear();
            _culledColliderCount = 0;
            _areCollidersCulled = false;
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            bool isOptActive = OptiHousePlugin.IsOptimizationActive &&
                               ModConfig.EnableStructuralOptimization != null &&
                               ModConfig.EnableStructuralOptimization.Value &&
                               ModConfig.EnableDecorativeColliderOptimization != null &&
                               ModConfig.EnableDecorativeColliderOptimization.Value;

            bool isBuilding = IsBuildToolEquipped();
            bool shouldCull = isOptActive && !isBuilding;

            if (shouldCull)
            {
                if (!_areCollidersCulled)
                {
                    CullAllDecorativeColliders();
                }
            }
            else
            {
                if (_areCollidersCulled)
                {
                    RestoreAllColliders();
                }
            }

            // Periodic cleanup of stale or destroyed pieces
            _cleanupTimer += Time.deltaTime;
            if (_cleanupTimer >= 2.0f)
            {
                _cleanupTimer = 0f;
                CleanupStalePieces();
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

        public static void RegisterPiece(Piece piece)
        {
            if (piece == null || piece.gameObject == null) return;

            int pieceId = piece.GetInstanceID();
            if (_pieceLookup.ContainsKey(pieceId)) return;

            if (!IsPurelyDecorativePiece(piece)) return;

            Collider[] allCols = piece.GetComponentsInChildren<Collider>(true);
            if (allCols == null || allCols.Length == 0) return;

            List<Collider> validCols = new List<Collider>(allCols.Length);
            for (int i = 0; i < allCols.Length; i++)
            {
                Collider c = allCols[i];
                if (c != null && !c.isTrigger)
                {
                    validCols.Add(c);
                }
            }

            if (validCols.Count == 0) return;

            var tracked = new TrackedDecorativePiece
            {
                Piece = piece,
                Transform = piece.transform,
                Colliders = validCols.ToArray(),
                CollidersCulled = false
            };

            // If culling is currently active (optimizations on and player is not holding a build tool),
            // disable colliders immediately for this new decorative piece
            if (_areCollidersCulled)
            {
                for (int i = 0; i < tracked.Colliders.Length; i++)
                {
                    Collider col = tracked.Colliders[i];
                    if (col != null)
                    {
                        col.enabled = false;
                    }
                }
                tracked.CollidersCulled = true;
                _culledColliderCount += tracked.Colliders.Length;
            }

            _trackedPieces.Add(tracked);
            _pieceLookup[pieceId] = tracked;
        }

        public static void UnregisterPiece(Piece piece)
        {
            if (piece == null) return;

            int pieceId = piece.GetInstanceID();
            if (_pieceLookup.TryGetValue(pieceId, out TrackedDecorativePiece tracked))
            {
                if (tracked.CollidersCulled && tracked.Colliders != null)
                {
                    for (int i = 0; i < tracked.Colliders.Length; i++)
                    {
                        Collider col = tracked.Colliders[i];
                        if (col != null)
                        {
                            col.enabled = true;
                        }
                    }
                    _culledColliderCount = Mathf.Max(0, _culledColliderCount - tracked.Colliders.Length);
                }
                _trackedPieces.Remove(tracked);
                _pieceLookup.Remove(pieceId);
            }
        }

        public static void CullAllDecorativeColliders()
        {
            int culled = 0;
            for (int i = 0; i < _trackedPieces.Count; i++)
            {
                TrackedDecorativePiece item = _trackedPieces[i];
                if (item == null || item.Piece == null) continue;

                if (!item.CollidersCulled && item.Colliders != null)
                {
                    for (int c = 0; c < item.Colliders.Length; c++)
                    {
                        Collider col = item.Colliders[c];
                        if (col != null && col.enabled)
                        {
                            col.enabled = false;
                        }
                    }
                    item.CollidersCulled = true;
                }

                if (item.Colliders != null)
                {
                    culled += item.Colliders.Length;
                }
            }
            _culledColliderCount = culled;
            _areCollidersCulled = true;
        }

        public static void RestoreAllColliders()
        {
            for (int i = 0; i < _trackedPieces.Count; i++)
            {
                TrackedDecorativePiece item = _trackedPieces[i];
                if (item == null || item.Piece == null) continue;

                if (item.CollidersCulled && item.Colliders != null)
                {
                    for (int c = 0; c < item.Colliders.Length; c++)
                    {
                        Collider col = item.Colliders[c];
                        if (col != null && !col.enabled)
                        {
                            col.enabled = true;
                        }
                    }
                    item.CollidersCulled = false;
                }
            }
            _culledColliderCount = 0;
            _areCollidersCulled = false;
        }

        public static bool IsBuildToolEquipped()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return false;

            // 1. In placement or repair mode
            if (player.InPlaceMode() || player.InRepairMode())
            {
                return true;
            }

            // 2. Active build tool piece table
            if (player.GetBuildTool() != null)
            {
                return true;
            }

            // 3. Right hand item has build piece list (e.g. Hammer, Hoe, Cultivator)
            ItemDrop.ItemData? rightItem = player.RightItem;
            if (rightItem != null && rightItem.m_shared != null && rightItem.m_shared.m_buildPieces != null)
            {
                return true;
            }

            // 4. Left hand item (in case of dual-wielding or modded tools)
            ItemDrop.ItemData? leftItem = player.LeftItem;
            if (leftItem != null && leftItem.m_shared != null && leftItem.m_shared.m_buildPieces != null)
            {
                return true;
            }

            return false;
        }

        public static bool IsNeverCulled(Piece piece)
        {
            if (piece == null || piece.gameObject == null) return true;

            // Critical functional components
            if (piece.GetComponent<Door>() != null ||
                piece.GetComponent<Container>() != null ||
                piece.GetComponent<Bed>() != null ||
                piece.GetComponent<Chair>() != null ||
                piece.GetComponent<CraftingStation>() != null ||
                piece.GetComponent<StationExtension>() != null ||
                piece.GetComponent<TeleportWorld>() != null ||
                piece.GetComponent<Sign>() != null ||
                piece.GetComponent<ItemStand>() != null ||
                piece.GetComponent<ArmorStand>() != null ||
                piece.GetComponent<Fireplace>() != null ||
                piece.GetComponent<CookingStation>() != null ||
                piece.GetComponent<Smelter>() != null ||
                piece.GetComponent<Fermenter>() != null ||
                piece.GetComponent<Ship>() != null)
            {
                return true;
            }

            // Valheim piece ground constraints
            if (piece.m_groundPiece || piece.m_groundOnly)
            {
                return true;
            }

            string name = piece.gameObject.name.ToLowerInvariant();

            // Walkable surfaces and structural foundations:
            if (name.Contains("floor") ||
                name.Contains("stair") ||
                name.Contains("step") ||
                name.Contains("ladder") ||
                name.Contains("door") ||
                name.Contains("gate") ||
                name.Contains("chest") ||
                name.Contains("cart") ||
                name.Contains("bed") ||
                name.Contains("chair") ||
                name.Contains("stool") ||
                name.Contains("bench") ||
                name.Contains("table") ||
                name.Contains("portal") ||
                name.Contains("ship") ||
                name.Contains("raft") ||
                name.Contains("foundation") ||
                name.Contains("hearth") ||
                name.Contains("iron_grate") ||
                name.Contains("stone_floor") ||
                name.Contains("stone_wall") ||
                name.Contains("stone_pillar") ||
                name.Contains("stone_arch") ||
                name.Contains("blackmarble_floor") ||
                name.Contains("blackmarble_stair") ||
                name.Contains("blackmarble_pillar") ||
                name.Contains("pillar") ||
                name.Contains("wall"))
            {
                return true;
            }

            // Normal sloped roof panels must retain colliders for player traversal
            if (name.Contains("roof") &&
                !name.Contains("roof_ridge") &&
                !name.Contains("roof_top") &&
                !name.Contains("roof_pole") &&
                !name.Contains("roof_crest") &&
                !name.Contains("roof_finial"))
            {
                return true;
            }

            return false;
        }

        public static bool IsPurelyDecorativePiece(Piece piece)
        {
            if (piece == null || piece.gameObject == null) return false;

            // Safety rule: Never cull walkable or functional pieces
            if (IsNeverCulled(piece))
            {
                return false;
            }

            string name = piece.gameObject.name.ToLowerInvariant();

            // 1. Cross beams (wood & darkwood cross beams 26° and 45°)
            if (name.Contains("cross_26") ||
                name.Contains("cross_45") ||
                name.Contains("beam_cross") ||
                name.Contains("wood_beam_cross") ||
                name.Contains("darkwood_beam_cross"))
            {
                return true;
            }

            // 2. Decorative roof ridge caps, top caps, and crests
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

            // 3. Decorative carvings, dragonheads, trim, and aesthetic brackets
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

            // 4. Roof poles and high-elevation decorative beams elevated > 3.5m above ground/walkable surfaces
            if (name.Contains("roof_pole") ||
                name.Contains("roof_beam") ||
                name.Contains("wood_pole") ||
                name.Contains("darkwood_pole") ||
                name.Contains("wood_beam") ||
                name.Contains("darkwood_beam") ||
                name.Contains("iron_beam") ||
                name.Contains("iron_pole"))
            {
                if (IsElevatedAboveWalkable(piece, 3.5f))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsElevatedAboveWalkable(Piece piece, float minElevation = 3.5f)
        {
            if (piece == null) return false;
            Vector3 pos = piece.transform.position;

            // 1. Check elevation above terrain height
            float groundY = pos.y;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(pos, out float gh))
            {
                groundY = gh;
            }
            else if (Heightmap.GetHeight(pos, out float hmh))
            {
                groundY = hmh;
            }

            if (pos.y - groundY < minElevation)
            {
                return false;
            }

            // 2. Raycast downward to ensure no walkable floor, platform, or structure exists within minElevation
            int layerMask = LayerMask.GetMask("piece", "Default", "terrain");
            if (layerMask == 0)
            {
                layerMask = LayerMask.GetMask("piece");
            }

            Vector3 rayOrigin = pos + Vector3.down * 0.2f;
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, minElevation, layerMask, QueryTriggerInteraction.Ignore);
            if (hits != null && hits.Length > 0)
            {
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider col = hits[i].collider;
                    if (col != null)
                    {
                        Piece hitPiece = col.GetComponentInParent<Piece>();
                        if (hitPiece != null && hitPiece == piece)
                        {
                            continue; // Ignore hits on self
                        }
                        // Found a floor or surface underneath within 3.5m
                        return false;
                    }
                }
            }

            return true;
        }

        private static void CleanupStalePieces()
        {
            for (int i = _trackedPieces.Count - 1; i >= 0; i--)
            {
                TrackedDecorativePiece item = _trackedPieces[i];
                if (item == null || item.Piece == null || item.Transform == null)
                {
                    if (item != null && item.CollidersCulled && item.Colliders != null)
                    {
                        _culledColliderCount = Mathf.Max(0, _culledColliderCount - item.Colliders.Length);
                    }
                    _trackedPieces.RemoveAt(i);
                }
            }
        }
    }

    #region Harmony Patches

    /// <summary>
    /// Attach DecorativeColliderOptimizer to the mod plugin GameObject on startup.
    /// </summary>
    [HarmonyPatch(typeof(OptiHousePlugin), "Awake")]
    public static class DecorativeColliderOptimizer_Plugin_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(OptiHousePlugin __instance)
        {
            if (__instance != null && __instance.gameObject.GetComponent<DecorativeColliderOptimizer>() == null)
            {
                __instance.gameObject.AddComponent<DecorativeColliderOptimizer>();
            }
        }
    }

    /// <summary>
    /// Intercepts Piece.Awake to register purely decorative pieces.
    /// </summary>
    [HarmonyPatch(typeof(Piece), "Awake")]
    public static class DecorativeColliderOptimizer_Piece_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Piece __instance)
        {
            DecorativeColliderOptimizer.RegisterPiece(__instance);
        }
    }

    /// <summary>
    /// Intercepts Piece.OnDestroy to cleanly unregister deleted pieces and restore collider counts.
    /// </summary>
    [HarmonyPatch(typeof(Piece), "OnDestroy")]
    public static class DecorativeColliderOptimizer_Piece_OnDestroy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Piece __instance)
        {
            DecorativeColliderOptimizer.UnregisterPiece(__instance);
        }
    }

    /// <summary>
    /// Safety guarantee: Instantly restore colliders before piece raycast tests so hammer snapping and hover text work pixel-perfect.
    /// </summary>
    [HarmonyPatch(typeof(Player), "PieceRayTest")]
    public static class DecorativeColliderOptimizer_PieceRayTest_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Player __instance)
        {
            if (__instance == Player.m_localPlayer && DecorativeColliderOptimizer.AreCollidersCulled)
            {
                DecorativeColliderOptimizer.RestoreAllColliders();
            }
        }
    }

    /// <summary>
    /// Safety guarantee: Instantly restore colliders before updating placement ghost snapping.
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    public static class DecorativeColliderOptimizer_UpdatePlacementGhost_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Player __instance)
        {
            if (__instance == Player.m_localPlayer && DecorativeColliderOptimizer.AreCollidersCulled)
            {
                DecorativeColliderOptimizer.RestoreAllColliders();
            }
        }
    }

    /// <summary>
    /// Safety guarantee: Instantly restore colliders when a build tool is equipped.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), "EquipItem")]
    public static class DecorativeColliderOptimizer_Humanoid_EquipItem_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Humanoid __instance, ItemDrop.ItemData item)
        {
            if (__instance == Player.m_localPlayer && item?.m_shared?.m_buildPieces != null)
            {
                if (DecorativeColliderOptimizer.AreCollidersCulled)
                {
                    DecorativeColliderOptimizer.RestoreAllColliders();
                }
            }
        }
    }

    #endregion
}
