# Digital Storage Unit — RimWorld 1.7 rewrite

Ground-up rewrite of *Digital Storage Unit* (Relvl / Johnson1893) for RimWorld 1.7 (.NET 10 / Harmony 3),
using Project RimFactory Revived 1.7 as reference.

## Build
Requires the .NET 10 SDK. Run `build.bat` (or `dotnet build -c Release` in `Source/DigitalStorageUnit`).
Output: `1.7/Assemblies/DigitalStorageUnit.dll`.

## Save compatibility
defNames, thing class names (`DigitalStorageUnit.*`) and the main save keys (`boundStorageUnit`, `uniqueName`,
`pawnAccess`, `outputSettings`, `positionIdx`, `outputSlotIndex`) are the same as the original mod.

## Performance design
* Stored items stay spawned and are consolidated on the DSU center cell (vanilla hauling/bills keep working).
* Content tracking: `HashSet<Thing>` + `Dictionary<ThingDef, List<Thing>>` → O(1) add/remove/lookup, O(stacks of def) merge.
* Per-map cell grid (`DsuMapComponent`) → O(1) "is this cell a DSU" for all hiding patches; global counter early-outs.
* `ForbidUtility.IsForbidden` and `Pawn_JobTracker.StartJob` are patched **only while needed**
  (pawn access disabled on some DSU / any Access Point exists).
* No `Thing.set_Position`, `Thing.TryAbsorbStack`, `Reachability.CanReach` or food search transpiler patches.
* Inspect string / gizmos / float menu / mouseover readout don't iterate the stored stacks.
* Items tab rebuilds only on content change (throttled) and draws only visible rows.
* Power draw recomputed only when content changed; ports and DSU use MapMeshOnly graphics.

## Not ported (yet)
* Bill ingredient search radius fix (counts DSU items via access point distance).
* LWM Deep Storage storage comps on DSU (only the architect category patch is kept).
