using System;
using BepInEx.Bootstrap;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Shared zero-allocation helpers for OptiHouse v2.0 optimization modules.
    /// </summary>
    internal static class OptimizationHelpers
    {
        private static int _ghostLayer = -2;
        private static int _vpoInstalledState = -1;

        public static int GhostLayer
        {
            get
            {
                if (_ghostLayer == -2)
                {
                    _ghostLayer = LayerMask.NameToLayer("ghost");
                }
                return _ghostLayer;
            }
        }

        public static bool IsVpoInstalled
        {
            get
            {
                if (_vpoInstalledState == -1)
                {
                    try
                    {
                        _vpoInstalledState = Chainloader.PluginInfos != null &&
                                             Chainloader.PluginInfos.ContainsKey("dev.ontrigger.vpo")
                            ? 1
                            : 0;
                    }
                    catch
                    {
                        _vpoInstalledState = 0;
                    }
                }
                return _vpoInstalledState == 1;
            }
        }

        public static Vector3 GetReferencePosition()
        {
            Camera mainCam = Utils.GetMainCamera();
            if (mainCam != null)
            {
                return mainCam.transform.position;
            }

            if (Player.m_localPlayer != null)
            {
                return Player.m_localPlayer.transform.position;
            }

            return Vector3.zero;
        }

        public static bool TryGetReferencePosition(out Vector3 pos)
        {
            Camera mainCam = Utils.GetMainCamera();
            if (mainCam != null)
            {
                pos = mainCam.transform.position;
                return true;
            }

            if (Player.m_localPlayer != null)
            {
                pos = Player.m_localPlayer.transform.position;
                return true;
            }

            pos = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Retrieves camera position and forward vector in one call for combined distance + frustum checks.
        /// </summary>
        public static bool TryGetCameraView(out Vector3 camPos, out Vector3 camForward)
        {
            Camera mainCam = Utils.GetMainCamera();
            if (mainCam != null)
            {
                Transform t = mainCam.transform;
                camPos = t.position;
                camForward = t.forward;
                return true;
            }

            if (Player.m_localPlayer != null)
            {
                Transform t = Player.m_localPlayer.transform;
                camPos = t.position;
                camForward = t.forward;
                return true;
            }

            camPos = Vector3.zero;
            camForward = Vector3.forward;
            return false;
        }

        /// <summary>
        /// Fast zero-alloc check whether worldPos is safely behind the camera's view cone
        /// and beyond a minimum safety margin (default 6m = 36 sqrMeters) so objects right next to the camera never pop.
        /// </summary>
        public static bool IsBehindCamera(Vector3 worldPos, Vector3 camPos, Vector3 camForward, float minSqrDist = 36f)
        {
            float dx = worldPos.x - camPos.x;
            float dy = worldPos.y - camPos.y;
            float dz = worldPos.z - camPos.z;
            float sqrDist = dx * dx + dy * dy + dz * dz;
            if (sqrDist < minSqrDist)
            {
                return false;
            }

            // Dot product < -2.0m margin behind camera plane
            float dot = dx * camForward.x + dy * camForward.y + dz * camForward.z;
            return dot < -2.0f;
        }

        public static bool IsValidWorldPiece(Piece? piece)
        {
            if (piece == null || piece.gameObject == null)
            {
                return false;
            }

            int ghostLayer = GhostLayer;
            if (ghostLayer >= 0 && piece.gameObject.layer == ghostLayer)
            {
                return false;
            }

            ZNetView nview = piece.GetComponent<ZNetView>();
            if (nview != null && !nview.IsValid())
            {
                return false;
            }

            return true;
        }

        public static bool IsValidWorldObject(Component? component)
        {
            if (component == null || component.gameObject == null)
            {
                return false;
            }

            int ghostLayer = GhostLayer;
            if (ghostLayer >= 0 && component.gameObject.layer == ghostLayer)
            {
                return false;
            }

            ZNetView nview = component.GetComponentInParent<ZNetView>();
            if (nview != null && !nview.IsValid())
            {
                return false;
            }

            return true;
        }

        public static bool ContainsIgnoreCase(string source, string value)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
