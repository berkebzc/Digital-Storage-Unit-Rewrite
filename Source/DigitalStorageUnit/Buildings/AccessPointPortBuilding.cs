using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DigitalStorageUnit;

/// <summary>
/// Access Point: when a pawn starts a job that needs an item stored in the DSU and this access point
/// is "closer" (pawn -> AP -> job destination), the item is teleported here (see Patch_AccessPoint_StartJob).
/// Unused leftovers are sent back after a short delay. Items hauled onto it are stored into the DSU
/// (it shares the DSU storage settings).
/// </summary>
public class AccessPointPortBuilding : ABasePortDsuBuilding
{
    private static readonly List<Thing> Buffer = [];
    private static readonly List<Thing> RemoveBuffer = [];

    /// <summary>thing -> tick it was provided</summary>
    private readonly Dictionary<Thing, int> provided = new();

    protected override Color PortColor(ModExtensionPortColor ext) => ext.apColor;

    public bool CanProvide => IsOperational;

    public override bool BlocksPawnInput(Thing t)
    {
        if (t.Spawned && t.Map == Map && t.Position == Position) return false;
        return !IsOperational || !BoundDsu.CanAcceptFromPort(t);
    }

    /// <summary>Teleport a stored item onto this access point.</summary>
    public void Provide(Thing t)
    {
        var dsu = BoundDsu;
        if (dsu == null || !dsu.Contains(t) || !t.Spawned) return;
        dsu.ReleaseForMove(t);
        DigitalStorageUnitBuilding.Teleport(t, Position);
        provided[t] = Find.TickManager.TicksGame;
    }

    public override void Notify_ReceivedThing(Thing newItem)
    {
        base.Notify_ReceivedThing(newItem);
        if (Current.ProgramState != ProgramState.Playing) return;
        // Hauled in by a pawn -> store it
        if (newItem.def.category == ThingCategory.Item && !provided.ContainsKey(newItem) && IsOperational
            && newItem.Spawned && newItem.Position == Position)
            BoundDsu.AcceptFromPort(newItem);
    }

    public override void Notify_LostThing(Thing newItem)
    {
        base.Notify_LostThing(newItem);
        provided.Remove(newItem);
    }

    protected override void Tick()
    {
        base.Tick();
        if (!Spawned || !this.IsHashIntervalTick(30)) return;
        ReturnUnused();
    }

    private void ReturnUnused()
    {
        var map = Map;
        var now = Find.TickManager.TicksGame;
        var delay = DigitalStorageUnitMod.Settings.AccessPointReturnDelay;

        if (provided.Count > 0)
        {
            RemoveBuffer.Clear();
            foreach (var kv in provided)
                if (kv.Key.Destroyed || !kv.Key.Spawned || kv.Key.Map != map || kv.Key.Position != Position)
                    RemoveBuffer.Add(kv.Key);
            foreach (var t in RemoveBuffer) provided.Remove(t);
        }

        if (!IsOperational) return;
        var dsu = BoundDsu;
        foreach (var t in ItemsAt(Position, map, Buffer))
        {
            if (provided.TryGetValue(t, out var tick) && now - tick < delay) continue;
            if (IsReserved(t)) continue;
            if (!dsu.CanAcceptFromPort(t)) continue;
            provided.Remove(t);
            dsu.AcceptFromPort(t);
        }
    }

    protected override string ExtraInspectString() =>
        provided.Count > 0 ? (string)"DSU.AccessPoint.Providing".Translate(provided.Count) : null;
}
