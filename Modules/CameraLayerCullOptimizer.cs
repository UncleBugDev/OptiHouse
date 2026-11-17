using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Hardware C++ SIMD Spherical Per-Layer Camera Culling for OptiHouse v2.2.
    /// Valheim leaves GameCamera's layerCullDistances at 0 (culling small clutter only at farClipPlane ~1000m+).
    /// Configures spherical per-layer culling strictly on tiny non-structural layers:
    /// - 'item' (loose ground loot)
    /// - 'Default_small' (pebbles, twigs, small forest debris, fragments)
    /// - 'piece_nonsolid' (rugs, signs, seedlings, tiny non-solid trim)
    /// Unity's multi-threaded C++ culling job skips both main-camera and directional shadow-cascade passes
    /// for out-of-range clutter with zero C# per-object overhead. Main 'piece', 'terrain', and 'Default' layers
    /// remain 100% untouched. Fully snapshot-backed and reversible via F7.
    /// </summary>
    public class CameraLayerCullOptimizer : MonoBehaviour
    {
        public static CameraLayerCullOptimizer? Instance { get; private set; }
        public static bool IsHardwareLayerCullActive { get; private set; }

        private static Camera? _trackedCamera;
        private static float[]? _origCullDistances;
        private static bool _origSpherical;
        private static bool _hasSnapshot;

        private static float _appliedItemDist = -1f;
        private static float _appliedSmallDist = -1f;
        private static float _appliedNonSolidDist = -1f;

        private float _syncTimer;

        private static bool ShouldEnable()
        {
            return OptiHousePlugin.IsOptimizationActive &&
                   ModConfig.EnableCameraLayerCulling != null &&
                   ModConfig.EnableCameraLayerCulling.Value;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            RestoreVanillaCameraCull();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!ShouldEnable())
            {
                if (IsHardwareLayerCullActive)
                {
                    RestoreVanillaCameraCull();
                }
                return;
            }

            _syncTimer += Time.unscaledDeltaTime;
            if (_syncTimer < 1.0f && IsHardwareLayerCullActive)
            {
                return;
            }
            _syncTimer = 0f;

            if (GameCamera.instance != null)
            {
                ApplyToGameCamera(GameCamera.instance);
            }
        }

        public static void ApplyToGameCamera(GameCamera? gameCam)
        {
            if (gameCam == null || !ShouldEnable()) return;

            Camera cam = gameCam.GetComponent<Camera>();
            if (cam == null) return;

            float itemDist = ModConfig.ItemLayerCullDistance != null ? ModConfig.ItemLayerCullDistance.Value : 55f;
            float smallDist = ModConfig.SmallDecorLayerCullDistance != null ? ModConfig.SmallDecorLayerCullDistance.Value : 65f;
            float nonSolidDist = ModConfig.NonSolidPieceLayerCullDistance != null ? ModConfig.NonSolidPieceLayerCullDistance.Value : 80f;

            if (_trackedCamera == cam &&
                IsHardwareLayerCullActive &&
                Mathf.Approximately(_appliedItemDist, itemDist) &&
                Mathf.Approximately(_appliedSmallDist, smallDist) &&
                Mathf.Approximately(_appliedNonSolidDist, nonSolidDist))
            {
                return;
            }

            if (_trackedCamera != cam)
            {
                _trackedCamera = cam;
                float[] current = cam.layerCullDistances;
                _origCullDistances = new float[32];
                if (current != null && current.Length == 32)
                {
                    System.Array.Copy(current, _origCullDistances, 32);
                }
                _origSpherical = cam.layerCullSpherical;
                _hasSnapshot = true;
            }

            float[] updated = new float[32];
            if (_origCullDistances != null)
            {
                System.Array.Copy(_origCullDistances, updated, 32);
            }

            int itemLayer = LayerMask.NameToLayer("item");
            int smallLayer = LayerMask.NameToLayer("Default_small");
            int nonSolidLayer = LayerMask.NameToLayer("piece_nonsolid");

            if (itemLayer >= 0 && itemLayer < 32) updated[itemLayer] = itemDist;
            if (smallLayer >= 0 && smallLayer < 32) updated[smallLayer] = smallDist;
            if (nonSolidLayer >= 0 && nonSolidLayer < 32) updated[nonSolidLayer] = nonSolidDist;

            cam.layerCullSpherical = true;
            cam.layerCullDistances = updated;

            _appliedItemDist = itemDist;
            _appliedSmallDist = smallDist;
            _appliedNonSolidDist = nonSolidDist;
            IsHardwareLayerCullActive = true;
        }

        public static void RestoreVanillaCameraCull()
        {
            if (_trackedCamera != null && _hasSnapshot && _origCullDistances != null)
            {
                float[] restored = new float[32];
                System.Array.Copy(_origCullDistances, restored, 32);
                _trackedCamera.layerCullDistances = restored;
                _trackedCamera.layerCullSpherical = _origSpherical;
            }

            IsHardwareLayerCullActive = false;
            _appliedItemDist = -1f;
            _appliedSmallDist = -1f;
            _appliedNonSolidDist = -1f;
        }
    }

    [HarmonyPatch(typeof(GameCamera), "Awake")]
    public static class GameCamera_Awake_LayerCull_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(GameCamera __instance)
        {
            CameraLayerCullOptimizer.ApplyToGameCamera(__instance);
        }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.ApplySettings))]
    public static class GameCamera_ApplySettings_LayerCull_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(GameCamera __instance)
        {
            CameraLayerCullOptimizer.ApplyToGameCamera(__instance);
        }
    }
}
