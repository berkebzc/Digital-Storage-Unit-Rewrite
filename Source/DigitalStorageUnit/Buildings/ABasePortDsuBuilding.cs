using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace DigitalStorageUnit;

/// <summary>
/// Common base for Input Bus, Output Bus and Access Point.
/// </summary>
[StaticConstructorOnStartup]
public abstract class ABasePortDsuBuilding : Building_Storage, IRenameable, IDsuPawnInputBlocker
{
    protected static readonly Texture2D LinkIcon = ContentFinder<Texture2D>.Get("Storage/CargoPlatform", false) ?? BaseContent.BadTex;

    // saved (same keys as the original mod -> old saves keep their links)
    private Building boundStorageUnit;
    private string uniqueName;

    // runtime
    protected CompPowerTrader power;
    protected PortPositionComp positionComp;
    private StorageSettings ownSettings;
    private Graphic cachedGraphic;

    public DigitalStorageUnitBuilding BoundDsu
    {
        get
        {
            var dsu = boundStorageUnit as DigitalStorageUnitBuilding;
            return dsu is { Destroyed: false } ? dsu : null;
        }
    }

    public bool Powered => power == null || power.PowerOn;

    /// <summary>Port + DSU both powered, spawned and on the same map.</summary>
    public bool IsOperational
    {
        get
        {
            if (!Spawned || !Powered) return false;
            var dsu = BoundDsu;
            return dsu is { Spawned: true } && dsu.Map == Map && dsu.Powered;
        }
    }

    public IntVec3 WorkCell => positionComp?.WorkCell ?? Position;

    /// <summary>Input bus and access point share the DSU storage settings.</summary>
    protected virtual bool SharesDsuSettings => true;

    protected abstract Color PortColor(ModExtensionPortColor ext);

    // ---------------------------------------------------------------- IRenameable
    public string RenamableLabel
    {
        get => uniqueName ?? LabelCapNoCount;
        set => uniqueName = value;
    }

    public string BaseLabel => LabelCapNoCount;
    public string InspectLabel => LabelCap;

    public abstract bool BlocksPawnInput(Thing t);

    // ---------------------------------------------------------------- graphic
    public override Graphic Graphic
    {
        get
        {
            if (cachedGraphic != null) return cachedGraphic;
            var baseGraphic = base.Graphic;
            var ext = def.GetModExtension<ModExtensionPortColor>();
            cachedGraphic = ext == null ? baseGraphic : baseGraphic.GetColoredVersion(baseGraphic.Shader, PortColor(ext), Color.white);
            return cachedGraphic;
        }
    }

    // ---------------------------------------------------------------- binding
    public void Bind(DigitalStorageUnitBuilding dsu)
    {
        BoundDsu?.RemovePort(this);
        boundStorageUnit = dsu;
        dsu?.AddPort(this);
        RefreshSettingsLink();
        OnBindingChanged();
    }

    /// <summary>Called by the map component when the bound DSU despawns.</summary>
    public void Notify_DsuLost()
    {
        boundStorageUnit = null;
        RefreshSettingsLink();
        OnBindingChanged();
    }

    protected virtual void OnBindingChanged()
    {
    }

    protected void RefreshSettingsLink()
    {
        if (SharesDsuSettings && BoundDsu != null)
        {
            settings = BoundDsu.settings;
            return;
        }
        if (ownSettings == null)
        {
            if (settings != null && settings.owner == this)
            {
                ownSettings = settings;
            }
            else
            {
                ownSettings = new StorageSettings(this);
                if (def.building?.defaultStorageSettings != null)
                    ownSettings.CopyFrom(def.building.defaultStorageSettings);
            }
        }
        settings = ownSettings;
    }

    // ---------------------------------------------------------------- lifecycle
    public override void PostMake()
    {
        base.PostMake();
        ownSettings = settings;
    }

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        if (settings != null && settings.owner == this) ownSettings = settings;
        base.SpawnSetup(map, respawningAfterLoad);
        power = GetComp<CompPowerTrader>();
        positionComp = GetComp<PortPositionComp>();
        var comp = DsuMapComponent.For(map);
        comp.Register(this);

        var dsu = BoundDsu;
        if (dsu != null && dsu.Spawned && dsu.Map != map)
        {
            boundStorageUnit = null; // moved to another map (gravship / caravan)
            dsu = null;
        }
        if (dsu != null)
        {
            dsu.AddPort(this);
        }
        else if (!respawningAfterLoad && DigitalStorageUnitMod.Settings.AutoBoundDsu && comp.Dsus.Count == 1)
        {
            Bind(comp.Dsus[0]);
            Messages.Message("DSU.Message.AutoBoundTo".Translate(comp.Dsus[0].RenamableLabel), this, MessageTypeDefOf.SilentInput, false);
        }
        RefreshSettingsLink();
    }

    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        var map = Map;
        BoundDsu?.RemovePort(this);
        if (map != null) DsuMapComponent.For(map).Deregister(this);
        // Never save the DSU's settings object as our own
        settings = ownSettings ?? new StorageSettings(this);
        base.DeSpawn(mode);
    }

    public override void ExposeData()
    {
        if (Scribe.mode == LoadSaveMode.Saving && ownSettings != null) settings = ownSettings;
        base.ExposeData();
        Scribe_References.Look(ref boundStorageUnit, "boundStorageUnit");
        Scribe_Values.Look(ref uniqueName, "uniqueName");
        if (Scribe.mode == LoadSaveMode.Saving && Spawned) RefreshSettingsLink();
        if (Scribe.mode == LoadSaveMode.LoadingVars) ownSettings = settings;
    }

    protected override void ReceiveCompSignal(string signal)
    {
        base.ReceiveCompSignal(signal);
        if (signal == CompPowerTrader.PowerTurnedOnSignal) OnBindingChanged();
    }

    // ---------------------------------------------------------------- UI
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (var g in base.GetGizmos()) yield return g;

        yield return new Command_Action
        {
            icon = LinkIcon,
            defaultLabel = "DSU".Translate() + ": " + (BoundDsu?.RenamableLabel ?? "NoneBrackets".Translate().ToString()),
            defaultDesc = "DSU.BindPort.Desc".Translate(),
            action = () =>
            {
                var options = DsuMapComponent.For(Map).Dsus
                    .Select(d => new FloatMenuOption(d.RenamableLabel, () =>
                    {
                        foreach (var p in SelectedPorts()) p.Bind(d);
                    }))
                    .ToList();
                if (options.Count == 0) options.Add(new FloatMenuOption("NoneBrackets".Translate(), null));
                Find.WindowStack.Add(new FloatMenu(options));
            }
        };

        yield return new Command_Action
        {
            icon = TexButton.Rename,
            defaultLabel = "DSU.Rename".Translate(),
            defaultDesc = "DSU.Rename.Desc".Translate(),
            hotKey = KeyBindingDefOf.Misc1,
            action = () => Find.WindowStack.Add(new Dialog_RenameDsu(this))
        };
    }

    private IEnumerable<ABasePortDsuBuilding> SelectedPorts()
    {
        var list = Find.Selector.SelectedObjects.OfType<ABasePortDsuBuilding>().Where(p => p.Map == Map).ToList();
        if (!list.Contains(this)) list.Add(this);
        return list;
    }

    public override string GetInspectString()
    {
        // Skip Building_Storage's item listing
        var sb = new StringBuilder();
        var comps = InspectStringPartsFromComps();
        if (!comps.NullOrEmpty()) sb.AppendLine(comps);
        sb.Append("DSU".Translate() + ": " + (BoundDsu?.RenamableLabel ?? "NoneBrackets".Translate().ToString()));
        var extra = ExtraInspectString();
        if (!extra.NullOrEmpty())
        {
            sb.AppendLine();
            sb.Append(extra);
        }
        return sb.ToString().TrimEndNewlines();
    }

    protected virtual string ExtraInspectString() => null;

    public override void DrawGUIOverlay()
    {
        base.DrawGUIOverlay();
        if (uniqueName != null && Find.CameraDriver.CurrentZoom <= CameraZoomRange.Close)
            GenMapUI.DrawThingLabel(this, uniqueName);
    }

    // ---------------------------------------------------------------- helpers
    protected bool IsReserved(Thing t) => Map.reservationManager.IsReservedByAnyoneOf(t, Faction.OfPlayer);

    /// <summary>Copy of the item list at a cell, items only.</summary>
    protected static List<Thing> ItemsAt(IntVec3 cell, Map map, List<Thing> buffer)
    {
        buffer.Clear();
        if (!cell.InBounds(map)) return buffer;
        var list = cell.GetThingList(map);
        for (var i = 0; i < list.Count; i++)
            if (list[i].def.category == ThingCategory.Item) buffer.Add(list[i]);
        return buffer;
    }
}
