using System;
using System.Collections.Generic;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Interior Occlusion Optimizer for OptiHouse.
    /// Culls enclosed interior furniture, chests, beds, tables, rugs, and decor beyond InteriorCullDistance (default 26m).
    /// - Never hides walls, roofs, floors, portals, hearths, fireplaces, or light fixtures.
    /// - Snapshots only currently enabled renderers when culling to preserve WearNTear/ArmorStand visual states.
    /// - Rate-limits enclosure checks on open-air furniture to avoid continuous Physics.Raycast overhead.
    /// </summary>
    public class InteriorOcclusionOptimizer : MonoBehaviour
    {
        public static InteriorOcclusionOptimizer? Instance { get; private set; }

        private class TrackedInteriorPiece
        {
            public int InstanceId;
            public Piece Piece = null!;
            public Transform Transform = null!;
            public Renderer[] AllRenderers = Array.Empty<Renderer>();
            public readonly List<Renderer> CulledRenderers = new List<Renderer>();
            public bool IsCulled;
            public bool IsEnclosed;
            public float NextEnclosureCheckTime;

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
            Instance = this;
            ScanExistingPieces();
        }

        private void OnDestroy()
        {
            RestoreAllRenderers();
            _trackedPieces.Clear();
            _pieceLookup.Clear();
            _culledPieceCount = 0;
            _totalEnclosedCount = 0;
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
            bool smartCulling = ModConfig.EnableSmartInteriorCulling != null && ModConfig.EnableSmartInteriorCulling.Value;

            if (isPlayerInShelter && !smartCulling)
            {
                if (!_wasPlayerInShelter || _culledPieceCount > 0)
                {
                    _wasPlayerInShelter = true;
                    RestoreAllRenderers();
                }
                return;
            }

            _wasPlayerInShelter = isPlayerInShelter;

            int total = _trackedPieces.Count;
            if (total == 0) return;

            Vector3 refPos = OptimizationHelpers.GetReferencePosition();
            float cullDistance = ModConfig.InteriorCullDistance != null
                ? ModConfig.InteriorCullDistance.Value
                : 26.0f;

            float cullDistSq = cullDistance * cullDistance;
            float restoreDistance = Mathf.Max(0f, cullDistance - 3.5f);
            float restoreDistSq = restoreDistance * restoreDistance;
            float now = Time.time;

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
                    if (item.IsCulled && _culledPieceCount > 0) _culledPieceCount--;
                    if (item.IsEnclosed && _totalEnclosedCount > 0) _totalEnclosedCount--;
                    _pieceLookup.Remove(item.InstanceId);
                    _trackedPieces.RemoveAt(_currentIndex);
                    if (_currentIndex > 0) _currentIndex--;
                    continue;
                }

                if (!item.Piece.gameObject.activeInHierarchy)
                {
                    continue;
                }

                // Lazily re-check enclosure on outdoor furniture at most once every 15 seconds
                if (!item.IsEnclosed)
                {
                    if (now < item.NextEnclosureCheckTime)
                    {
                        continue;
                    }

                    if (IsEnclosedPiece(item.Piece))
                    {
                        item.IsEnclosed = true;
                        _totalEnclosedCount++;
                    }
                    else
                    {
                        item.NextEnclosureCheckTime = now + 15.0f;
                        if (item.IsCulled)
                        {
                            item.Restore();
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
                        item.Cull();
                        _culledPieceCount++;
                    }
                }
                else if (sqrDist < restoreDistSq)
                {
                    if (item.IsCulled)
                    {
                        item.Restore();
                        if (_culledPieceCount > 0) _culledPieceCount--;
                    }
                }
            }
        }

        public static void RegisterPiece(Piece piece)
        {
            if (!OptimizationHelpers.IsValidWorldPiece(piece)) return;

            int id = piece.GetInstanceID();
            if (_pieceLookup.ContainsKey(id)) return;

            if (!IsInteriorPiece(piece)) return;

            Renderer[] renderers = piece.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            bool enclosed = IsEnclosedPiece(piece);

            var item = new TrackedInteriorPiece
            {
                InstanceId = id,
                Piece = piece,
                Transform = piece.transform,
                AllRenderers = renderers,
                IsCulled = false,
                IsEnclosed = enclosed,
                NextEnclosureCheckTime = Time.time + 15.0f
            };

            if (enclosed)
            {
                _totalEnclosedCount++;

                bool isOptActive = OptiHousePlugin.IsOptimizationActive &&
                                   ModConfig.EnableInteriorCulling != null &&
                                   ModConfig.EnableInteriorCulling.Value;

                Player player = Player.m_localPlayer;
                bool isOutside = player == null || !player.InShelter();
                bool smart = ModConfig.EnableSmartInteriorCulling != null && ModConfig.EnableSmartInteriorCulling.Value;

                if (isOptActive && (isOutside || smart))
                {
                    Vector3 refPos = OptimizationHelpers.GetReferencePosition();
                    float cullDist = ModConfig.InteriorCullDistance != null
                        ? ModConfig.InteriorCullDistance.Value
                        : 26.0f;

                    if ((item.Transform.position - refPos).sqrMagnitude > cullDist * cullDist)
                    {
                        item.Cull();
                        _culledPieceCount++;
                    }
                }
            }

            _trackedPieces.Add(item);
            _pieceLookup[id] = item;
        }

        public static void UnregisterPiece(Piece piece)
        {
            if (piece == null) return;

            int id = piece.GetInstanceID();
            if (_pieceLookup.TryGetValue(id, out TrackedInteriorPiece item))
            {
                if (item.IsCulled)
                {
                    item.Restore();
                    if (_culledPieceCount > 0) _culledPieceCount--;
                }
                if (item.IsEnclosed && _totalEnclosedCount > 0) _totalEnclosedCount--;
                _trackedPieces.Remove(item);
                _pieceLookup.Remove(id);
            }
        }

        public static void RefreshPiece(Piece piece)
        {
            if (piece == null) return;

            int id = piece.GetInstanceID();
            if (_pieceLookup.TryGetValue(id, out TrackedInteriorPiece item))
            {
                bool wasCulled = item.IsCulled;
                if (wasCulled)
                {
                    item.Restore();
                }
                item.AllRenderers = piece.GetComponentsInChildren<Renderer>(true) ?? Array.Empty<Renderer>();
                if (wasCulled)
                {
                    item.Cull();
                }
            }
        }

        public static void RestoreAllRenderers()
        {
            for (int i = 0; i < _trackedPieces.Count; i++)
            {
                TrackedInteriorPiece item = _trackedPieces[i];
                if (item.IsCulled)
                {
                    item.Restore();
                }
            }
            _culledPieceCount = 0;
        }

        public static bool IsInteriorPiece(Piece piece)
        {
            if (piece == null || piece.gameObject == null) return false;

            // Never hide fire sources or light fixtures (prevents floating lights without meshes)
            if (piece.GetComponent<Fireplace>() != null || piece.GetComponentInChildren<Light>() != null)
            {
                return false;
            }

            string name = piece.gameObject.name;

            if (OptimizationHelpers.ContainsIgnoreCase(name, "wall") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "roof") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "floor") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "gate") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "door") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "window") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "shutter") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "beam") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "pole") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "pillar") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "post") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "arch") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "stair") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "ladder") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "steps") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "support") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "foundation") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "portal") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "chimney") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "hearth") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "fire") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "bonfire") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "brazier") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "sconce") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "torch") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "lantern") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "chandelier") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "stone_") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "blackmarble_") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "ashwood_") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "turret") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "ballista") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "stake") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "cart") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "ship") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "karve") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "vikingboat") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "cultivate") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "sapling") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "plant") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "beehive") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "windmill") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "blastfurnace") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "smelter") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "kiln"))
            {
                return false;
            }

            if (piece.GetComponent<Bed>() != null ||
                piece.GetComponent<Chair>() != null ||
                piece.GetComponent<Container>() != null ||
                piece.GetComponent<ItemStand>() != null ||
                piece.GetComponent<ArmorStand>() != null ||
                piece.GetComponent<Sign>() != null)
            {
                return true;
            }

            if (piece.m_category == Piece.PieceCategory.Furniture || piece.m_comfort > 0)
            {
                return true;
            }

            if (OptimizationHelpers.ContainsIgnoreCase(name, "chest") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "bed") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "chair") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "table") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "bench") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "stool") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "throne") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "couch") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "rug") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "carpet") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "tapestry") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "banner") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "curtain") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "shelf") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "cupboard") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "bookcase") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "cabinet") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "drawer") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "barrel") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "cask") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "tub") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "armorstand") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "itemstand") ||
                OptimizationHelpers.ContainsIgnoreCase(name, "sign"))
            {
                return true;
            }

            return false;
        }

        private static readonly AccessTools.FieldRef<WearNTear, bool>? _haveRoofRef =
            AccessTools.FieldRefAccess<WearNTear, bool>("m_haveRoof");
        private static readonly AccessTools.FieldRef<WearNTear, bool>? _haveAshRoofRef =
            AccessTools.FieldRefAccess<WearNTear, bool>("m_haveAshRoof");

        private static int _pieceMask = -1;

        public static bool IsEnclosedPiece(Piece piece)
        {
            if (piece == null) return false;

            WearNTear wnt = piece.GetComponent<WearNTear>();
            if (wnt != null)
            {
                try
                {
                    if (_haveRoofRef != null && _haveRoofRef(wnt)) return true;
                    if (_haveAshRoofRef != null && _haveAshRoofRef(wnt)) return true;
                }
                catch { }
            }

            if (_pieceMask == -1)
            {
                _pieceMask = LayerMask.GetMask("piece");
            }

            Vector3 origin = piece.transform.position + Vector3.up * 0.25f;
            if (_pieceMask != 0 && Physics.Raycast(origin, Vector3.up, 20f, _pieceMask, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            return false;
        }
    }

    #region Harmony Patches

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
                if (p != null) InteriorOcclusionOptimizer.RefreshPiece(p);
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
                if (p != null) InteriorOcclusionOptimizer.RefreshPiece(p);
            }
        }
    }

    #endregion
}
