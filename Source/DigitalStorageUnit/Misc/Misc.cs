using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace DigitalStorageUnit;

public class ModExtensionPortColor : DefModExtension
{
    public Color inColor = new(144f / 255f, 222f / 255f, 1f, 1f);
    public Color outColor = new(215f / 255f, 169f / 255f, 72f / 255f, 1f);
    public Color apColor = new(190f / 255f, 140f / 255f, 1f, 1f);
}

/// <summary>Min / max amounts of the output bus.</summary>
public class OutputSettings : IExposable
{
    public bool UseMin;
    public bool UseMax;
    private int min;
    private int max = 75;

    public int Min
    {
        get => min;
        set
        {
            min = Math.Max(0, value);
            if (min > max) max = min;
        }
    }

    public int Max
    {
        get => max;
        set
        {
            max = Math.Max(1, value);
            if (min > max) min = max;
        }
    }

    public bool SatisfiesMin(int stackCount) => !UseMin || stackCount >= min;

    public int CountNeededToReachMax(int currentCount, int stackLimit) =>
        (UseMax ? Math.Min(stackLimit, max) : stackLimit) - currentCount;

    public void CopyFrom(OutputSettings other)
    {
        UseMin = other.UseMin;
        UseMax = other.UseMax;
        min = other.min;
        max = other.max;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref UseMin, "useMin");
        Scribe_Values.Look(ref UseMax, "useMax");
        Scribe_Values.Look(ref min, "min");
        Scribe_Values.Look(ref max, "max", 75);
        if (min > max) max = min;
    }
}

public static class DsuRoomUtility
{
    /// <summary>Room the building stands in (or the room next to it, for impassable buildings).</summary>
    public static Room RoomOf(Thing t)
    {
        var map = t.Map;
        var room = t.Position.GetRoom(map);
        if (room != null) return room;
        foreach (var c in GenAdj.CellsAdjacent8Way(t))
        {
            if (!c.InBounds(map)) continue;
            room = c.GetRoom(map);
            if (room != null) return room;
        }
        return null;
    }

    public static Room RoomAt(IntVec3 loc, Map map, IntVec2 size)
    {
        var room = loc.GetRoom(map);
        if (room != null) return room;
        foreach (var c in GenAdj.OccupiedRect(loc, Rot4.North, size).ExpandedBy(1))
        {
            if (!c.InBounds(map)) continue;
            room = c.GetRoom(map);
            if (room != null) return room;
        }
        return null;
    }

    public static DigitalStorageUnitBuilding FindDsuInSameRoom(Thing t)
    {
        var room = RoomOf(t);
        if (room == null) return null;
        DigitalStorageUnitBuilding best = null;
        var bestDist = float.MaxValue;
        foreach (var dsu in DsuMapComponent.For(t.Map).Dsus)
        {
            if (RoomOf(dsu) != room) continue;
            var d = dsu.Position.DistanceToSquared(t.Position);
            if (d < bestDist)
            {
                bestDist = d;
                best = dsu;
            }
        }
        return best;
    }

    public static int CountDsusInRoom(Room room, Map map)
    {
        if (room == null) return 0;
        var n = 0;
        foreach (var dsu in DsuMapComponent.For(map).Dsus)
            if (RoomOf(dsu) == room) n++;
        return n;
    }

    public static bool IsHermetic(Room room) => room != null && !room.UsesOutdoorTemperature && room.OpenRoofCount == 0;
}

// ---------------------------------------------------------------------------- place workers

public class ExtenderPlaceWorker : PlaceWorker
{
    public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
    {
        var size = (checkingDef as ThingDef)?.size ?? IntVec2.One;
        var room = DsuRoomUtility.RoomAt(loc, map, size);
        var count = DsuRoomUtility.CountDsusInRoom(room, map);
        if (count == 0) return "DSU.PlaceWorker.DsuNeeded".Translate();
        if (count > 1) return "DSU.PlaceWorker.OnlyOneDsuNeeded".Translate();
        return true;
    }
}

public class DsuHeaterPlaceWorker : PlaceWorker
{
    public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
    {
        if (!DigitalStorageUnitMod.Settings.HeaterEnabled) return true;
        var room = loc.GetRoom(map);
        if (room == null || room.UsesOutdoorTemperature) return "DSU.PlaceWorker.ProperRoomNeeded".Translate();
        return true;
    }
}

// ---------------------------------------------------------------------------- room role

public class DsuRoomRoleWorker : RoomRoleWorker
{
    public override float GetScore(Room room)
    {
        if (DsuGlobal.DsuCount == 0 || room == null || room.Map == null) return 0f;
        var n = DsuRoomUtility.CountDsusInRoom(room, room.Map);
        return n * 50000f;
    }
}
