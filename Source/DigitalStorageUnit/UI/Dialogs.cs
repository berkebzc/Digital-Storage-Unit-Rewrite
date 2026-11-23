using System;
using UnityEngine;
using Verse;

namespace DigitalStorageUnit;

public class Dialog_RenameDsu : Dialog_Rename<IRenameable>
{
    public Dialog_RenameDsu(IRenameable renaming) : base(renaming)
    {
    }
}

public class Dialog_OutputMinMax : Window
{
    private readonly OutputSettings settings;
    private readonly Action onClose;
    private string minBuffer;
    private string maxBuffer;

    public Dialog_OutputMinMax(OutputSettings settings, Action onClose)
    {
        this.settings = settings;
        this.onClose = onClose;
        doCloseX = true;
        doCloseButton = true;
        closeOnClickedOutside = true;
        absorbInputAroundWindow = true;
        draggable = true;
    }

    public override Vector2 InitialSize => new(420f, 300f);

    public override void DoWindowContents(Rect inRect)
    {
        var l = new Listing_Standard();
        l.Begin(inRect);
        l.Label("DSU.MinMaxAmounts".Translate());
        l.GapLine();

        l.CheckboxLabeled("DSU.UseMin".Translate(), ref settings.UseMin, "DSU.Min.Desc".Translate(), 0f, 1f);
        if (settings.UseMin)
        {
            var v = settings.Min;
            l.TextFieldNumericLabeled("DSU.Min".Translate() + " ", ref v, ref minBuffer, 0, 100000);
            settings.Min = v;
        }
        l.Gap();

        l.CheckboxLabeled("DSU.UseMax".Translate(), ref settings.UseMax, "DSU.Max.Desc".Translate(), 0f, 1f);
        if (settings.UseMax)
        {
            var v = settings.Max;
            l.TextFieldNumericLabeled("DSU.Max".Translate() + " ", ref v, ref maxBuffer, 1, 100000);
            settings.Max = v;
        }
        l.End();
    }

    public override void PostClose()
    {
        base.PostClose();
        onClose?.Invoke();
    }
}
