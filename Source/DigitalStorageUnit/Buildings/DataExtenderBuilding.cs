using RimWorld;
using Verse;

namespace DigitalStorageUnit;

/// <summary>
/// Data Extender: adds extra stack capacity to the DSU standing in the same room while powered.
/// </summary>
public class DataExtenderBuilding : Building
{
    private CompPowerTrader power;
    private DigitalStorageUnitBuilding linked;
    private int lastLinkCheck = -99999;

    public bool IsActive => Spawned && (power == null || power.PowerOn);

    public DigitalStorageUnitBuilding LinkedDsu
    {
        get
        {
            var tick = Find.TickManager.TicksGame;
            if (tick - lastLinkCheck >= 250 || linked is { Spawned: false })
            {
                lastLinkCheck = tick;
                var newLink = Spawned ? DsuRoomUtility.FindDsuInSameRoom(this) : null;
                if (newLink != linked)
                {
                    linked?.InvalidateCapacity();
                    linked = newLink;
                    linked?.InvalidateCapacity();
                }
            }
            return linked;
        }
    }

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        power = GetComp<CompPowerTrader>();
        DsuMapComponent.For(map).Register(this);
        lastLinkCheck = -99999;
    }

    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        var map = Map;
        if (map != null) DsuMapComponent.For(map).Deregister(this);
        linked?.InvalidateCapacity();
        linked = null;
        base.DeSpawn(mode);
    }

    protected override void ReceiveCompSignal(string signal)
    {
        base.ReceiveCompSignal(signal);
        linked?.InvalidateCapacity();
    }

    public override string GetInspectString()
    {
        var s = base.GetInspectString();
        var dsu = LinkedDsu;
        var line = "DSU".Translate() + ": " + (dsu?.RenamableLabel ?? "NoneBrackets".Translate().ToString());
        return s.NullOrEmpty() ? line : s + "\n" + line;
    }
}
