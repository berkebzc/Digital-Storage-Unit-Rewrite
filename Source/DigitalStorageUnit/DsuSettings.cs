using UnityEngine;
using Verse;

namespace DigitalStorageUnit;

public class DsuSettings : ModSettings
{
    // --- storage ---
    public int BaseDsuStackSize = 768;
    public int ExtenderStackIncrease = 128;
    public float EnergyPerStack = 10f;

    // --- behaviour ---
    public bool AutoBoundDsu = true;
    public float DsuPathingMultiplier = 1.0f;
    public int AccessPointReturnDelay = 300;
    public bool CleanCellItemList = true;

    // --- hard mode ---
    public bool HeaterEnabled;

    private string bufBase, bufExt, bufDelay;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref BaseDsuStackSize, "BaseDsuStackSize", 768);
        Scribe_Values.Look(ref ExtenderStackIncrease, "ExtenderStackIncrease", 128);
        Scribe_Values.Look(ref EnergyPerStack, "EnergyPerStack", 10f);
        Scribe_Values.Look(ref AutoBoundDsu, "AutoBoundDsu", true);
        Scribe_Values.Look(ref DsuPathingMultiplier, "DsuPathingMultiplier", 1.0f);
        Scribe_Values.Look(ref AccessPointReturnDelay, "AccessPointReturnDelay", 300);
        Scribe_Values.Look(ref CleanCellItemList, "CleanCellItemList", true);
        Scribe_Values.Look(ref HeaterEnabled, "HeaterEnabled");
    }

    public void DoWindowContents(Rect inRect)
    {
        var l = new Listing_Standard();
        l.Begin(inRect);

        l.Label("DSU.Config.BaseDsuStackSize".Translate(BaseDsuStackSize), -1f, (string)"DSU.Config.BaseDsuStackSize.Desc".Translate());
        l.TextFieldNumericLabeled("    ", ref BaseDsuStackSize, ref bufBase, 1, 100000);
        l.Label("DSU.Config.ExtenderStackIncrease".Translate(ExtenderStackIncrease), -1f, (string)"DSU.Config.ExtenderStackIncrease.Desc".Translate());
        l.TextFieldNumericLabeled("    ", ref ExtenderStackIncrease, ref bufExt, 0, 100000);
        l.GapLine();

        EnergyPerStack = Mathf.Round(l.SliderLabeled("DSU.Config.EnergyPerStack".Translate(EnergyPerStack.ToString("0")), EnergyPerStack, 0f, 100f, 0.6f, "DSU.Config.EnergyPerStack.Desc".Translate()));
        DsuPathingMultiplier = Mathf.Round(l.SliderLabeled("DSU.Config.DsuPathingMultiplier".Translate(DsuPathingMultiplier.ToString("0.0")), DsuPathingMultiplier, 0.5f, 5f, 0.6f, "DSU.Config.DsuPathingMultiplier.Desc".Translate()) * 10f) / 10f;
        l.Label("DSU.Config.AccessPointReturnDelay".Translate(AccessPointReturnDelay), -1f, (string)"DSU.Config.AccessPointReturnDelay.Desc".Translate());
        l.TextFieldNumericLabeled("    ", ref AccessPointReturnDelay, ref bufDelay, 30, 60000);
        l.GapLine();

        l.CheckboxLabeled("DSU.Config.AutoBoundDsu".Translate(), ref AutoBoundDsu, "DSU.Config.AutoBoundDsu.Desc".Translate(), 0f, 1f);
        l.CheckboxLabeled("DSU.Config.CleanCellItemList".Translate(), ref CleanCellItemList, "DSU.Config.CleanCellItemList.Desc".Translate(), 0f, 1f);
        l.GapLine();
        l.CheckboxLabeled("DSU.Config.HeaterEnabled".Translate(), ref HeaterEnabled, "DSU.Config.HeaterEnabled.Desc".Translate(), 0f, 1f);

        l.End();
    }
}
