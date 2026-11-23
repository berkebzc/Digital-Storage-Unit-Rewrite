using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace DigitalStorageUnit;

/// <summary>
/// Implemented by every DSU storage building that may refuse pawn hauling.
/// Checked by the (cheap) Building_Storage.Accepts prefix.
/// </summary>
public interface IDsuPawnInputBlocker
{
    bool BlocksPawnInput(Thing t);
}

/// <summary>
/// The Digital Storage Unit.
///
/// Design notes (performance):
///  * Stored items stay spawned on the map and are consolidated onto the center cell, so the vanilla
///    storage / hauling / reservation / bill systems keep working without heavy patching.
///  * Contents are tracked in a HashSet + per-def index, so insert/remove/lookup are O(1)
///    (the old implementation rescanned the cell thing list on every insert).
///  * Items are hidden through O(1) grid lookups (see DsuMapComponent), realtime drawables are
///    unregistered while stored.
///  * Power draw is only recomputed when the content changed.
/// </summary>
[StaticConstructorOnStartup]
public class DigitalStorageUnitBuilding : Building_Storage, IRenameable, IDsuPawnInputBlocker
{
    private static readonly Texture2D PawnAccessIcon = ContentFinder<Texture2D>.Get("UI/dsu", false) ?? BaseContent.BadTex;

    // ---------------------------------------------------------------- saved
    private string uniqueName;
    private bool pawnAccess = true;

    // ---------------------------------------------------------------- runtime
    private readonly HashSet<Thing> stored = [];
    private readonly Dictionary<ThingDef, List<Thing>> byDef = new();
    private readonly List<ABasePortDsuBuilding> ports = [];
    private CompPowerTrader power;
    private DsuDropAdjustComp dropComp;
    private bool powerDirty = true;
    private int capacityCacheTick = -99999;
    private int capacityCache;
    private bool isRegistered;
    private bool needsConsolidate;

    /// <summary>Incremented on every content change. UI caches compare against it.</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<Thing> StoredItems => stored;
    public int StoredCount => stored.Count;
    public IEnumerable<ThingDef> StoredDefs => byDef.Keys;
    public IReadOnlyList<ABasePortDsuBuilding> Ports => ports;
    public bool Contains(Thing t) => t != null && stored.Contains(t);
    public bool Powered => power == null || power.PowerOn;

    public bool PawnAccess
    {
        get => pawnAccess;
        set
        {
            if (pawnAccess == value) return;
            pawnAccess = value;
            if (isRegistered)
            {
                DsuGlobal.PawnAccessDisabledCount += value ? -1 : 1;
                Patches.ConditionalPatches.Refresh();
            }
        }
    }

    public int Capacity
    {
        get
        {
            var tick = Find.TickManager?.TicksGame ?? 0;
            if (tick - capacityCacheTick < 60 && capacityCacheTick >= 0) return capacityCache;
            capacityCacheTick = tick;
            var settings = DigitalStorageUnitMod.Settings;
            var cap = settings.BaseDsuStackSize;
            if (Spawned)
            {
                var comp = DsuMapComponent.For(Map);
                foreach (var ext in comp.Extenders)
                    if (ext.IsActive && ext.LinkedDsu == this) cap += settings.ExtenderStackIncrease;
            }
            capacityCache = cap;
            return cap;
        }
    }

    public void InvalidateCapacity() => capacityCacheTick = -99999;

    public float FillRatio => Capacity <= 0 ? 1f : Mathf.Clamp01((float)stored.Count / Capacity);

    public bool CanStoreMore => Spawned && Powered && stored.Count < Capacity;

    // ---------------------------------------------------------------- IRenameable
    public string RenamableLabel
    {
        get => uniqueName ?? LabelCapNoCount;
        set => uniqueName = value;
    }

    public string BaseLabel => LabelCapNoCount;
    public string InspectLabel => LabelCap;

    // ---------------------------------------------------------------- pawn input
    public bool BlocksPawnInput(Thing t)
    {
        if (pawnAccess && CanStoreMore) return false;
        // Never reject things that are already inside - otherwise they'd count as "badly stored".
        return !(t.Spawned && t.Map == Map && this.OccupiedRect().Contains(t.Position));
    }

    // ---------------------------------------------------------------- ports
    public void AddPort(ABasePortDsuBuilding port)
    {
        if (!ports.Contains(port)) ports.Add(port);
    }

    public void RemovePort(ABasePortDsuBuilding port) => ports.Remove(port);

    // ---------------------------------------------------------------- lifecycle
    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        power = GetComp<CompPowerTrader>();
        dropComp = GetComp<DsuDropAdjustComp>();
        DsuMapComponent.For(map).Register(this);
        isRegistered = true;
        if (!pawnAccess)
        {
            DsuGlobal.PawnAccessDisabledCount++;
            Patches.ConditionalPatches.Refresh();
        }

        // Ports that were loaded before us
        foreach (var port in DsuMapComponent.For(map).Ports)
            if (port.BoundDsu == this) AddPort(port);

        Rebuild(consolidate: !respawningAfterLoad);
        powerDirty = true;
    }

    public override void PostMapInit()
    {
        base.PostMapInit();
        // Items may be loaded after the building - rescan and consolidate once the map is ready.
        Rebuild(consolidate: true);
        UpdatePower(force: true);
    }

    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        var map = Map;
        var center = Position;
        var gravship = GravshipUtility.generatingGravship || GravshipPlacementUtility.placingGravship;
        var toSpill = gravship ? null : stored.Where(t => t is { Spawned: true } && t.Map == map).ToList();

        if (isRegistered)
        {
            DsuMapComponent.For(map).Deregister(this);
            if (!pawnAccess)
            {
                DsuGlobal.PawnAccessDisabledCount--;
                Patches.ConditionalPatches.Refresh();
            }
            isRegistered = false;
        }

        // restore drawables before forgetting the items
        foreach (var t in stored)
            if (t.Spawned) RestoreDrawable(t);
        stored.Clear();
        byDef.Clear();
        Version++;

        base.DeSpawn(mode);

        if (toSpill == null || map == null) return;
        // Spread the stack of items so they don't sit on one cell
        foreach (var t in toSpill)
        {
            if (t.Destroyed || !t.Spawned || t.Map != map) continue;
            var pos = t.Position;
            t.DeSpawn();
            if (!GenPlace.TryPlaceThing(t, center, map, ThingPlaceMode.Near))
                GenSpawn.Spawn(t, pos, map);
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref uniqueName, "uniqueName");
        Scribe_Values.Look(ref pawnAccess, "pawnAccess", true);
    }

    protected override void Tick()
    {
        base.Tick();
        if (!Spawned) return;
        if (this.IsHashIntervalTick(60))
        {
            if (needsConsolidate && CanConsolidateNow) Rebuild(consolidate: true);
            UpdatePower(force: false);
        }
        if (this.IsHashIntervalTick(2500)) Validate();
    }

    // ---------------------------------------------------------------- vanilla storage notifications
    public override void Notify_ReceivedThing(Thing newItem)
    {
        base.Notify_ReceivedThing(newItem);
        if (newItem == null || newItem.def.category != ThingCategory.Item) return;
        Register(newItem);
        if (newItem.Position == Position) return;
        if (CanConsolidateNow) Consolidate(newItem);
        else needsConsolidate = true;
    }

    public override void Notify_LostThing(Thing newItem)
    {
        base.Notify_LostThing(newItem);
        Unregister(newItem, restoreDrawable: false);
    }

    private static bool CanConsolidateNow =>
        Current.ProgramState == ProgramState.Playing && !GravshipPlacementUtility.placingGravship;

    // ---------------------------------------------------------------- content bookkeeping
    private void Register(Thing t)
    {
        if (!stored.Add(t)) return;
        if (!byDef.TryGetValue(t.def, out var list))
        {
            list = [];
            byDef[t.def] = list;
        }
        list.Add(t);
        if (t.Spawned && t.def.drawerType is DrawerType.RealtimeOnly or DrawerType.MapMeshAndRealTime)
            t.Map.dynamicDrawManager.DeRegisterDrawable(t);
        Version++;
        powerDirty = true;
    }

    private void Unregister(Thing t, bool restoreDrawable)
    {
        if (t == null || !stored.Remove(t)) return;
        if (byDef.TryGetValue(t.def, out var list))
        {
            list.Remove(t);
            if (list.Count == 0) byDef.Remove(t.def);
        }
        if (restoreDrawable && t.Spawned) RestoreDrawable(t);
        Version++;
        powerDirty = true;
    }

    private static void RestoreDrawable(Thing t)
    {
        if (t.def.drawerType is DrawerType.RealtimeOnly or DrawerType.MapMeshAndRealTime)
            t.Map.dynamicDrawManager.RegisterDrawable(t);
    }

    /// <summary>Move an edge-cell item onto the center cell, merging into existing stacks.</summary>
    private void Consolidate(Thing t)
    {
        if (TryMergeIntoStacks(t)) return; // fully absorbed
        if (!t.Destroyed && t.Spawned && t.Position != Position) Teleport(t, Position);
    }

    /// <summary>Move a spawned thing to another cell of the same map and refresh the map mesh at both ends.</summary>
    internal static void Teleport(Thing t, IntVec3 dest)
    {
        var map = t.Map;
        if (map == null || t.Position == dest) return;
        t.DirtyMapMesh(map);
        t.Position = dest;
        t.DirtyMapMesh(map);
    }

    /// <summary>
    /// Merge into partial stacks on the center cell. Returns true if t was fully absorbed (destroyed).
    /// </summary>
    private bool TryMergeIntoStacks(Thing t)
    {
        if (t.def.stackLimit <= 1 || !byDef.TryGetValue(t.def, out var list)) return false;
        for (var guard = 0; guard < 16; guard++)
        {
            Thing target = null;
            for (var i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s == t || !s.Spawned || s.Position != Position || s.stackCount >= s.def.stackLimit) continue;
                if (!s.CanStackWith(t)) continue;
                target = s;
                break;
            }
            if (target == null) return false;
            if (target.TryAbsorbStack(t, true) || t.Destroyed) return true;
        }
        return false;
    }

    private bool HasMergeTarget(Thing t)
    {
        if (t.def.stackLimit <= 1 || !byDef.TryGetValue(t.def, out var list)) return false;
        for (var i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (s != t && s.stackCount < s.def.stackLimit && s.CanStackWith(t)) return true;
        }
        return false;
    }

    /// <summary>Full rescan of all slot cells. Cheap enough to run on load and every 2500 ticks.</summary>
    public void Rebuild(bool consolidate)
    {
        if (!Spawned) return;
        foreach (var t in stored)
            if (t.Spawned) RestoreDrawable(t);
        stored.Clear();
        byDef.Clear();
        var map = Map;
        var buffer = new List<Thing>();
        foreach (var cell in this.OccupiedRect())
        {
            if (!cell.InBounds(map)) continue;
            var list = cell.GetThingList(map);
            for (var i = 0; i < list.Count; i++)
                if (list[i].def.category == ThingCategory.Item) buffer.Add(list[i]);
        }
        foreach (var t in buffer) Register(t);
        needsConsolidate = false;
        var anyOutside = buffer.Any(t => t.Position != Position);
        if (anyOutside)
        {
            if (consolidate && CanConsolidateNow)
            {
                foreach (var t in buffer)
                    if (!t.Destroyed && t.Spawned && t.Position != Position) Consolidate(t);
            }
            else needsConsolidate = true;
        }
        Version++;
        powerDirty = true;
    }

    /// <summary>Drops stale references (things removed without notification).</summary>
    private void Validate()
    {
        var rect = this.OccupiedRect();
        var dirty = stored.Any(t => t.Destroyed || !t.Spawned || t.Map != Map || !rect.Contains(t.Position));
        if (dirty) Rebuild(consolidate: true);
    }

    // ---------------------------------------------------------------- port API
    /// <summary>Can a port push this (spawned) thing into the storage?</summary>
    public bool CanAcceptFromPort(Thing t)
    {
        if (t == null || !Spawned || !Powered || t.Destroyed) return false;
        if (!settings.AllowedToAccept(t)) return false;
        return stored.Count < Capacity || HasMergeTarget(t);
    }

    /// <summary>Teleports a spawned thing from a port into the DSU. Returns true on (partial) success.</summary>
    public bool AcceptFromPort(Thing t)
    {
        if (!CanAcceptFromPort(t) || !t.Spawned || t.Map != Map) return false;
        var before = t.stackCount;
        if (TryMergeIntoStacks(t)) return true;
        if (t.Destroyed) return true;
        if (stored.Count >= Capacity) return t.stackCount != before;
        Teleport(t, Position);
        Register(t);
        return true;
    }

    /// <summary>Removes the thing from tracking before a port teleports it out.</summary>
    public void ReleaseForMove(Thing t) => Unregister(t, restoreDrawable: true);

    public IReadOnlyList<Thing> StacksOf(ThingDef def) =>
        byDef.TryGetValue(def, out var list) ? list : (IReadOnlyList<Thing>)System.Array.Empty<Thing>();

    public int CountOf(ThingDef def)
    {
        if (!byDef.TryGetValue(def, out var list)) return 0;
        var n = 0;
        for (var i = 0; i < list.Count; i++) n += list[i].stackCount;
        return n;
    }

    /// <summary>Drop a stored thing next to the DSU (used by the Items tab).</summary>
    public bool DropThing(Thing t)
    {
        if (!Contains(t) || !t.Spawned) return false;
        var map = Map;
        var dropCell = dropComp?.DropCell ?? Position + IntVec3.South * 2;
        var pos = t.Position;
        t.DeSpawn(); // -> Notify_LostThing -> Unregister
        if (GenPlace.TryPlaceThing(t, dropCell, map, ThingPlaceMode.Near, null, c => DsuMapComponent.DsuAt(map, c) == null))
            return true;
        GenSpawn.Spawn(t, pos, map);
        return false;
    }

    // ---------------------------------------------------------------- power
    private void UpdatePower(bool force)
    {
        if (power == null) return;
        if (!powerDirty && !force) return;
        powerDirty = false;
        var desired = -(power.Props.PowerConsumption + stored.Count * DigitalStorageUnitMod.Settings.EnergyPerStack);
        if (Mathf.Abs(power.PowerOutput - desired) > 0.01f) power.PowerOutput = desired;
    }

    // ---------------------------------------------------------------- UI
    public override IEnumerable<Gizmo> GetGizmos()
    {
        // NOTE: Building_Storage.GetGizmos would add a "select" gizmo for every stored stack.
        // That part is skipped for DSUs by Patch_BuildingStorage_GetGizmos.
        foreach (var g in base.GetGizmos()) yield return g;

        yield return new Command_Action
        {
            icon = TexButton.Rename,
            defaultLabel = "DSU.Rename".Translate(),
            defaultDesc = "DSU.Rename.Desc".Translate(),
            hotKey = KeyBindingDefOf.Misc1,
            action = () => Find.WindowStack.Add(new Dialog_RenameDsu(this))
        };

        yield return new Command_Toggle
        {
            icon = PawnAccessIcon,
            defaultLabel = "DSU.PawnAccess".Translate(),
            defaultDesc = "DSU.PawnAccess.Desc".Translate(),
            isActive = () => pawnAccess,
            toggleAction = () => PawnAccess = !PawnAccess
        };

        if (Prefs.DevMode)
        {
            yield return new Command_Action
            {
                defaultLabel = "DEV: Rebuild DSU contents",
                action = () => Rebuild(true)
            };
        }
    }

    public override string GetInspectString()
    {
        // Building_Storage.GetInspectString lists every stored item -> way too slow for 700+ stacks.
        // So only the comp parts are used here.
        var sb = new StringBuilder();
        var comps = InspectStringPartsFromComps();
        if (!comps.NullOrEmpty()) sb.AppendLine(comps);
        sb.Append("DSU.StacksCount.Detailed".Translate(stored.Count, Capacity));
        return sb.ToString().TrimEndNewlines();
    }

    public override void DrawGUIOverlay()
    {
        base.DrawGUIOverlay();
        if (Find.CameraDriver.CurrentZoom <= CameraZoomRange.Close)
            GenMapUI.DrawThingLabel(this, RenamableLabel + "\n" + "DSU.StacksCount.Detailed".Translate(stored.Count, Capacity));
    }
}
