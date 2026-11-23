using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DigitalStorageUnit;

/// <summary>
/// Input Bus: pawns (or other mods' conveyors) drop items here, they are teleported into the bound DSU.
/// Uses the DSU's storage settings, so it only attracts items the DSU wants.
/// With a work cell offset it also "sucks" loose items from the adjacent cell.
/// </summary>
public class InputPortDsuBuilding : ABasePortDsuBuilding
{
    private const int MaxItemsPerPass = 8;
    private static readonly List<Thing> Buffer = [];

    protected override Color PortColor(ModExtensionPortColor ext) => ext.inColor;

    public override bool BlocksPawnInput(Thing t)
    {
        if (t.Spawned && t.Map == Map && t.Position == Position) return false;
        return !IsOperational || !BoundDsu.CanAcceptFromPort(t);
    }

    public override void Notify_ReceivedThing(Thing newItem)
    {
        base.Notify_ReceivedThing(newItem);
        if (Current.ProgramState != ProgramState.Playing) return;
        if (newItem.def.category == ThingCategory.Item && IsOperational && newItem.Spawned && newItem.Position == Position)
            BoundDsu.AcceptFromPort(newItem);
    }

    protected override void OnBindingChanged()
    {
        if (Spawned && Current.ProgramState == ProgramState.Playing) Pull();
    }

    protected override void Tick()
    {
        base.Tick();
        if (!Spawned || !this.IsHashIntervalTick(30)) return;
        Pull();
    }

    private void Pull()
    {
        if (!IsOperational) return;
        var dsu = BoundDsu;
        var map = Map;
        var moved = 0;

        // own cell
        foreach (var t in ItemsAt(Position, map, Buffer))
        {
            if (moved >= MaxItemsPerPass) return;
            if (IsReserved(t)) continue;
            if (dsu.AcceptFromPort(t)) moved++;
        }

        // adjacent work cell: loose items only (never steal from another storage)
        var cell = WorkCell;
        if (cell == Position || !cell.InBounds(map) || cell.GetSlotGroup(map) != null) return;
        if (DsuMapComponent.DsuAt(map, cell) != null) return;
        foreach (var t in ItemsAt(cell, map, Buffer))
        {
            if (moved >= MaxItemsPerPass) return;
            if (IsReserved(t) || t.IsForbidden(Faction.OfPlayer)) continue;
            if (dsu.AcceptFromPort(t)) moved++;
        }
    }
}
