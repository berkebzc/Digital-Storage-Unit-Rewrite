using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace DigitalStorageUnit.Patches;

/// <summary>
/// Patches on very hot methods are only applied while they are actually needed:
///  * ForbidUtility.IsForbidden  -> only while at least one DSU has "pawn access" switched off
///  * Pawn_JobTracker.StartJob   -> only while at least one Access Point exists
/// When unpatched they cost exactly nothing.
/// </summary>
public static class ConditionalPatches
{
    private static Harmony harmony;

    private static readonly TogglePatch ForbidPatch = new(
        () => AccessTools.Method(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), [typeof(Thing), typeof(Pawn)]),
        prefix: AccessTools.Method(typeof(Patch_ForbidUtility_IsForbidden), nameof(Patch_ForbidUtility_IsForbidden.Prefix)));

    private static readonly TogglePatch StartJobPatch = new(
        () => AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)),
        prefix: AccessTools.Method(typeof(Patch_AccessPoint_StartJob), nameof(Patch_AccessPoint_StartJob.Prefix)));

    public static void Init(Harmony h)
    {
        harmony = h;
        Refresh();
    }

    public static void Refresh()
    {
        if (harmony == null) return;
        ForbidPatch.Set(harmony, DsuGlobal.PawnAccessDisabledCount > 0);
        StartJobPatch.Set(harmony, DsuGlobal.AccessPointCount > 0);
    }

    private sealed class TogglePatch(Func<MethodBase> original, MethodInfo prefix)
    {
        private bool active;
        private MethodBase target;

        public void Set(Harmony h, bool enable)
        {
            if (enable == active) return;
            try
            {
                target ??= original();
                if (target == null || prefix == null)
                {
                    Log.ErrorOnce("[DSU] conditional patch target not found: " + prefix?.Name, prefix?.GetHashCode() ?? 0);
                    return;
                }
                if (enable) h.Patch(target, prefix: new HarmonyMethod(prefix));
                else h.Unpatch(target, prefix);
                active = enable;
            }
            catch (Exception e)
            {
                Log.Error("[DSU] toggling patch " + prefix?.Name + " failed: " + e);
            }
        }
    }
}

/// <summary>"Pawn access" switched off: items inside that DSU are forbidden for pawns.</summary>
public static class Patch_ForbidUtility_IsForbidden
{
    public static bool Prefix(Thing t, ref bool __result)
    {
        if (t == null || t.def.category != ThingCategory.Item) return true;
        var map = t.Map;
        if (map == null) return true;
        var dsu = DsuMapComponent.For(map).DsuAt(t.Position);
        if (dsu == null || dsu.PawnAccess) return true;
        __result = true;
        return false;
    }
}

/// <summary>
/// Access Point routing.
/// When a pawn starts a job whose targets are stored in a DSU, the item is teleported onto the bound
/// access point that gives the shortest pawn -> item -> destination trip.
/// Distance is straight-line (cheap); only the chosen AP gets one reachability check.
/// </summary>
public static class Patch_AccessPoint_StartJob
{
    private const string PickUpAndHaulJob = "HaulToInventory";

    public static void Prefix(Job newJob, Pawn ___pawn)
    {
        try
        {
            if (DsuGlobal.AccessPointCount == 0 || newJob?.def == null) return;
            var pawn = ___pawn;
            if (pawn is not { Spawned: true } || pawn.Faction is not { IsPlayer: true }) return;
            if (newJob.def.defName == PickUpAndHaulJob) return;
            var comp = DsuMapComponent.For(pawn.Map);
            if (comp.AccessPoints.Count == 0 || comp.Dsus.Count == 0) return;
            if (!AnyTargetInDsu(newJob, comp)) return;

            var dest = FindDestination(newJob, pawn, comp);
            Route(newJob.targetA, pawn, dest, comp);
            Route(newJob.targetB, pawn, dest, comp);
            Route(newJob.targetC, pawn, dest, comp);
            RouteQueue(newJob.targetQueueA, pawn, dest, comp);
            RouteQueue(newJob.targetQueueB, pawn, dest, comp);
        }
        catch (Exception e)
        {
            Log.ErrorOnce("[DSU] Access point routing failed: " + e, 0x5D5A11);
        }
    }

    private static bool InDsu(LocalTargetInfo target, DsuMapComponent comp)
    {
        var t = target.Thing;
        return t != null && t.def.category == ThingCategory.Item && t.Spawned && t.Map == comp.map && comp.DsuAt(t.Position) != null;
    }

    private static bool AnyTargetInDsu(Job job, DsuMapComponent comp)
    {
        if (InDsu(job.targetA, comp) || InDsu(job.targetB, comp) || InDsu(job.targetC, comp)) return true;
        if (job.targetQueueA != null)
            foreach (var t in job.targetQueueA)
                if (InDsu(t, comp)) return true;
        if (job.targetQueueB != null)
            foreach (var t in job.targetQueueB)
                if (InDsu(t, comp)) return true;
        return false;
    }

    /// <summary>First target that is not a stored item = where the pawn is going with it.</summary>
    private static IntVec3 FindDestination(Job job, Pawn pawn, DsuMapComponent comp)
    {
        foreach (var target in new[] { job.targetA, job.targetB, job.targetC })
        {
            if (!target.IsValid) continue;
            if (target.HasThing)
            {
                var t = target.Thing;
                if (!t.Spawned || t.Map != pawn.Map) continue;
                if (t.def.category == ThingCategory.Item && comp.DsuAt(t.Position) != null) continue;
                return t.Position;
            }
            return target.Cell;
        }
        return pawn.Position;
    }

    private static void RouteQueue(List<LocalTargetInfo> queue, Pawn pawn, IntVec3 dest, DsuMapComponent comp)
    {
        if (queue == null) return;
        for (var i = 0; i < queue.Count; i++) Route(queue[i], pawn, dest, comp);
    }

    private static void Route(LocalTargetInfo target, Pawn pawn, IntVec3 dest, DsuMapComponent comp)
    {
        if (!InDsu(target, comp)) return;
        var t = target.Thing;
        var dsu = comp.DsuAt(t.Position);
        if (!dsu.Contains(t)) return;
        var map = comp.map;
        if (map.reservationManager.IsReservedByAnyoneOf(t, pawn.Faction)) return;

        var pawnPos = pawn.Position;
        var itemPos = t.Position;
        var best = (AccessPointPortBuilding)null;
        var bestCost = (pawnPos.DistanceTo(itemPos) + itemPos.DistanceTo(dest)) * DigitalStorageUnitMod.Settings.DsuPathingMultiplier;
        var aps = comp.AccessPoints;
        for (var i = 0; i < aps.Count; i++)
        {
            var ap = aps[i];
            if (ap.BoundDsu != dsu || !ap.CanProvide) continue;
            var apPos = ap.Position;
            var cost = pawnPos.DistanceTo(apPos) + apPos.DistanceTo(dest);
            if (cost >= bestCost) continue;
            bestCost = cost;
            best = ap;
        }
        if (best == null) return;
        if (!pawn.CanReach(best, PathEndMode.ClosestTouch, Danger.Deadly)) return;
        best.Provide(t);
    }
}
