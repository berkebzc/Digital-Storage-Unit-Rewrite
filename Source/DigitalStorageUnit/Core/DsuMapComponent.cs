using System.Collections.Generic;
using Verse;

namespace DigitalStorageUnit;

/// <summary>
/// Per-map registry of every DSU building.
/// Holds a flat cell grid so that the hot Harmony patches (Print, DrawGUIOverlay, selection ...)
/// can answer "is this cell part of a DSU?" with a single array read.
/// Nothing in here is saved - it is rebuilt from SpawnSetup calls.
/// </summary>
public class DsuMapComponent : MapComponent
{
    public readonly List<DigitalStorageUnitBuilding> Dsus = [];
    public readonly List<ABasePortDsuBuilding> Ports = [];
    public readonly List<AccessPointPortBuilding> AccessPoints = [];
    public readonly List<DataExtenderBuilding> Extenders = [];

    private DigitalStorageUnitBuilding[] dsuGrid;

    // --- fast static lookup (avoids Map.GetComponent list scan in hot paths) ---
    private static Map lastMap;
    private static DsuMapComponent lastComp;

    public DsuMapComponent(Map map) : base(map)
    {
    }

    public static void ClearCache()
    {
        lastMap = null;
        lastComp = null;
    }

    public static DsuMapComponent For(Map map)
    {
        if (map == null) return null;
        if (ReferenceEquals(map, lastMap)) return lastComp;
        var comp = map.GetComponent<DsuMapComponent>();
        if (comp == null)
        {
            comp = new DsuMapComponent(map);
            map.components.Add(comp);
        }
        lastMap = map;
        lastComp = comp;
        return comp;
    }

    private DigitalStorageUnitBuilding[] Grid => dsuGrid ??= new DigitalStorageUnitBuilding[map.cellIndices.NumGridCells];

    /// <summary>DSU occupying the cell or null. O(1).</summary>
    public DigitalStorageUnitBuilding DsuAt(IntVec3 c)
    {
        if (dsuGrid == null || !c.InBounds(map)) return null;
        return dsuGrid[map.cellIndices.CellToIndex(c)];
    }

    /// <summary>Fast check used by the patches. Expects a spawned item.</summary>
    public static bool IsInDsuCell(Thing t)
    {
        if (DsuGlobal.DsuCount == 0) return false;
        var map = t.Map; // null when not spawned
        if (map == null) return false;
        var comp = For(map);
        return comp.dsuGrid != null && comp.DsuAt(t.Position) != null;
    }

    public static DigitalStorageUnitBuilding DsuAt(Map map, IntVec3 c)
    {
        if (DsuGlobal.DsuCount == 0 || map == null) return null;
        return For(map).DsuAt(c);
    }

    // ------------------------------------------------------------------ registration

    public void Register(DigitalStorageUnitBuilding dsu)
    {
        if (Dsus.Contains(dsu)) return;
        Dsus.Add(dsu);
        var grid = Grid;
        foreach (var c in dsu.OccupiedRect())
            if (c.InBounds(map)) grid[map.cellIndices.CellToIndex(c)] = dsu;
        DsuGlobal.DsuCount++;
    }

    public void Deregister(DigitalStorageUnitBuilding dsu)
    {
        if (!Dsus.Remove(dsu)) return;
        if (dsuGrid != null)
            foreach (var c in dsu.OccupiedRect())
            {
                if (!c.InBounds(map)) continue;
                var i = map.cellIndices.CellToIndex(c);
                if (dsuGrid[i] == dsu) dsuGrid[i] = null;
            }
        DsuGlobal.DsuCount--;
        // Ports keep their reference: a reinstalled (minified) DSU stays linked,
        // a destroyed one reads as "no DSU" through ABasePortDsuBuilding.BoundDsu.
    }

    public void Register(ABasePortDsuBuilding port)
    {
        if (Ports.Contains(port)) return;
        Ports.Add(port);
        if (port is AccessPointPortBuilding ap)
        {
            AccessPoints.Add(ap);
            DsuGlobal.AccessPointCount++;
            Patches.ConditionalPatches.Refresh();
        }
    }

    public void Deregister(ABasePortDsuBuilding port)
    {
        if (!Ports.Remove(port)) return;
        if (port is AccessPointPortBuilding ap && AccessPoints.Remove(ap))
        {
            DsuGlobal.AccessPointCount--;
            Patches.ConditionalPatches.Refresh();
        }
    }

    public void Register(DataExtenderBuilding ext)
    {
        if (!Extenders.Contains(ext)) Extenders.Add(ext);
    }

    public void Deregister(DataExtenderBuilding ext) => Extenders.Remove(ext);

    public override void MapRemoved()
    {
        base.MapRemoved();
        // Keep global counters consistent when a map is discarded without DeSpawn calls.
        DsuGlobal.DsuCount -= Dsus.Count;
        DsuGlobal.AccessPointCount -= AccessPoints.Count;
        foreach (var dsu in Dsus)
            if (!dsu.PawnAccess) DsuGlobal.PawnAccessDisabledCount--;
        if (DsuGlobal.PawnAccessDisabledCount < 0) DsuGlobal.PawnAccessDisabledCount = 0;
        if (DsuGlobal.DsuCount < 0) DsuGlobal.DsuCount = 0;
        if (DsuGlobal.AccessPointCount < 0) DsuGlobal.AccessPointCount = 0;
        Dsus.Clear();
        Ports.Clear();
        AccessPoints.Clear();
        Extenders.Clear();
        dsuGrid = null;
        if (ReferenceEquals(lastMap, map))
        {
            lastMap = null;
            lastComp = null;
        }
        Patches.ConditionalPatches.Refresh();
    }
}

/// <summary>
/// Global counters used as an ultra-cheap early-out by every patch.
/// </summary>
public static class DsuGlobal
{
    public static int DsuCount;
    public static int AccessPointCount;
    public static int PawnAccessDisabledCount;

    /// <summary>Reset on game load / new game (see GameComponent).</summary>
    public static void Reset()
    {
        DsuCount = 0;
        AccessPointCount = 0;
        PawnAccessDisabledCount = 0;
    }
}

/// <summary>
/// Resets static state when a new game is started/loaded so counters never leak between saves.
/// </summary>
public class DsuGameComponent : GameComponent
{
    public DsuGameComponent(Game game)
    {
        DsuGlobal.Reset();
        DsuMapComponent.ClearCache();
        Patches.ConditionalPatches.Refresh();
    }
}
