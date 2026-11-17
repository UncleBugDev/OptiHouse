using System;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Shared zero-allocation helpers for OptiHouse optimization modules.
    /// </summary>
    internal static class OptimizationHelpers
    {
        private static int _ghostLayer = -2;

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

        /// <summary>
        /// Returns the active camera position (accurate for third-person zoom and freefly),
        /// falling back to local player position if no camera is available.
        /// </summary>
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
        /// Filters out hammer placement ghosts and uninitialized prefab templates.
        /// </summary>
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

        /// <summary>
        /// Filters out placement ghosts on non-Piece components (ItemStand, Smelter, Fireplace, etc.).
        /// </summary>
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

        /// <summary>
        /// Zero-allocation case-insensitive substring check.
        /// </summary>
        public static bool ContainsIgnoreCase(string source, string value)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
