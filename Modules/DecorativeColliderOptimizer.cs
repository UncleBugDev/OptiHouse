using System;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// DEPRECATED & INERT in OptiHouse v1.4.1.
    /// Disabling colliders on structural pieces caused severe Valheim WearNTear support calculation
    /// failures (roof and structural collapse), because Valheim's physics support graph depends on
    /// active piece colliders.
    ///
    /// This module has been made completely inert and now only guarantees all piece colliders remain
    /// 100% active and intact.
    /// </summary>
    public class DecorativeColliderOptimizer : MonoBehaviour
    {
        public static int TotalDecorativePieces => 0;
        public static int CulledColliderCount => 0;
        public static bool AreCollidersCulled => false;

        public static void ScanExistingPieces() { }
        public static void RegisterPiece(Piece piece) { }
        public static void UnregisterPiece(Piece piece) { }
        public static void CullAllDecorativeColliders() { }

        public static void RestoreAllColliders()
        {
            // Safety scan: ensure all piece colliders in scene are active
            try
            {
                Piece[] pieces = FindObjectsByType<Piece>(FindObjectsSortMode.None);
                if (pieces != null)
                {
                    for (int i = 0; i < pieces.Length; i++)
                    {
                        Piece p = pieces[i];
                        if (p != null)
                        {
                            Collider[] cols = p.GetComponentsInChildren<Collider>(true);
                            if (cols != null)
                            {
                                for (int c = 0; c < cols.Length; c++)
                                {
                                    if (cols[c] != null && !cols[c].enabled)
                                    {
                                        cols[c].enabled = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public static bool IsBuildToolEquipped()
        {
            return DetailPieceLOD.IsBuildToolEquipped();
        }
    }
}
