using System;
using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Interior Occlusion Optimizer for OptiHouse.
    /// Solves the lack of occlusion culling in Valheim player bases.
    /// 
    /// High-density player bases contain hundreds or thousands of interior items (furniture, beds, chests,
    /// rugs, decorations, item stands, cups, lighting) hidden behind exterior walls and roofs.
    /// Because Valheim lacks dynamic occlusion culling, all internal items submit geometry and batch draw calls
    /// even when the player is outside looking at a solid stone/wood wall.
    /// 
    /// When the player is outside (!Player.m_localPlayer.InShelter()) and distance > InteriorCullDistance
    /// (default 18m with 3m hysteresis), this module temporarily disables MeshRenderers/SkinnedMeshRenderers
    /// on enclosed interior pieces.
    /// 
    /// Exterior walls, roofs, windows, doors, and facades remain 100% rendered so bases look solid and intact.
    /// Renderers are instantly restored when the player approaches an entrance or enters shelter (InShelter() == true),
    /// or when optimizations are toggled off.
    /// 
    /// Exposes public metrics for HUD: TotalInteriorPieces, CulledInteriorPieces.
    /// </summary>
    public class InteriorOcclusionOptimizer : MonoBehaviour
    {
        private static InteriorOcclusionOptimizer? _instance;
        public static InteriorOcclusionOptimizer? Instance => _instance;

        private class TrackedInteriorPiece
        {
            public int InstanceId;
            public Piece Piece = null!;
            public Transform Transform = null!;
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public bool IsCulled;
            public bool IsEnclosed;

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

        private static readonly List<TrackedInteriorPiece> _trackedPieces = new List<TrackedInteriorPiece>();
        private static readonly Dictionary<int, TrackedInteriorPiece> _pieceLookup = new Dictionary<int, TrackedInteriorPiece>();

        private static int _culledPieceCount;
        private static int _totalEnclosedCount;
        private int _currentIndex;
        private bool _wasOptimizationActive = true;
        private bool _wasPlayerInShelter;

        public static int TotalInteriorPieces => _totalEnclosedCount;
        public static int CulledInteriorPieces => _culledPieceCount;
        public static int TrackedPiecesCount => _trackedPieces.Count;
        public static bool IsCullingActive => _culledPieceCount > 0;

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
            _culledPieceCount = 0;
            _totalEnclosedCount = 0;
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// Scans all active pieces currently loaded in the scene.
        /// </summary>
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
                            ModConfig.EnableInteriorCulling != null &&
                            ModConfig.EnableInteriorCulling.Value;

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

            Player player = Player.m_localPlayer;
            bool isPlayerInShelter = player != null && player.InShelter();

            // When player enters shelter, instantly restore all culled interior pieces
            if (isPlayerInShelter)
            {
                if (!_wasPlayerInShelter || _culledPieceCount > 0)
                {
                    _wasPlayerInShelter = true;
                    RestoreAllRenderers();
                }
                return;
            }

            _wasPlayerInShelter = false;

            int total = _trackedPieces.Count;
            if (total == 0) return;

            Vector3 refPos = GetReferencePosition();
            float cullDistance = ModConfig.InteriorCullDistance != null
                ? ModConfig.InteriorCullDistance.Value
                : 18.0f;

            float cullDistSq = cullDistance * cullDistance;
            // 3.0m hysteresis margin prevents rapid render toggling at boundary
            float restoreDistance = Mathf.Max(0f, cullDistance - 3.0f);
            float restoreDistSq = restoreDistance * restoreDistance;

            // Process up to 300 pieces per frame in a round-robin schedule for zero-alloc smooth performance
            int batchSize = Mathf.Min(300, total);
            for (int i = 0; i < batchSize; i++)
            {
                _currentIndex++;
                if (_currentIndex >= _trackedPieces.Count)
                {
                    _currentIndex = 0;
                }

                TrackedInteriorPiece item = _trackedPieces[_currentIndex];
                if (item.Piece == null || item.Transform == null)
                {
                    if (item.IsCulled && _culledPieceCount > 0)
                    {
                        _culledPieceCount--;
                    }
                    if (item.IsEnclosed && _totalEnclosedCount > 0)
                    {
                        _totalEnclosedCount--;
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

                // If not yet enclosed, re-check lazily in case a roof/ceiling was completed above it
                if (!item.IsEnclosed)
                {
                    if (IsEnclosedPiece(item.Piece))
                    {
                        item.IsEnclosed = true;
                        _totalEnclosedCount++;
                    }
                    else
                    {
                        // Open-air piece: keep visible to the player outside
                        if (item.IsCulled)
                        {
                            item.SetRenderersEnabled(true);
                            item.IsCulled = false;
                            if (_culledPieceCount > 0) _culledPieceCount--;
                        }
                        continue;
                    }
                }

                float sqrDist = (item.Transform.position - refPos).sqrMagnitude;

                if (sqrDist > cullDistSq)
                {
                    if (!item.IsCulled)
                    {
                        item.SetRenderersEnabled(false);
                        item.IsCulled = true;
                        _culledPieceCount++;
                    }
                }
                else if (sqrDist < restoreDistSq)
                {
                    if (item.IsCulled)
                    {
                        item.SetRenderersEnabled(true);
                        item.IsCulled = false;
                        if (_culledPieceCount > 0) _culledPieceCount--;
                    }
                }
            }
        }

        /// <summary>
        /// Registers a building piece if it qualifies as an interior piece.
        /// </summary>
        public static void RegisterPiece(Piece piece)
        {
            if (piece == null || piece.gameObject == null) return;

            int id = piece.GetInstanceID();
            if (_pieceLookup.ContainsKey(id)) return;

            if (!IsInteriorPiece(piece)) return;

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

            bool enclosed = IsEnclosedPiece(piece);

            var item = new TrackedInteriorPiece
            {
                InstanceId = id,
                Piece = piece,
                Transform = piece.transform,
                IsCulled = false,
                IsEnclosed = enclosed
            };
            item.Renderers.AddRange(validRenderers);

            if (enclosed)
            {
                _totalEnclosedCount++;

                // If optimizations are currently active, player is outside, and distant, cull immediately
                bool isOptActive = OptiHousePlugin.IsOptimizationActive &&
                                   ModConfig.EnableInteriorCulling != null &&
                                   ModConfig.EnableInteriorCulling.Value;

                Player player = Player.m_localPlayer;
                bool isOutside = player == null || !player.InShelter();

                if (isOptActive && isOutside)
                {
                    Vector3 refPos = GetReferencePosition();
                    float cullDist = ModConfig.InteriorCullDistance != null
                        ? ModConfig.InteriorCullDistance.Value
                        : 18.0f;

                    if ((item.Transform.position - refPos).sqrMagnitude > cullDist * cullDist)
                    {
                        item.SetRenderersEnabled(false);
                        item.IsCulled = true;
                        _culledPieceCount++;
                    }
                }
            }

            _trackedPieces.Add(item);
            _pieceLookup[id] = item;
        }

        /// <summary>
        /// Unregisters a piece when demolished or unloaded.
        /// </summary>
        public static void UnregisterPiece(Piece piece)
        {
            if (piece == null) return;

            int id = piece.GetInstanceID();
            if (_pieceLookup.TryGetValue(id, out TrackedInteriorPiece item))
            {
                if (item.IsCulled)
                {
                    item.SetRenderersEnabled(true);
                    item.IsCulled = false;
                    if (_culledPieceCount > 0) _culledPieceCount--;
                }
                if (item.IsEnclosed && _totalEnclosedCount > 0)
                {
                    _totalEnclosedCount--;
                }
                _trackedPieces.Remove(item);
                _pieceLookup.Remove(id);
            }
        }

        /// <summary>
        /// Refreshes renderers for pieces whose child geometry changed (e.g. mounted items on ItemStand/ArmorStand).
        /// </summary>
        public static void RefreshPiece(Piece piece)
        {
            if (piece == null) return;

            int id = piece.GetInstanceID();
            if (_pieceLookup.TryGetValue(id, out TrackedInteriorPiece item))
            {
                Renderer[] renderers = piece.GetComponentsInChildren<Renderer>(true);
                item.Renderers.Clear();
                if (renderers != null)
                {
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        Renderer r = renderers[i];
                        if (r != null)
                        {
                            item.Renderers.Add(r);
                            if (item.IsCulled)
                            {
                                r.enabled = false;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Restores all culled renderers immediately.
        /// </summary>
        public static void RestoreAllRenderers()
        {
            for (int i = 0; i < _trackedPieces.Count; i++)
            {
                TrackedInteriorPiece item = _trackedPieces[i];
                if (item.IsCulled)
                {
                    item.SetRenderersEnabled(true);
                    item.IsCulled = false;
                }
            }
            _culledPieceCount = 0;
        }

        /// <summary>
        /// Determines whether a piece is an interior item (furniture, chests, beds, decor, stands).
        /// Explicitly excludes exterior walls, roofs, windows, facades, and foundations.
        /// </summary>
        public static bool IsInteriorPiece(Piece piece)
        {
            if (piece == null || piece.gameObject == null) return false;

            string name = piece.gameObject.name.ToLowerInvariant();

            // 1. HARD EXCLUSIONS: Exterior structural elements, foundations, walls, roofs, floors, doors, gates, portals
            // These must NEVER be culled, so the base exterior remains 100% solid, intact, and opaque from outside.
            if (name.Contains("wall") ||
                name.Contains("roof") ||
                name.Contains("floor") ||
                name.Contains("gate") ||
                name.Contains("door") ||
                name.Contains("window") ||
                name.Contains("shutter") ||
                name.Contains("beam") ||
                name.Contains("pole") ||
                name.Contains("pillar") ||
                name.Contains("post") ||
                name.Contains("arch") ||
                name.Contains("stair") ||
                name.Contains("ladder") ||
                name.Contains("steps") ||
                name.Contains("support") ||
                name.Contains("foundation") ||
                name.Contains("portal") ||
                name.Contains("chimney") ||
                name.Contains("stone_") ||
                name.Contains("blackmarble_") ||
                name.Contains("ashwood_wall") ||
                name.Contains("ashwood_roof") ||
                name.Contains("ashwood_floor") ||
                name.Contains("turret") ||
                name.Contains("ballista") ||
                name.Contains("stake") ||
                name.Contains("sharpstakes") ||
                name.Contains("cart") ||
                name.Contains("ship") ||
                name.Contains("karve") ||
                name.Contains("vikingboat") ||
                name.Contains("cultivate") ||
                name.Contains("sapling") ||
                name.Contains("plant") ||
                name.Contains("beehive") ||
                name.Contains("windmill") ||
                name.Contains("blastfurnace") ||
                name.Contains("smelter") ||
                name.Contains("kiln"))
            {
                return false;
            }

            // 2. Component-based Interior Checks:
            // Bed, Chair, Container (chests), ItemStand, ArmorStand, Sign
            if (piece.GetComponent<Bed>() != null ||
                piece.GetComponent<Chair>() != null ||
                piece.GetComponent<Container>() != null ||
                piece.GetComponent<ItemStand>() != null ||
                piece.GetComponent<ArmorStand>() != null ||
                piece.GetComponent<Sign>() != null)
            {
                return true;
            }

            // 3. PieceCategory.Furniture
            if (piece.m_category == Piece.PieceCategory.Furniture)
            {
                return true;
            }

            // 4. Comfort items (rugs, tables, tapestries, braziers, thrones)
            if (piece.m_comfort > 0)
            {
                return true;
            }

            // 5. Name-based Interior/Decor Checks
            if (name.Contains("chest") ||
                name.Contains("bed") ||
                name.Contains("chair") ||
                name.Contains("table") ||
                name.Contains("bench") ||
                name.Contains("stool") ||
                name.Contains("throne") ||
                name.Contains("couch") ||
                name.Contains("rug") ||
                name.Contains("carpet") ||
                name.Contains("tapestry") ||
                name.Contains("banner") ||
                name.Contains("curtain") ||
                name.Contains("sconce") ||
                name.Contains("chandelier") ||
                name.Contains("brazier") ||
                name.Contains("lantern") ||
                name.Contains("shelf") ||
                name.Contains("cupboard") ||
                name.Contains("bookcase") ||
                name.Contains("cabinet") ||
                name.Contains("drawer") ||
                name.Contains("barrel") ||
                name.Contains("cask") ||
                name.Contains("bath") ||
                name.Contains("tub") ||
                name.Contains("hottub") ||
                name.Contains("armorstand") ||
                name.Contains("itemstand") ||
                name.Contains("sign"))
            {
                return true;
            }

            return false;
        }

        private static readonly AccessTools.FieldRef<WearNTear, bool>? _haveRoofRef =
            AccessTools.FieldRefAccess<WearNTear, bool>("m_haveRoof");
        private static readonly AccessTools.FieldRef<WearNTear, bool>? _haveAshRoofRef =
            AccessTools.FieldRefAccess<WearNTear, bool>("m_haveAshRoof");

        /// <summary>
        /// Determines whether a piece is located inside an enclosed room (under a roof or upper-floor ceiling).
        /// </summary>
        public static bool IsEnclosedPiece(Piece piece)
        {
            if (piece == null) return false;

            // Check 1: WearNTear roof status
            WearNTear wnt = piece.GetComponent<WearNTear>();
            if (wnt != null)
            {
                try
                {
                    if (_haveRoofRef != null && _haveRoofRef(wnt)) return true;
                    if (_haveAshRoofRef != null && _haveAshRoofRef(wnt)) return true;
                }
                catch
                {
                    // Fall back to physics raycast
                }
            }

            // Check 2: Fast upward raycast for roof/ceiling/floor structure above the piece
            Vector3 origin = piece.transform.position + Vector3.up * 0.15f;
            int mask = LayerMask.GetMask("piece", "Default", "static_solid");
            if (mask == 0) mask = ~0;

            if (Physics.Raycast(origin, Vector3.up, out RaycastHit hit, 25f, mask, QueryTriggerInteraction.Ignore))
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
    public static class InteriorOcclusionOptimizer_Plugin_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(OptiHousePlugin __instance)
        {
            if (__instance != null && __instance.gameObject.GetComponent<InteriorOcclusionOptimizer>() == null)
            {
                __instance.gameObject.AddComponent<InteriorOcclusionOptimizer>();
            }
        }
    }

    [HarmonyPatch(typeof(Piece), "Awake")]
    public static class InteriorOcclusionOptimizer_Piece_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Piece __instance)
        {
            InteriorOcclusionOptimizer.RegisterPiece(__instance);
        }
    }

    [HarmonyPatch(typeof(Piece), "OnDestroy")]
    public static class InteriorOcclusionOptimizer_Piece_OnDestroy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Piece __instance)
        {
            InteriorOcclusionOptimizer.UnregisterPiece(__instance);
        }
    }

    [HarmonyPatch(typeof(ItemStand), "UpdateVisual")]
    public static class InteriorOcclusionOptimizer_ItemStand_Visual_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemStand __instance)
        {
            if (__instance != null)
            {
                Piece p = __instance.GetComponent<Piece>();
                if (p != null)
                {
                    InteriorOcclusionOptimizer.RefreshPiece(p);
                }
            }
        }
    }

    [HarmonyPatch(typeof(ArmorStand), "UpdateVisual")]
    public static class InteriorOcclusionOptimizer_ArmorStand_Visual_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ArmorStand __instance)
        {
            if (__instance != null)
            {
                Piece p = __instance.GetComponent<Piece>();
                if (p != null)
                {
                    InteriorOcclusionOptimizer.RefreshPiece(p);
                }
            }
        }
    }

    #endregion
}
