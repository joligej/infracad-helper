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

    protected override void OnLayout(LayoutEventArgs e)
    {
        foreach (Control control in Controls)
            if (control is Label or CheckBox)
                control.MaximumSize = new Size(Math.Max(1, ClientSize.Width - Padding.Horizontal - control.Margin.Horizontal), 0);
        base.OnLayout(e);
        if (Parent is ScrollableControl { AutoScroll: true } scroll)
        {
            var range = new Size(0, Height + scroll.Padding.Vertical + Margin.Vertical);
            if (scroll.AutoScrollMinSize != range)
            {
                scroll.AutoScrollMinSize = range;
                if (scroll.IsHandleCreated)
                    scroll.BeginInvoke((Action)(() => { if (!scroll.IsDisposed) scroll.PerformLayout(); }));
            }
        }
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        e.Control!.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
    }
}

internal sealed class SettingsRow : TableLayoutPanel
{
    private readonly Label _label;
    private readonly Control _input;
    private bool _stacked;
    private bool _arranging;

    public SettingsRow(string text, Control input)
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(3, 3, 3, 6);
        ColumnCount = 2;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _label = new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(0, 4, 6, 4) };
        _input = input;
        _input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(_label, 0, 0);
        Controls.Add(_input, 1, 0);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (_label is null || _arranging) { base.OnLayout(e); return; }
        _arranging = true;
        try
        {
            bool stacked = ClientSize.Width < Font.Height * 27;
            if (stacked != _stacked)
            {
                _stacked = stacked;
                SetCellPosition(_input, new TableLayoutPanelCellPosition(stacked ? 0 : 1, stacked ? 1 : 0));
                ColumnStyles[0].Width = stacked ? 100 : 48;
                ColumnStyles[1].Width = stacked ? 0 : 52;
                RowCount = stacked ? 2 : 1;
                if (RowStyles.Count < RowCount) RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            int width = (int)(ClientSize.Width * (_stacked ? 1 : 0.48)) - _label.Margin.Horizontal;
            _label.MaximumSize = new Size(Math.Max(1, width), 0);
            base.OnLayout(e);
        }
        finally { _arranging = false; }
    }
}
