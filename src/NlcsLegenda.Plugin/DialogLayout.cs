using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal class LegendDialog : Form
{
    private readonly ResourceScope _resources = new();

    protected T Own<T>(T resource) where T : IDisposable => _resources.Own(resource);

    protected DialogSaveState TrackSaving(ButtonBar buttons, Func<string> snapshot, Func<string?> save)
    {
        var state = Own(new DialogSaveState(buttons, snapshot));
        bool Save()
        {
            if (!state.HasChanges) return true;
            try
            {
                if (save() is not { } error) { state.Saved(); return true; }
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
            { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            return false;
        }
        buttons.Apply!.Click += (_, _) => Save();
        FormClosing += (_, e) => { if (DialogResult == DialogResult.OK && !Save()) e.Cancel = true; };
        return state;
    }

    protected override void Dispose(bool disposing)
    {
        using var resources = disposing ? _resources : null;
        base.Dispose(disposing);
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

internal sealed class DialogSaveState : IDisposable
{
    private readonly ButtonBar _buttons;
    private readonly Func<string> _snapshot;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private string _saved;

    public DialogSaveState(ButtonBar buttons, Func<string> snapshot)
    {
        _buttons = buttons;
        _snapshot = snapshot;
        _saved = snapshot();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    public static string GridEdit(DataGridView grid) => grid.IsCurrentCellDirty
        ? $"{grid.CurrentCell?.RowIndex}:{grid.CurrentCell?.ColumnIndex}:{grid.CurrentCell?.EditedFormattedValue}"
        : string.Empty;

    public bool HasChanges => _snapshot() != _saved;
    public void Refresh()
    {
        bool changed = HasChanges;
        _buttons.Ok.Enabled = changed;
        if (_buttons.Apply is { } apply) apply.Enabled = changed;
    }
    public void Saved() { _saved = _snapshot(); Refresh(); }
    public void Dispose() => _timer.Dispose();
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
