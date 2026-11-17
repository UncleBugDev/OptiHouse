using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using OptiHouse.Configuration;
using UnityEngine;

namespace OptiHouse.Modules
{
    /// <summary>
    /// Portal &amp; Mega-Base Arrival Freeze Killer (ZoneLoadPacer) for OptiHouse v2.2.
    /// Fixes two severe bottlenecks in ZNetScene.CreateObjectsSorted when arriving at a 10,000–30,000 piece base:
    /// 1. Eliminates 30x/sec O(N log N) full-list QuickSort across 20,000+ uncreated ZDOs by caching a contiguous
    ///    value-type priority/distance queue and walking a cursor across ticks (rebuilding only every 1.25s or &gt;8m move).
    /// 2. Replaces vanilla's uncapped `Count / 100` (= 200+ prefabs in a single frame!) burst with a high-resolution
    ///    Stopwatch frame-time budget (default 3.5 ms live gameplay, 18 ms during black loading/teleport screens),
    ///    streaming the base radially outward from the player's feet at a locked 60+ FPS.
    /// Normal gameplay (&lt;= 80 uncreated ZDOs) runs vanilla immediately with zero latency.
    /// </summary>
    public static class ZoneLoadPacer
    {
        private struct QueuedZdo
        {
            public ZDO Zdo;
            public int Priority;
            public float DistSqr;
        }

        private sealed class QueuedZdoComparer : IComparer<QueuedZdo>
        {
            public static readonly QueuedZdoComparer Instance = new QueuedZdoComparer();

            public int Compare(QueuedZdo x, QueuedZdo y)
            {
                if (x.Priority != y.Priority)
                {
                    return y.Priority.CompareTo(x.Priority);
                }
                return Utils.CompareFloats(x.DistSqr, y.DistSqr);
            }
        }

        private static readonly List<QueuedZdo> _sortedQueue = new List<QueuedZdo>(4096);
        private static int _cursor;
        private static float _lastSortTime = -999f;
        private static Vector3 _lastSortRefPos = new Vector3(-99999f, -99999f, -99999f);
        private static int _uncreatedSnapshotCount;

        private static readonly Func<ZNetScene, ZDO, GameObject?>? _createObjectDelegate = CreateMethodDelegate();

        public static long SortsSaved { get; internal set; }
        public static long PacedTicksCount { get; internal set; }
        public static int QueuedRemaining => Mathf.Max(0, _sortedQueue.Count - _cursor);

        private static Func<ZNetScene, ZDO, GameObject?>? CreateMethodDelegate()
        {
            try
            {
                var method = AccessTools.Method(typeof(ZNetScene), "CreateObject", new[] { typeof(ZDO) });
                if (method != null)
                {
                    return AccessTools.MethodDelegate<Func<ZNetScene, ZDO, GameObject?>>(method);
                }
            }
            catch
            {
            }
            return null;
        }

        public static void ResetQueue()
        {
            _sortedQueue.Clear();
            _cursor = 0;
            _lastSortTime = -999f;
            _uncreatedSnapshotCount = 0;
        }

        internal static bool TryRunPacedCreateObjectsSorted(
            ZNetScene scene,
            List<ZDO> currentNearObjects,
            int maxCreatedPerFrame,
            ref int created)
        {
            if (_createObjectDelegate == null ||
                scene == null ||
                currentNearObjects == null ||
                !OptiHousePlugin.IsOptimizationActive ||
                ModConfig.EnablePortalZonePacer == null ||
                !ModConfig.EnablePortalZonePacer.Value)
            {
                return false; // Run vanilla
            }

            if (ZoneSystem.instance == null || !ZoneSystem.instance.IsActiveAreaLoaded())
            {
                return true; // Handled (exact vanilla early-out)
            }

            // Fast O(N) count of uncreated objects to see if we are in a heavy zone-arrival burst
            int uncreatedCount = 0;
            int totalNear = currentNearObjects.Count;
            for (int i = 0; i < totalNear; i++)
            {
                ZDO z = currentNearObjects[i];
                if (z != null && !z.Created)
                {
                    uncreatedCount++;
                }
            }

            if (uncreatedCount <= 80)
            {
                if (_sortedQueue.Count > 0)
                {
                    ResetQueue();
                }
                return false; // Normal gameplay: run vanilla immediately with zero delay
            }

            Vector3 refPos = ZNet.instance != null ? ZNet.instance.GetReferencePosition() : OptimizationHelpers.GetReferencePosition();
            float now = Time.unscaledTime;

            bool needRebuild = _cursor >= _sortedQueue.Count ||
                               now - _lastSortTime >= 1.25f ||
                               (refPos - _lastSortRefPos).sqrMagnitude > 64f ||
                               uncreatedCount > _uncreatedSnapshotCount + 64;

            if (needRebuild)
            {
                _sortedQueue.Clear();
                if (_sortedQueue.Capacity < uncreatedCount)
                {
                    _sortedQueue.Capacity = uncreatedCount + 256;
                }

                for (int i = 0; i < totalNear; i++)
                {
                    ZDO z = currentNearObjects[i];
                    if (z != null && !z.Created)
                    {
                        float distSqr = Utils.DistanceSqr(refPos, z.GetPosition());
                        z.m_tempSortValue = distSqr;
                        _sortedQueue.Add(new QueuedZdo
                        {
                            Zdo = z,
                            Priority = (int)z.Type,
                            DistSqr = distSqr
                        });
                    }
                }

                _sortedQueue.Sort(QueuedZdoComparer.Instance);
                _cursor = 0;
                _lastSortTime = now;
                _lastSortRefPos = refPos;
                _uncreatedSnapshotCount = uncreatedCount;
            }
            else
            {
                SortsSaved++;
            }

            bool inLoadingScreen = Player.m_localPlayer == null || Player.m_localPlayer.IsTeleporting();
            float budgetMs = inLoadingScreen
                ? 18.0f
                : (ModConfig.PortalSpawnBudgetMs != null ? Mathf.Clamp(ModConfig.PortalSpawnBudgetMs.Value, 1.0f, 15.0f) : 3.5f);

            long budgetTicks = (long)(budgetMs * (Stopwatch.Frequency / 1000.0));
            long startStamp = Stopwatch.GetTimestamp();

            int hardCap = inLoadingScreen ? 250 : Mathf.Max(uncreatedCount / 100, maxCreatedPerFrame);
            int spawnedThisCall = 0;
            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();

            while (_cursor < _sortedQueue.Count)
            {
                QueuedZdo entry = _sortedQueue[_cursor];
                _cursor++;

                ZDO item = entry.Zdo;
                if (item == null || !item.IsValid() || item.Created)
                {
                    continue;
                }

                if (!ZoneSystem.instance.IsZoneReadyForType(item.GetSector(), item.Type))
                {
                    continue;
                }

                if (_createObjectDelegate(scene, item) != null)
                {
                    created++;
                    spawnedThisCall++;

                    if (created >= hardCap)
                    {
                        break;
                    }

                    // Always spawn at least 2 prefabs so streaming never stalls, then enforce exact ms budget
                    if (spawnedThisCall >= 2 && (Stopwatch.GetTimestamp() - startStamp) >= budgetTicks)
                    {
                        PacedTicksCount++;
                        // Signal CreateDistantObjects to wait for next tick since frame budget is full
                        if (created <= maxCreatedPerFrame)
                        {
                            created = maxCreatedPerFrame + 1;
                        }
                        break;
                    }
                }
                else if (isServer && ZDOMan.instance != null)
                {
                    item.SetOwner(ZDOMan.GetSessionID());
                    ZLog.Log("Destroyed invalid prefab ZDO:" + item.m_uid);
                    ZDOMan.instance.DestroyZDO(item);
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "CreateObjectsSorted")]
    public static class ZNetScene_CreateObjectsSorted_Pacer_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            ZNetScene __instance,
            List<ZDO> currentNearObjects,
            int maxCreatedPerFrame,
            ref int created)
        {
            return !ZoneLoadPacer.TryRunPacedCreateObjectsSorted(
                __instance,
                currentNearObjects,
                maxCreatedPerFrame,
                ref created);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "OnDestroy")]
    public static class ZNetScene_OnDestroy_Pacer_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            ZoneLoadPacer.ResetQueue();
        }
    }
}
