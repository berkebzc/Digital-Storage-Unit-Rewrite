using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DigitalStorageUnit;

/// <summary>
/// Output Bus: pushes items allowed by its own storage filter out of the DSU onto its work cell
/// (its own cell or an adjacent one, e.g. a hopper). Supports min/max amounts.
/// Pawns never haul into it.
/// </summary>
[StaticConstructorOnStartup]
public class OutputPortDsuBuilding : ABasePortDsuBuilding
{
    private static readonly Texture2D MinMaxIcon = ContentFinder<Texture2D>.Get("UI/Commands/SetTargetFuelLevel", false) ?? BaseContent.BadTex;
    private static readonly Texture2D ForbidOnIcon = ContentFinder<Texture2D>.Get("UI/Designators/ForbidOn", false) ?? BaseContent.BadTex;
    private static readonly Texture2D ForbidOffIcon = ContentFinder<Texture2D>.Get("UI/Designators/ForbidOff", false) ?? BaseContent.BadTex;

    private OutputSettings outputSettings = new();
    private bool forbidOnPlacing;

    public OutputSettings OutputSettings => outputSettings ??= new OutputSettings();

    protected override bool SharesDsuSettings => false;

    protected override Color PortColor(ModExtensionPortColor ext) => ext.outColor;

    public override bool BlocksPawnInput(Thing t) => !(t.Spawned && t.Map == Map && t.Position == Position);

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Deep.Look(ref outputSettings, "outputSettings");
        Scribe_Values.Look(ref forbidOnPlacing, "forbidOnPlacing");
        outputSettings ??= new OutputSettings();
    }

    protected override void Tick()
    {
        base.Tick();
        if (!Spawned || !this.IsHashIntervalTick(30)) return;
        if (GravshipPlacementUtility.placingGravship || GravshipUtility.generatingGravship) return;
        if (IsOperational) DoOutput();
    }

    // ---------------------------------------------------------------- logic
    private bool CellAccepts(IntVec3 cell, Thing t)
    {
        if (cell == Position) return settings.AllowedToAccept(t);
        var slotGroup = cell.GetSlotGroup(Map);
        // Respect the target storage settings; loose ground is fine too
        return slotGroup == null || slotGroup.parent.Accepts(t);
    }

    private bool CellUsable(IntVec3 cell)
    {
        var map = Map;
        if (!cell.InBounds(map)) return false;
        if (DsuMapComponent.DsuAt(map, cell) != null) return false;
        var edifice = cell.GetEdifice(map);
        if (edifice != null && edifice != this && edifice.def.passability == Traversability.Impassable && cell.GetSlotGroup(map) == null)
            return false;
        return true;
    }

    private void DoOutput()
    {
        var dsu = BoundDsu;
        var map = Map;
        var cell = WorkCell;
        if (!CellUsable(cell)) return;
        var os = OutputSettings;
        var current = cell.GetFirstItem(map);

        if (current != null)
        {
            // On our own cell: send back what is not wanted anymore
            if (cell == Position)
            {
                if ((!settings.AllowedToAccept(current) || !os.SatisfiesMin(current.stackCount)) && !IsReserved(current))
                {
                    current.SetForbidden(false, false);
                    dsu.AcceptFromPort(current);
                    return;
                }
                var excess = -os.CountNeededToReachMax(current.stackCount, current.def.stackLimit);
                if (excess > 0 && excess < current.stackCount && !IsReserved(current))
                {
                    var back = current.SplitOff(excess);
                    GenSpawn.Spawn(back, dsu.Position, map); // DSU registers it through Notify_ReceivedThing
                    return;
                }
            }

            // Top up
            var need = os.CountNeededToReachMax(current.stackCount, current.def.stackLimit);
            if (need <= 0 || !CellAccepts(cell, current)) return;
            var stacks = dsu.StacksOf(current.def);
            for (var i = stacks.Count - 1; i >= 0 && need > 0; i--)
            {
                var s = stacks[i];
                if (s == current || !s.Spawned || !s.CanStackWith(current) || IsReserved(s)) continue;
                var n = Math.Min(need, s.stackCount);
                var piece = s.SplitOff(n); // whole stack -> despawns -> DSU unregisters it
                if (!current.TryAbsorbStack(piece, true))
                {
                    if (!piece.Destroyed && !piece.Spawned) GenSpawn.Spawn(piece, dsu.Position, map);
                    return;
                }
                need -= n;
                stacks = dsu.StacksOf(current.def);
                if (i > stacks.Count) i = stacks.Count;
            }
            if (forbidOnPlacing) current.SetForbidden(true, false);
            return;
        }

        // Empty cell: find something to output
        foreach (var def in dsu.StoredDefs)
        {
            if (!settings.AllowedToAccept(def)) continue;
            var stacks = dsu.StacksOf(def);
            for (var i = 0; i < stacks.Count; i++)
            {
                var t = stacks[i];
                if (!t.Spawned || IsReserved(t) || !settings.AllowedToAccept(t) || !CellAccepts(cell, t)) continue;
                if (os.UseMin && !os.SatisfiesMin(StackableTotal(stacks, t))) continue;
                var n = Math.Min(t.stackCount, os.CountNeededToReachMax(0, t.def.stackLimit));
                if (n <= 0) continue;
                var piece = t.SplitOff(n);
                var spawned = GenSpawn.Spawn(piece, cell, map);
                if (forbidOnPlacing) spawned.SetForbidden(true, false);
                return; // one stack per pass; further top-ups next pass
            }
        }
    }

    private static int StackableTotal(IReadOnlyList<Thing> stacks, Thing like)
    {
        var total = 0;
        for (var i = 0; i < stacks.Count; i++)
            if (stacks[i] == like || stacks[i].CanStackWith(like)) total += stacks[i].stackCount;
        return total;
    }

    // ---------------------------------------------------------------- UI
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (var g in base.GetGizmos()) yield return g;

        yield return new Command_Action
        {
            icon = MinMaxIcon,
            defaultLabel = "DSU.Output.Settings".Translate(),
            defaultDesc = "DSU.MinMaxAmounts".Translate(),
            action = () => Find.WindowStack.Add(new Dialog_OutputMinMax(OutputSettings, () =>
            {
                foreach (var obj in Find.Selector.SelectedObjects)
                    if (obj is OutputPortDsuBuilding other && other != this)
                        other.OutputSettings.CopyFrom(OutputSettings);
            }))
        };

        yield return new Command_Toggle
        {
            icon = forbidOnPlacing ? ForbidOnIcon : ForbidOffIcon,
            defaultLabel = "DSU.ForbidOnPlacing".Translate(),
            defaultDesc = "DSU.ForbidOnPlacing.Desc".Translate(),
            isActive = () => forbidOnPlacing,
            toggleAction = () => forbidOnPlacing = !forbidOnPlacing
        };
    }

    protected override string ExtraInspectString()
    {
        var os = OutputSettings;
        if (!os.UseMin && !os.UseMax) return null;
        return "DSU.Limit.GetInspectString".Translate(os.UseMin ? os.Min.ToString() : "-", os.UseMax ? os.Max.ToString() : "-");
    }
}
