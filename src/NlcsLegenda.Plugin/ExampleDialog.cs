using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class ExampleDialog : LegendDialog
{
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "Zoek een voorbeeld" };
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly Label _path = new() { AutoSize = true, Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Func<string, IReadOnlyList<LegendExample>> _load;
    private IReadOnlyList<LegendExample> _examples = Array.Empty<LegendExample>();
    public string LibraryPath { get; private set; }
    public string? SelectedBlock { get; private set; }
    public LegendExample? SelectedExample { get; private set; }
    public bool AddEntry { get; private set; }
    private sealed record Choice(LegendExample Example)
    {
        public override string ToString() => $"{Example.Section} · {Example.Name} · 1:{Example.Scale:g}";
    }

    public ExampleDialog(string path, Func<string, IReadOnlyList<LegendExample>> load)
    {
        LibraryPath = path; _load = load;
        Text = "Voorbeeld kiezen";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(720, 480); MinimumSize = new Size(480, 300);
        StartPosition = FormStartPosition.CenterParent; ShowIcon = false; ShowInTaskbar = false;
        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var browse = new Button { Text = "Bibliotheek…", AutoSize = true };
        top.Controls.Add(_path, 0, 0); top.Controls.Add(browse, 1, 0); top.Controls.Add(_search, 0, 1); top.SetColumnSpan(_search, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6), FlowDirection = FlowDirection.LeftToRight };
        void Button(string text, Action action) { var b = new Button { Text = text, AutoSize = true }; b.Click += (_, _) => action(); buttons.Controls.Add(b); }
        Button("Gebruiken", () => Choose(false));
        Button("Als nieuwe regel", () => Choose(true));
        Button("Uit tekening", () => { SelectedBlock = ""; DialogResult = DialogResult.OK; Close(); });
        Button("Automatisch", () => { SelectedBlock = null; DialogResult = DialogResult.OK; Close(); });
        Button("Annuleren", () => { DialogResult = DialogResult.Cancel; Close(); });
        Controls.Add(_list); Controls.Add(top); Controls.Add(buttons);
        _search.TextChanged += (_, _) => Filter();
        _list.DoubleClick += (_, _) => Choose(false);
        browse.Click += (_, _) =>
        {
            using var file = new OpenFileDialog { Filter = "Voorbeeldenbibliotheek (*.dwg)|*.dwg", CheckFileExists = true };
            if (file.ShowDialog(this) == DialogResult.OK) LoadLibrary(file.FileName);
        };
        Shown += (_, _) => { if (!string.IsNullOrWhiteSpace(path)) LoadLibrary(path); };
    }

    private void LoadLibrary(string path)
    {
        try
        {
            _examples = _load(path); LibraryPath = path; _path.Text = System.IO.Path.GetFileName(path); Filter();
        }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex)) { MessageBox.Show(this, ex.Message, "Voorbeelden", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void Filter()
    {
        _list.Items.Clear();
        foreach (var example in _examples.Where(e => (e.Section + " " + e.Name).Contains(_search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            _list.Items.Add(new Choice(example));
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    private void Choose(bool add)
    {
        if (_list.SelectedItem is not Choice choice) return;
        SelectedExample = choice.Example; SelectedBlock = choice.Example.BlockName; AddEntry = add;
        DialogResult = DialogResult.OK; Close();
    }
}
