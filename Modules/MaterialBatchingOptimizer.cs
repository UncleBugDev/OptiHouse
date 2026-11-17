using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Fixes two vanilla Valheim engine bugs that break Unity GPU Instancing (DrawMeshInstanced) in bases:
    /// 1. MaterialMan.PropertyContainer.UpdateBlock(): When all shader overrides are removed (e.g., 0.2s after
    ///    WearNTear.ResetHighlight() when hovering a Hammer over walls), vanilla passes a non-null empty
    ///    MaterialPropertyBlock to Renderer.SetPropertyBlock(m_propertyBlock). In Unity C++, only SetPropertyBlock(null)
    ///    frees the native ShaderPropertySheet (HasPropertyBlock() == false) to re-enable GPU Instancing.
    /// 2. RandomMaterialValues.CheckMaterial(): Grausten, Black Marble, and Ashwood pieces attach a unique per-piece
    ///    RandMatSeed MaterialPropertyBlock on spawn, disabling DrawMeshInstanced across 100% of stone/marble castles.
    /// </summary>
    public static class MaterialBatchingOptimizer
    {
        public static long EmptyPropertyBlocksCleared { get; internal set; }
        public static long PieceRandomBlocksPrevented { get; internal set; }

        internal static readonly Type? PropertyContainerType =
            typeof(MaterialMan).GetNestedType("PropertyContainer", BindingFlags.NonPublic | BindingFlags.Public);

        internal static readonly FieldInfo? ShaderPropertiesField =
            PropertyContainerType?.GetField("m_shaderProperties", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        internal static readonly FieldInfo? AssignedRenderersField =
            PropertyContainerType?.GetField("m_assignedRenderers", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
    }

    [HarmonyPatch]
    public static class MaterialMan_PropertyContainer_UpdateBlock_Patch
    {
        public static bool Prepare()
        {
            return TargetMethod() != null &&
                   MaterialBatchingOptimizer.ShaderPropertiesField != null &&
                   MaterialBatchingOptimizer.AssignedRenderersField != null;
        }

        public static MethodBase? TargetMethod()
        {
            return MaterialBatchingOptimizer.PropertyContainerType?.GetMethod(
                "UpdateBlock",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        [HarmonyPostfix]
        public static void Postfix(object __instance)
        {
            if (__instance == null ||
                !OptiHousePlugin.IsOptimizationActive ||
                ModConfig.FixMaterialManInstancingBug == null ||
                !ModConfig.FixMaterialManInstancingBug.Value)
            {
                return;
            }

            if (MaterialBatchingOptimizer.ShaderPropertiesField?.GetValue(__instance) is not ICollection props ||
                props.Count != 0)
            {
                return;
            }

            if (MaterialBatchingOptimizer.AssignedRenderersField?.GetValue(__instance) is not List<Renderer> renderers)
            {
                return;
            }

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer ren = renderers[i];
                if (ren != null)
                {
                    ren.SetPropertyBlock(null);
                    MaterialBatchingOptimizer.EmptyPropertyBlocksCleared++;
                }
            }
        }
    }

    [HarmonyPatch(typeof(RandomMaterialValues), "CheckMaterial")]
    public static class RandomMaterialValues_CheckMaterial_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(RandomMaterialValues __instance, Piece ___m_piece)
        {
            if (__instance == null ||
                ___m_piece == null ||
                !OptiHousePlugin.IsOptimizationActive ||
                ModConfig.StripPieceRandomMaterialBlocks == null ||
                !ModConfig.StripPieceRandomMaterialBlocks.Value)
            {
                return true;
            }

            if (!OptimizationHelpers.IsValidWorldPiece(___m_piece))
            {
                return true;
            }

            // Stop repeating timer and keep renderer free of non-instanced MaterialPropertyBlocks
            __instance.CancelInvoke("CheckMaterial");
            MaterialBatchingOptimizer.PieceRandomBlocksPrevented++;
            return false;
        }
    }
}
