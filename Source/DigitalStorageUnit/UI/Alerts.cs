using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DigitalStorageUnit;

public class Alert_DsuPortLostLink : Alert
{
    private readonly List<Thing> culprits = [];

    public Alert_DsuPortLostLink()
    {
        defaultLabel = "DSU.Alert.LostLink.Label".Translate();
        defaultExplanation = "DSU.Alert.LostLink.Desc".Translate();
        defaultPriority = AlertPriority.Medium;
    }

    public override AlertReport GetReport()
    {
        culprits.Clear();
        var maps = Find.Maps;
        for (var i = 0; i < maps.Count; i++)
        {
            var ports = DsuMapComponent.For(maps[i]).Ports;
            for (var j = 0; j < ports.Count; j++)
                if (ports[j].Spawned && ports[j].BoundDsu == null) culprits.Add(ports[j]);
        }
        return AlertReport.CulpritsAre(culprits);
    }
}

public class Alert_DsuOverheat : Alert_Critical
{
    private readonly List<Thing> culprits = [];

    public Alert_DsuOverheat()
    {
        defaultLabel = "DSU.Alert.DsuOverheat".Translate();
        defaultExplanation = "DSU.Alert.DsuOverheat.Desc".Translate();
    }

    public override AlertReport GetReport()
    {
        culprits.Clear();
        if (!DigitalStorageUnitMod.Settings.HeaterEnabled || DsuGlobal.DsuCount == 0) return false;
        foreach (var map in Find.Maps)
        foreach (var dsu in DsuMapComponent.For(map).Dsus)
            if (dsu.GetComp<DsuHeaterComp>()?.IsOverheating ?? false) culprits.Add(dsu);
        return AlertReport.CulpritsAre(culprits);
    }
}

public class Alert_DsuNonHermetic : Alert
{
    private readonly List<Thing> culprits = [];

    public Alert_DsuNonHermetic()
    {
        defaultLabel = "DSU.Alert.DsuNonHermetic".Translate();
        defaultExplanation = "DSU.Alert.DsuNonHermetic.Desc".Translate();
        defaultPriority = AlertPriority.High;
    }

    public override AlertReport GetReport()
    {
        culprits.Clear();
        if (!DigitalStorageUnitMod.Settings.HeaterEnabled || DsuGlobal.DsuCount == 0) return false;
        foreach (var map in Find.Maps)
        foreach (var dsu in DsuMapComponent.For(map).Dsus)
            if (dsu.GetComp<DsuHeaterComp>()?.IsNotHermetic ?? false) culprits.Add(dsu);
        return AlertReport.CulpritsAre(culprits);
    }
}
