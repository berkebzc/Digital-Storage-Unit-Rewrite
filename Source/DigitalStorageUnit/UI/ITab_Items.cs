using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DigitalStorageUnit;

/// <summary>
/// Items tab for the DSU and its ports.
/// Optimizations: the filtered/sorted list is only rebuilt when the DSU content version or the search
/// text changes (throttled to 2x per second), and only the visible rows are drawn.
/// </summary>
[StaticConstructorOnStartup]
public class ITab_Items : ITab
{
    private const float RowHeight = 28f;
    private const float RebuildInterval = 0.5f;

    private Vector2 scrollPos;
    private string searchQuery = "";
    private string builtQuery;
    private int builtVersion = -1;
    private DigitalStorageUnitBuilding builtFor;
    private float lastBuildTime = -999f;

    private readonly List<Entry> entries = [];

    private struct Entry
    {
        public Thing Thing;
        public string SortKey;
    }

    public ITab_Items()
    {
        size = new Vector2(480f, 480f);
        labelKey = "DSU.IItemsTab";
    }

    private DigitalStorageUnitBuilding SelectedDsu => SelThing switch
    {
        DigitalStorageUnitBuilding dsu => dsu,
        ABasePortDsuBuilding port => port.BoundDsu,
        _ => null
    };

    public override bool IsVisible => SelThing switch
    {
        DigitalStorageUnitBuilding => true,
        ABasePortDsuBuilding port => port.IsOperational,
        _ => false
    };

    public override void OnOpen()
    {
        base.OnOpen();
        builtVersion = -1;
    }

    private void RebuildIfNeeded(DigitalStorageUnitBuilding dsu)
    {
        var now = Time.realtimeSinceStartup;
        var forced = dsu != builtFor || searchQuery != builtQuery;
        if (!forced && (builtVersion == dsu.Version || now - lastBuildTime < RebuildInterval)) return;

        builtFor = dsu;
        builtQuery = searchQuery;
        builtVersion = dsu.Version;
        lastBuildTime = now;
        entries.Clear();

        var query = searchQuery.NullOrEmpty() ? null : searchQuery.ToLowerInvariant();
        foreach (var t in dsu.StoredItems)
        {
            if (t.Destroyed) continue;
            var inner = t.GetInnerIfMinified();
            var label = inner.LabelNoCount;
            var key = label.ToLowerInvariant();
            if (query != null && !key.Contains(query)) continue;
            entries.Add(new Entry { Thing = t, SortKey = key });
        }
        entries.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.SortKey, b.SortKey);
            return c != 0 ? c : b.Thing.stackCount.CompareTo(a.Thing.stackCount);
        });
    }

    protected override void FillTab()
    {
        var dsu = SelectedDsu;
        if (dsu == null) return;
        RebuildIfNeeded(dsu);

        Text.Font = GameFont.Small;
        var frame = new Rect(10f, 10f, size.x - 20f, size.y - 20f);
        Widgets.BeginGroup(frame);
        var curY = 0f;
        Widgets.ListSeparator(ref curY, frame.width, dsu.RenamableLabel);
        curY += 4f;

        var caption = searchQuery.NullOrEmpty()
            ? "DSU.IItemsTab.Caption".Translate(dsu.RenamableLabel, dsu.StoredCount, dsu.Capacity)
            : "DSU.IItemsTab.Caption.Filtered".Translate(dsu.RenamableLabel, dsu.StoredCount, dsu.Capacity, entries.Count);
        Widgets.Label(new Rect(0f, curY, frame.width, 24f), caption);
        curY += 26f;

        searchQuery = Widgets.TextField(new Rect(0f, curY, frame.width - 30f, 24f), searchQuery) ?? "";
        if (Widgets.ButtonImage(new Rect(frame.width - 24f, curY, 24f, 24f), TexButton.CloseXSmall, true, null))
            searchQuery = "";
        curY += 30f;

        var outRect = new Rect(0f, curY, frame.width, frame.height - curY);
        var viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(entries.Count * RowHeight, outRect.height - 1f));
        Widgets.BeginScrollView(outRect, ref scrollPos, viewRect, true);

        if (entries.Count == 0)
        {
            Widgets.Label(new Rect(0f, 0f, viewRect.width, 24f), "DSU.IItemsTab.Label.Empty".Translate());
        }
        else
        {
            // draw only the visible window
            var first = Mathf.Max(0, Mathf.FloorToInt(scrollPos.y / RowHeight) - 1);
            var last = Mathf.Min(entries.Count - 1, Mathf.CeilToInt((scrollPos.y + outRect.height) / RowHeight) + 1);
            for (var i = first; i <= last; i++)
            {
                var t = entries[i].Thing;
                if (t.Destroyed || !dsu.Contains(t))
                {
                    builtVersion = -1;
                    continue;
                }
                DrawRow(dsu, t, i * RowHeight, viewRect.width);
            }
        }

        Widgets.EndScrollView();
        Widgets.EndGroup();
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private void DrawRow(DigitalStorageUnitBuilding dsu, Thing thing, float y, float width)
    {
        var row = new Rect(0f, y, width, RowHeight);
        if (Mouse.IsOver(row))
        {
            GUI.color = ITab_Pawn_Gear.HighlightColor;
            GUI.DrawTexture(row, TexUI.HighlightTex);
            GUI.color = Color.white;
        }

        var x = width - 24f;
        Widgets.InfoCardButton(x, y, thing);

        x -= 24f;
        var allowed = !thing.IsForbidden(Faction.OfPlayer);
        var before = allowed;
        Widgets.Checkbox(x, y, ref allowed, 24f, false, true, null, null);
        TooltipHandler.TipRegion(new Rect(x, y, 24f, 24f), allowed ? "CommandNotForbiddenDesc".Translate() : "CommandForbiddenDesc".Translate());
        if (allowed != before) thing.SetForbidden(!allowed, false);

        x -= 24f;
        var dropRect = new Rect(x, y, 24f, 24f);
        if (Widgets.ButtonImage(dropRect, TexButton.Drop, Color.gray, Color.white, true, null))
        {
            if (dsu.DropThing(thing)) builtVersion = -1;
        }
        TooltipHandler.TipRegion(dropRect, "DSU.Drop".Translate(thing.LabelNoCount));

        Widgets.ThingIcon(new Rect(4f, y, RowHeight, RowHeight), thing, 1f, null, false, 1f, false);

        var labelRect = new Rect(36f, y, x - 40f, RowHeight);
        Text.Anchor = TextAnchor.MiddleLeft;
        Text.WordWrap = false;
        GUI.color = ITab_Pawn_Gear.ThingLabelColor;
        var label = thing.LabelCap;
        Widgets.Label(labelRect, label.Truncate(labelRect.width));
        GUI.color = Color.white;
        Text.WordWrap = true;
        Text.Anchor = TextAnchor.UpperLeft;

        if (Mouse.IsOver(labelRect))
        {
            var tip = thing.def.useHitPoints ? label + "\n" + thing.HitPoints + " / " + thing.MaxHitPoints : label;
            TooltipHandler.TipRegion(labelRect, tip);
        }
    }
}
