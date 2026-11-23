using HarmonyLib;
using UnityEngine;
using Verse;

namespace DigitalStorageUnit;

/// <summary>
/// Mod entry point: settings + Harmony bootstrap.
/// </summary>
public class DigitalStorageUnitMod : Mod
{
    public static DsuSettings Settings { get; private set; }
    public static Harmony Harmony { get; private set; }
    public const string HarmonyId = "dsu.rewrite.digitalstorageunit";

    public DigitalStorageUnitMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<DsuSettings>();
        Harmony = new Harmony(HarmonyId);
        try
        {
            Harmony.PatchAll(typeof(DigitalStorageUnitMod).Assembly);
            Patches.ConditionalPatches.Init(Harmony);
            Log.Message($"[DSU] Digital Storage Unit {content?.ModMetaData?.ModVersion} loaded (Harmony patches OK)");
        }
        catch (System.Exception e)
        {
            Log.Error("[DSU] Harmony patching failed: " + e);
        }
    }

    public override string SettingsCategory() => "DSU".Translate();

    public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);
}
