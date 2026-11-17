using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Under-Foundation Grass Clutter Suppressor for OptiHouse v2.1.
    /// Valheim's ClutterSystem raycasts exclusively against the Heightmap terrain layer and spawns dense instanced
    /// grass patches underneath wood/stone base floors raised 0.05m–2.0m above uncultivated soil, wasting GPU
    /// InstanceRenderer draw calls and poking grass blades through floorboards.
    /// Suppresses grass placement strictly directly beneath solid ground-level building pieces (`piece` layer)
    /// while leaving courtyards, gardens, crops (`piece_nonsolid`), and high bridges untouched.
    /// </summary>
    public class ClutterOptimizer : MonoBehaviour
    {
        public static ClutterOptimizer? Instance { get; private set; }
        public static long UnderFloorTuftsSuppressed { get; internal set; }

        private static int _pieceLayerMask;
        private bool _wasActive = true;

        internal static int PieceLayerMask
        {
            get
            {
                if (_pieceLayerMask == 0)
                {
                    _pieceLayerMask = LayerMask.GetMask("piece");
                }
                return _pieceLayerMask;
            }
        }

        private static bool IsActive()
        {
            return OptiHousePlugin.IsOptimizationActive &&
                   ModConfig.SuppressUnderFloorGrass != null &&
                   ModConfig.SuppressUnderFloorGrass.Value;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            bool active = IsActive();
            if (active != _wasActive)
            {
                _wasActive = active;
                if (ClutterSystem.instance != null)
                {
                    ClutterSystem.instance.ClearAll();
                }
            }
        }
    }

    [HarmonyPatch(typeof(ClutterSystem), nameof(ClutterSystem.GetGroundInfo))]
    public static class ClutterSystem_GetGroundInfo_UnderFloor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result, Vector3 point)
        {
            if (!__result ||
                !OptiHousePlugin.IsOptimizationActive ||
                ModConfig.SuppressUnderFloorGrass == null ||
                !ModConfig.SuppressUnderFloorGrass.Value)
            {
                return;
            }

            // Only perform foundation check when near player structures
            if (OptimizationHelpers.GetTotalPieceCount() < 40)
            {
                return;
            }

            int mask = ClutterOptimizer.PieceLayerMask;
            if (mask == 0)
            {
                return;
            }

            // Check if a solid floor or foundation slab sits directly above the soil (0.05m to 2.10m clearance)
            Vector3 origin = new Vector3(point.x, point.y + 2.10f, point.z);
            if (Physics.Raycast(origin, Vector3.down, 2.05f, mask, QueryTriggerInteraction.Ignore))
            {
                __result = false;
                ClutterOptimizer.UnderFloorTuftsSuppressed++;
            }
        }
    }
}
