using System;
using HarmonyLib;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Unified single-hook Piece dispatcher for OptiHouse v2.0.
    /// Replaces 4 separate Piece.Awake and 4 separate Piece.OnDestroy Harmony patches with a single pass:
    /// - Validates ghost layer / ZNetView ONCE per piece.
    /// - Queries GetComponentsInChildren&lt;Renderer&gt;(true) ONCE per piece and shares the array across subsystems.
    /// - Synchronizes WearNTear.SetHealthVisual (rain/damage/repair mesh swaps) with active LOD &amp; occlusion cullers.
    /// </summary>
    internal static class PieceRegistry
    {
        public static void ScanExistingScenePieces()
        {
            Piece[] pieces = UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None);
            if (pieces == null) return;

            for (int i = 0; i < pieces.Length; i++)
            {
                OnPieceAwake(pieces[i]);
            }
        }

        public static void OnPieceAwake(Piece? piece)
        {
            if (!OptimizationHelpers.IsValidWorldPiece(piece) || piece == null)
            {
                return;
            }

            Renderer[] renderers = piece.GetComponentsInChildren<Renderer>(true) ?? Array.Empty<Renderer>();

            if (renderers.Length > 0)
            {
                PieceShadowCuller.RegisterPiece(piece, renderers);
                DetailPieceLOD.RegisterPiece(piece, renderers);
                InteriorOcclusionOptimizer.RegisterPiece(piece, renderers);
            }

            ClothOptimizer.RegisterPiece(piece);
        }

        public static void OnPieceDestroy(Piece? piece)
        {
            if (piece == null) return;

            int pieceId = piece.GetInstanceID();
            PieceShadowCuller.UnregisterPieceById(pieceId);
            DetailPieceLOD.UnregisterPieceById(pieceId);
            InteriorOcclusionOptimizer.UnregisterPieceById(pieceId);
            ClothOptimizer.UnregisterPieceById(pieceId);
            SignOptimizer.UnregisterFromPiece(piece);
            WearNTearOptimizer.InvalidateStructureEquilibrium();
        }

        public static void OnHealthVisualChanged(WearNTear? wnt)
        {
            if (wnt == null) return;
            Piece p = wnt.GetComponent<Piece>();
            if (p == null) return;

            int pieceId = p.GetInstanceID();
            DetailPieceLOD.OnPieceVisualChanged(pieceId, p);
            InteriorOcclusionOptimizer.RefreshPieceById(pieceId, p);
        }
    }

    [HarmonyPatch(typeof(Piece), "Awake")]
    public static class Unified_Piece_Awake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Piece __instance)
        {
            PieceRegistry.OnPieceAwake(__instance);
        }
    }

    [HarmonyPatch(typeof(Piece), "OnDestroy")]
    public static class Unified_Piece_OnDestroy_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Piece __instance)
        {
            PieceRegistry.OnPieceDestroy(__instance);
        }
    }

    [HarmonyPatch(typeof(WearNTear), "SetHealthVisual")]
    public static class Unified_WearNTear_SetHealthVisual_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(WearNTear __instance)
        {
            PieceRegistry.OnHealthVisualChanged(__instance);
        }
    }
}
