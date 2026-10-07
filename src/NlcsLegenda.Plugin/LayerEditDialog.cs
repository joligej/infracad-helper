using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

// Bewerkt een NLCS-laagnaam component voor component, met live preview en validatie tegen de
// formele NLCS-codes. De dialog kent de tekening niet; de aanroeper levert het aantal geraakte
// entiteiten en een probe die vertelt of de doelnaam al bestaat (botsing/samenvoegen).
internal sealed class LayerEditDialog : Form
{
    private readonly NlcsLayerComponents _comp;
    private readonly Func<string, (bool exists, int count, string info)>? _probe;
    private readonly int _affected;
    private readonly string _sourceName;
    private readonly bool _sourceLocked;

    private readonly ComboBox _status = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 70 };
    private readonly TextBox _sub = new() { Width = 50 };
    private readonly TextBox _disc = new() { Width = 70 };
    private readonly TextBox _hoofd = new() { Width = 70 };
    private readonly TextBox _object = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _element = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 90 };
    private readonly TextBox _scale = new() { Width = 70 };
    private readonly Label _preview = new() { AutoSize = false, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _status2 = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.Firebrick, TextAlign = ContentAlignment.TopLeft };
    private readonly Button _ok;

    public NlcsLayerComponents Result => _comp;

    // True als bij een botsing met een bestaande laag moet worden samengevoegd.
    public bool MergeIntoExisting { get; private set; }

    public LayerEditDialog(
        NlcsLayerComponents initial, string sourceName, int affected, bool sourceLocked,
        Func<string, (bool exists, int count, string info)>? targetProbe)
    {
        _comp = initial;
        _affected = affected;
        _probe = targetProbe;
        _sourceName = sourceName;
        _sourceLocked = sourceLocked;

        Text = "NLCS Legenda \u2013 laagnaam bewerken";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(560, 360);
        MinimumSize = new Size(520, 340);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;

        _status.Items.AddRange(NlcsLayerComponents.StatusCodes);
        _element.Items.AddRange(NlcsLayerComponents.ElementCodes);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoSize = false };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = $"Huidige laag: {sourceName}", AutoSize = true, ForeColor = Color.DimGray }, 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 2);

        AddRow(layout, 1, "Status", Flow(_status, new Label { Text = "subnr.", AutoSize = true, Margin = new Padding(8, 6, 2, 0) }, _sub));
        AddRow(layout, 2, "Discipline", _disc);
        AddRow(layout, 3, "Hoofdgroep", _hoofd);
        AddRow(layout, 4, "Object (met - gescheiden)", _object);
        AddRow(layout, 5, "Element", _element);
        AddRow(layout, 6, "Schaal (optioneel)", _scale);
        AddRow(layout, 7, "Nieuwe naam", _preview);
        AddRow(layout, 8, "Controle", _status2);
        layout.RowStyles.Clear();
        for (int i = 0; i < 8; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var buttons = new ButtonBar(withApply: false);
        _ok = buttons.Ok;
        _ok.Text = "Hernoemen";
        AcceptButton = _ok;
        CancelButton = buttons.Cancel;

        Controls.Add(layout);
        Controls.Add(buttons);

        LoadFields();
        foreach (Control c in new Control[] { _status, _sub, _disc, _hoofd, _object, _element, _scale })
        {
            if (c is TextBox tb) tb.TextChanged += (_, _) => Recalculate();
            if (c is ComboBox cb) { cb.TextChanged += (_, _) => Recalculate(); cb.SelectedIndexChanged += (_, _) => Recalculate(); }
        }
        Recalculate();
    }

    private void LoadFields()
    {
        _status.Text = _comp.Status;
        _sub.Text = _comp.SubStatus;
        _disc.Text = _comp.Discipline;
        _hoofd.Text = _comp.Hoofdgroep;
        _object.Text = string.Join('-', _comp.ObjectParts);
        _element.Text = _comp.Element;
        _scale.Text = _comp.Scale;
    }

    private void Recalculate()
    {
        _comp.Status = _status.Text;
        _comp.SubStatus = _sub.Text;
        _comp.Discipline = _disc.Text;
        _comp.Hoofdgroep = _hoofd.Text;
        _comp.ObjectParts = _object.Text.Split('-', StringSplitOptions.RemoveEmptyEntries).ToList();
        _comp.Element = _element.Text;
        _comp.Scale = _scale.Text;

        var name = _comp.Compose();
        _preview.Text = name;

        var errors = _comp.Validate();
        var lines = new List<string>();
        lines.AddRange(errors);

        MergeIntoExisting = false;
        if (errors.Count == 0 && _probe is not null)
        {
            var (exists, count, info) = _probe(name);
            if (exists)
            {
                MergeIntoExisting = true;
                lines.Add($"Let op: laag \"{name}\" bestaat al ({count} entiteit(en){(string.IsNullOrEmpty(info) ? "" : ", " + info)}).");
                lines.Add("Hernoemen voegt samen: de entiteiten gaan naar die laag en de bronlaag wordt verwijderd. De eigenschappen van de bestaande laag blijven behouden.");
            }
        }
        if (_sourceLocked)
            lines.Add("Let op: de huidige laag is vergrendeld. Hernoemen kan wel.");
        lines.Add($"Raakt {_affected} entiteit(en) op de huidige laag.");

        _status2.ForeColor = errors.Count > 0 ? Color.Firebrick : Color.DimGray;
        _status2.Text = string.Join(Environment.NewLine, lines);
        _ok.Enabled = errors.Count == 0 && !string.Equals(name, _sourceName, StringComparison.Ordinal);
    }

    private static Control Flow(params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        p.Controls.AddRange(controls);
        return p;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, row);
        layout.Controls.Add(control, 1, row);
    }
}
