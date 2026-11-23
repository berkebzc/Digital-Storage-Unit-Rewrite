using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DigitalStorageUnit.Patches;

/// <summary>
/// Building_Storage.Accepts is final in vanilla, so pawn-input blocking (DSU full / unpowered / pawn access off,
/// output bus, ...) has to go through a prefix. The type check is the only cost for every other storage.
/// </summary>
[HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.Accepts))]
internal static class Patch_BuildingStorage_Accepts
{
    private static bool Prefix(Building_Storage __instance, Thing t, ref bool __result)
    {
        if (__instance is IDsuPawnInputBlocker blocker && t != null && blocker.BlocksPawnInput(t))
        {
            __result = false;
            return false;
        }
        return true;
    }
}

/// <summary>
/// Building_Storage.GetGizmos adds a "select stored thing" gizmo for every stored stack when a single
/// storage is selected. For a DSU with hundreds of stacks this kills the UI, so that block is skipped.
/// Inserted check:  if (IsDsu(this)) goto endOfThatBlock;
/// </summary>
[HarmonyPatch]
internal static class Patch_BuildingStorage_GetGizmos
{
    private static MethodBase target;

    private static MethodBase FindTarget()
    {
        if (target != null) return target;
        var nested = typeof(Building_Storage).GetNestedTypes(AccessTools.all)
            .FirstOrDefault(t => t.Name.Contains("<GetGizmos>"));
        target = nested == null ? null : AccessTools.Method(nested, "MoveNext");
        return target;
    }

    private static bool Prepare()
    {
        var ok = FindTarget() != null;
        if (!ok) Log.Warning("[DSU] Building_Storage.GetGizmos iterator not found - stored item gizmos will not be hidden.");
        return ok;
    }

    private static MethodBase TargetMethod() => FindTarget();

    public static bool IsDsu(Building_Storage b) => b is DigitalStorageUnitBuilding;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var codes = instructions.ToList();
        var thisField = AccessTools.GetDeclaredFields(original.DeclaringType)
            .FirstOrDefault(f => f.Name.Contains("<>4__this"));
        var getNumSelected = AccessTools.PropertyGetter(typeof(Selector), nameof(Selector.NumSelected));

        var numIdx = codes.FindIndex(c => (c.opcode == OpCodes.Callvirt || c.opcode == OpCodes.Call) && Equals(c.operand, getNumSelected));
        if (thisField == null || numIdx < 0) return Fail(codes, "NumSelected call not found");

        var branchIdx = -1;
        for (var i = numIdx + 1; i < codes.Count && i < numIdx + 6; i++)
            if (codes[i].opcode == OpCodes.Bne_Un || codes[i].opcode == OpCodes.Bne_Un_S)
            {
                branchIdx = i;
                break;
            }
        if (branchIdx < 0) return Fail(codes, "branch not found");
        var skipLabel = codes[branchIdx].operand;

        var insertIdx = -1;
        for (var i = branchIdx + 1; i < codes.Count; i++)
            if (codes[i].opcode == OpCodes.Ldarg_0)
            {
                insertIdx = i;
                break;
            }
        if (insertIdx < 0) return Fail(codes, "insert point not found");

        var first = new CodeInstruction(OpCodes.Ldarg_0);
        first.labels.AddRange(codes[insertIdx].labels);
        codes[insertIdx].labels.Clear();
        codes.InsertRange(insertIdx,
        [
            first,
            new CodeInstruction(OpCodes.Ldfld, thisField),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_BuildingStorage_GetGizmos), nameof(IsDsu))),
            new CodeInstruction(OpCodes.Brtrue, skipLabel)
        ]);
        return codes;
    }

    private static List<CodeInstruction> Fail(List<CodeInstruction> codes, string why)
    {
        Log.Warning("[DSU] Patch_BuildingStorage_GetGizmos: " + why + " - patch skipped.");
        return codes;
    }
}

/// <summary>Powered DSUs act as trade beacons for their own contents (orbital trade).</summary>
[HarmonyPatch(typeof(TradeUtility), nameof(TradeUtility.AllLaunchableThingsForTrade))]
internal static class Patch_TradeUtility_AllLaunchableThingsForTrade
{
    private static void Postfix(Map map, ref IEnumerable<Thing> __result)
    {
        if (DsuGlobal.DsuCount == 0 || map == null) return;
        var dsus = DsuMapComponent.For(map).Dsus;
        if (dsus.Count == 0) return;
        var set = new HashSet<Thing>(__result);
        foreach (var dsu in dsus)
            if (dsu.Powered)
                foreach (var t in dsu.StoredItems)
                    set.Add(t);
        __result = set;
    }
}

/// <summary>Keep the stored items stacked on the DSU while a gravship lands.</summary>
[HarmonyPatch(typeof(GravshipPlacementUtility), "SpawnNonPawnThings")]
internal static class Patch_GravshipPlacementUtility_SpawnNonPawnThings
{
    private static readonly List<DigitalStorageUnitBuilding> Dsus = [];
    private static readonly Dictionary<ThingDef, int> OldMax = new();

    private static void Prefix(List<Thing> gravshipThings)
    {
        Dsus.Clear();
        OldMax.Clear();
        if (gravshipThings == null) return;
        foreach (var t in gravshipThings)
        {
            if (t is not DigitalStorageUnitBuilding dsu) continue;
            Dsus.Add(dsu);
            var b = dsu.def.building;
            if (b != null && !OldMax.ContainsKey(dsu.def))
            {
                OldMax[dsu.def] = b.maxItemsInCell;
                b.maxItemsInCell = int.MaxValue;
            }
        }
    }

    private static void Postfix()
    {
        foreach (var kv in OldMax) kv.Key.building.maxItemsInCell = kv.Value;
        OldMax.Clear();
        foreach (var dsu in Dsus)
            if (dsu.Spawned) dsu.Rebuild(false);
        Dsus.Clear();
    }
}
