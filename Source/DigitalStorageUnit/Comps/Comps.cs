using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DigitalStorageUnit;

// ============================================================================ link lines

/// <summary>Draws lines between a DSU and its ports / extenders while selected.</summary>
public class RenderLinksComp : ThingComp
{
    public override void PostDrawExtraSelectionOverlays()
    {
        base.PostDrawExtraSelectionOverlays();
        var from = parent.TrueCenter();
        switch (parent)
        {
            case DigitalStorageUnitBuilding dsu:
                foreach (var p in dsu.Ports)
                    if (p.Spawned && p.Map == dsu.Map)
                        GenDraw.DrawLineBetween(from, p.TrueCenter(), SimpleColor.Cyan, 0.2f);
                foreach (var e in DsuMapComponent.For(dsu.Map).Extenders)
                    if (e.LinkedDsu == dsu)
                        GenDraw.DrawLineBetween(from, e.TrueCenter(), e.IsActive ? SimpleColor.Green : SimpleColor.Red, 0.2f);
                break;
            case ABasePortDsuBuilding port:
                var target = port.BoundDsu;
                if (target is { Spawned: true } && target.Map == port.Map)
                    GenDraw.DrawLineBetween(from, target.TrueCenter(), port.IsOperational ? SimpleColor.Cyan : SimpleColor.Red, 0.2f);
                break;
            case DataExtenderBuilding ext:
                var linked = ext.LinkedDsu;
                if (linked != null)
                    GenDraw.DrawLineBetween(from, linked.TrueCenter(), ext.IsActive ? SimpleColor.Green : SimpleColor.Red, 0.2f);
                break;
        }
    }
}

// ============================================================================ port work cell

/// <summary>Lets input/output buses work on an adjacent cell. Index: 0 self, 1 N, 2 E, 3 S, 4 W.</summary>
[StaticConstructorOnStartup]
public class PortPositionComp : ThingComp
{
    private static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("UI/OutputDirection", false) ?? BaseContent.BadTex;
    private static readonly IntVec3[] Offsets = [IntVec3.Zero, IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West];
    private static readonly string[] DirKeys = ["DSU.Direction.Self", "DSU.Direction.North", "DSU.Direction.East", "DSU.Direction.South", "DSU.Direction.West"];

    private int positionIdx;

    public IntVec3 WorkCell => parent.Position + Offsets[Mathf.Clamp(positionIdx, 0, Offsets.Length - 1)];

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref positionIdx, "positionIdx");
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        foreach (var g in base.CompGetGizmosExtra()) yield return g;
        yield return new Command_Action
        {
            icon = Icon,
            defaultLabel = "DSU.AdjustOutputDirection".Translate() + ": " + DirKeys[Mathf.Clamp(positionIdx, 0, 4)].Translate(),
            defaultDesc = "DSU.AdjustOutputDirection.Desc".Translate(),
            action = () => positionIdx = (positionIdx + 1) % Offsets.Length
        };
    }

    public override void PostDrawExtraSelectionOverlays()
    {
        base.PostDrawExtraSelectionOverlays();
        if (positionIdx != 0) GenDraw.DrawFieldEdges([WorkCell], Color.yellow);
    }
}

// ============================================================================ DSU drop cell

/// <summary>Where the Items tab drops items: one cell outside the DSU on the chosen side.</summary>
[StaticConstructorOnStartup]
public class DsuDropAdjustComp : ThingComp
{
    private static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("UI/OutputDirection", false) ?? BaseContent.BadTex;
    private static readonly IntVec3[] Dirs = [IntVec3.South, IntVec3.West, IntVec3.North, IntVec3.East];
    private static readonly string[] DirKeys = ["DSU.Direction.South", "DSU.Direction.West", "DSU.Direction.North", "DSU.Direction.East"];

    private int outputSlotIndex;

    public IntVec3 DropCell
    {
        get
        {
            var idx = Mathf.Clamp(outputSlotIndex, 0, 3);
            var half = (idx % 2 == 0 ? parent.def.size.z : parent.def.size.x) / 2 + 1;
            return parent.Position + Dirs[idx] * half;
        }
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref outputSlotIndex, "outputSlotIndex");
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        foreach (var g in base.CompGetGizmosExtra()) yield return g;
        yield return new Command_Action
        {
            icon = Icon,
            defaultLabel = "DSU.DropDirection".Translate() + ": " + DirKeys[Mathf.Clamp(outputSlotIndex, 0, 3)].Translate(),
            defaultDesc = "DSU.DropDirection.Desc".Translate(),
            action = () => outputSlotIndex = (outputSlotIndex + 1) % 4
        };
    }

    public override void PostDrawExtraSelectionOverlays()
    {
        base.PostDrawExtraSelectionOverlays();
        GenDraw.DrawFieldEdges([DropCell], Color.yellow);
    }
}

// ============================================================================ heater (hard mode)

public class DsuHeaterCompProperties : CompProperties
{
    public float HeatPerSecond = 30f;
    public float MaxHeat = 1000f;
    public float DamageAtHeat = 200f;
    public float DamageMultiplier = 0.1f;

    public DsuHeaterCompProperties()
    {
        compClass = typeof(DsuHeaterComp);
    }
}

public class DsuHeaterComp : ThingComp
{
    private const int Interval = 250;
    private const float SecondsPerInterval = Interval / 60f;

    public DsuHeaterCompProperties Props => (DsuHeaterCompProperties)props;

    private static bool Enabled => DigitalStorageUnitMod.Settings.HeaterEnabled;

    public float RoomTemperature
    {
        get
        {
            var room = DsuRoomUtility.RoomOf(parent);
            return room?.Temperature ?? parent.AmbientTemperature;
        }
    }

    public bool IsOverheating => Enabled && parent.Spawned && RoomTemperature >= Props.DamageAtHeat * 0.8f;

    public bool IsNotHermetic => Enabled && parent.Spawned && !DsuRoomUtility.IsHermetic(DsuRoomUtility.RoomOf(parent));

    public override void CompTick()
    {
        base.CompTick();
        if (!Enabled || !parent.IsHashIntervalTick(Interval)) return;
        if (parent is not DigitalStorageUnitBuilding { Spawned: true } dsu || !dsu.Powered) return;

        var temp = RoomTemperature;
        if (temp < Props.MaxHeat)
        {
            var load = 0.25f + 0.75f * dsu.FillRatio;
            GenTemperature.PushHeat(parent, Props.HeatPerSecond * load * SecondsPerInterval);
        }

        if (temp < Props.DamageAtHeat) return;
        var amount = Mathf.Max(1f, (temp - Props.DamageAtHeat) * Props.DamageMultiplier);
        parent.TakeDamage(new DamageInfo(DamageDefOf.Burn, amount));
        // damage a few random stored stacks
        var victims = new List<Thing>(3);
        foreach (var t in dsu.StoredItems)
        {
            if (victims.Count >= 3) break;
            if (t.def.useHitPoints && Rand.Chance(0.5f)) victims.Add(t);
        }
        foreach (var t in victims)
            if (!t.Destroyed) t.TakeDamage(new DamageInfo(DamageDefOf.Burn, amount));
    }

    public override string CompInspectStringExtra()
    {
        if (!Enabled) return null;
        return "DSU.Heater.Inspect".Translate(RoomTemperature.ToStringTemperature("F0"), Props.DamageAtHeat.ToStringTemperature("F0"));
    }
}
