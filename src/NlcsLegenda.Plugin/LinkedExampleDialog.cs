using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class LinkedExampleDialog : LegendDialog
{
    private readonly ComboBox _target = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _description = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _status = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _component = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _follow = new() { Text = "Voorbeeld blijft de bronweergave volgen", Checked = true, AutoSize = true };
    private readonly ComboBox _display = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private sealed record DisplayChoice(string Handle, string Label) { public override string ToString() => Label; }
    public string MapViewportHandle => (_display.SelectedItem as DisplayChoice)?.Handle ?? "";
    private readonly TextBox _key = new() { Dock = DockStyle.Top };
    private sealed record Target(ManualEntry? Entry) { public override string ToString() => Entry?.Description ?? "Nieuwe eigen regel"; }
    private sealed record Component(CivilExampleComponent? Value) { public override string ToString() => Value?.Label ?? "Geselecteerd bronobject"; }
    public ManualEntry? Existing => (_target.SelectedItem as Target)?.Entry;
    public string Description => _description.Text.Trim();
    public string ComparisonKey => _key.Text.Trim();
    public NlcsStatus Status => (NlcsStatus)_status.SelectedItem!;
    public bool Follow => _follow.Checked;
    public CivilExampleComponent? SelectedComponent => (_component.SelectedItem as Component)?.Value;
    public LinkedExampleDialog(LegendDefinition definition, IReadOnlyList<CivilExampleComponent> components, string source, IReadOnlyList<(string Handle, string Label)>? viewports = null)
    {
        Text = "Bronvoorbeeld koppelen"; ClientSize = new Size(720, 500); MinimumSize = new Size(460, 350);
        StartPosition = FormStartPosition.CenterParent; ShowIcon = false; ShowInTaskbar = false;
        var form = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, Padding = new Padding(12) };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Field(string label, Control control) { form.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 0, 2) }); form.Controls.Add(control); }
        _target.Items.Add(new Target(null)); foreach (var m in definition.Settings.ManualEntries) _target.Items.Add(new Target(m)); _target.SelectedIndex = 0;
        _description.Items.AddRange(new object[] { "Bestaand maaiveld", "Ontwerpmaaiveld", "IT-riool", "GWA-riool", "GWA-put", "Overlap rioolkruising" });
        _status.Items.AddRange(Enum.GetValues<NlcsStatus>().Cast<object>().ToArray()); _status.SelectedItem = NlcsStatus.Bestaand;
        if (components.Count == 0) _component.Items.Add(new Component(null)); else foreach (var c in components) _component.Items.Add(new Component(c));
        _component.SelectedIndex = 0;
        _display.Items.Add(new DisplayChoice("", "Globale laagweergave van de tekening"));
        foreach (var view in viewports ?? Array.Empty<(string Handle, string Label)>()) _display.Items.Add(new DisplayChoice(view.Handle, view.Label));
        _display.SelectedIndex = 0;
        _target.SelectedIndexChanged += (_, _) => { if (Existing is { } m) { _description.Text = m.Description; _status.SelectedItem = m.Status; _key.Text = m.ComparisonKey; _follow.Checked = m.ExampleMode == ExampleMode.FollowSource; _display.SelectedItem = _display.Items.Cast<DisplayChoice>().FirstOrDefault(v => v.Handle == (m.Link?.MapViewportHandle ?? "")) ?? _display.Items[0]; } };
        _description.SelectionChangeCommitted += (_, _) => _key.Text = _description.SelectedItem?.ToString() ?? "";
        Field("Regel", _target); Field("Omschrijving", _description); Field("Status", _status);
        Field("Bron / profielvenster / stijlcomponent", _component); Field("Laagweergave", _display); Field("Onderdeelnaam voor vergelijking tussen bladen (optioneel)", _key);
        form.Controls.Add(_follow);
        var hint = new Label { Text = source + Environment.NewLine + "Het bronobject blijft ongewijzigd. Een bestaande eigen regel behoudt zijn plaats en identiteit.", AutoSize = true, MaximumSize = new Size(650, 0), Padding = new Padding(0, 10, 0, 0) };
        form.Controls.Add(hint); SizeChanged += (_, _) => hint.MaximumSize = new Size(Math.Max(100, ClientSize.Width - 55), 0);
        var buttons = new ButtonBar(withApply: false); buttons.Ok.Text = "Koppelen"; AcceptButton = buttons.Ok; CancelButton = buttons.Cancel;
        FormClosing += (_, e) => { if (DialogResult == DialogResult.OK && Description.Length == 0) { e.Cancel = true; DialogResult = DialogResult.None; _description.Focus(); } };
        Controls.Add(form); Controls.Add(buttons);
    }
}
