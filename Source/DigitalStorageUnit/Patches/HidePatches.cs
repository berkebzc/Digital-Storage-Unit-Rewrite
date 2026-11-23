using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DigitalStorageUnit.Patches;

// All prefixes below start with the cheapest possible checks:
//   global DSU counter -> def.category -> O(1) grid read.

/// <summary>Don't print stored items into the map mesh.</summary>
[HarmonyPatch(typeof(Thing), nameof(Thing.Print))]
internal static class Patch_Thing_Print
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Thing __instance) =>
        DsuGlobal.DsuCount == 0 || __instance.def.category != ThingCategory.Item || !DsuMapComponent.IsInDsuCell(__instance);
}

[HarmonyPatch(typeof(MinifiedThing), nameof(MinifiedThing.Print))]
internal static class Patch_MinifiedThing_Print
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Thing __instance) =>
        DsuGlobal.DsuCount == 0 || !DsuMapComponent.IsInDsuCell(__instance);
}

/// <summary>Don't draw stack count labels of stored items (called every frame!).</summary>
[HarmonyPatch(typeof(Thing), nameof(Thing.DrawGUIOverlay))]
internal static class Patch_Thing_DrawGUIOverlay
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Thing __instance) =>
        DsuGlobal.DsuCount == 0 || __instance.def.category != ThingCategory.Item || !DsuMapComponent.IsInDsuCell(__instance);
}

[HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.DrawGUIOverlay))]
internal static class Patch_ThingWithComps_DrawGUIOverlay
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Thing __instance) =>
        DsuGlobal.DsuCount == 0 || __instance.def.category != ThingCategory.Item || !DsuMapComponent.IsInDsuCell(__instance);
}

/// <summary>Clicking a DSU selects the DSU, not one of its hundreds of items.</summary>
[HarmonyPatch(typeof(ThingSelectionUtility), nameof(ThingSelectionUtility.SelectableByMapClick))]
internal static class Patch_SelectableByMapClick
{
    private static void Postfix(Thing t, ref bool __result)
    {
        if (__result && DsuGlobal.DsuCount != 0 && t.def.category == ThingCategory.Item && DsuMapComponent.IsInDsuCell(t))
            __result = false;
    }
}

/// <summary>Right clicking a filled DSU would build float menu options for every stored stack.</summary>
[HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
internal static class Patch_FloatMenuMakerMap_GetOptions
{
    private static bool Prefix(Vector3 clickPos, ref List<FloatMenuOption> __result)
    {
        if (DsuGlobal.DsuCount == 0) return true;
        var dsu = DsuMapComponent.DsuAt(Find.CurrentMap, clickPos.ToIntVec3());
        if (dsu == null || dsu.StoredCount == 0) return true;
        __result = [];
        return false;
    }
}

/// <summary>The bottom-left mouseover readout would list every stored stack each frame.</summary>
[HarmonyPatch(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI))]
internal static class Patch_MouseoverReadout
{
    private static bool Prefix()
    {
        if (DsuGlobal.DsuCount == 0 || !DigitalStorageUnitMod.Settings.CleanCellItemList) return true;
        var dsu = DsuMapComponent.DsuAt(Find.CurrentMap, UI.MouseCell());
        return dsu == null || dsu.StoredCount == 0;
    }
}

/// <summary>Items inside a DSU must not be save-compressed (the compressed grid holds one thing per cell).</summary>
[HarmonyPatch(typeof(CompressibilityDeciderUtility), nameof(CompressibilityDeciderUtility.IsSaveCompressible))]
internal static class Patch_IsSaveCompressible
{
    private static void Postfix(Thing t, ref bool __result)
    {
        if (__result && t.def.category == ThingCategory.Item && DsuMapComponent.IsInDsuCell(t)) __result = false;
    }
}

/// <summary>No "merge stacks" haul jobs inside a DSU - it merges stacks itself.</summary>
[HarmonyPatch(typeof(ListerMergeables), "ShouldBeMergeable")]
internal static class Patch_ShouldBeMergeable
{
    private static void Postfix(Thing t, ref bool __result)
    {
        if (__result && DsuMapComponent.IsInDsuCell(t)) __result = false;
    }
}
