using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal class LegendDialog : Form
{
    private readonly ResourceScope _resources = new();

    protected T Own<T>(T resource) where T : IDisposable => _resources.Own(resource);

    protected override void Dispose(bool disposing)
    {
        try { base.Dispose(disposing); }
        finally { if (disposing) _resources.Dispose(); }
    }

    public LegendDialog()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    protected override void OnLoad(EventArgs e)
    {
        var area = Screen.FromControl(Owner ?? this).WorkingArea;
        var available = new Size(Math.Max(1, area.Width - 24), Math.Max(1, area.Height - 24));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, available.Width), Math.Min(MinimumSize.Height, available.Height));
        Size = new Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
        base.OnLoad(e);
    }
}

internal sealed class SettingsStack : TableLayoutPanel
{
    public SettingsStack()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Margin = Padding.Empty;
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        e.Control!.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
    }
}
