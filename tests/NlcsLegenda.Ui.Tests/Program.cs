using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;
using NlcsLegenda.Plugin;

internal static class Program
{
    private static int _checks;
    private static readonly List<string> Errors = new();
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--icons"])
        {
            DiagnosticRun.Execute(CheckRibbonIcons, Errors.Add);
            foreach (var error in Errors) Console.WriteLine("FAIL " + error);
            Console.WriteLine($"Pictogrammen: {_checks} controles; {Errors.Count} fouten");
            return Errors.Count == 0 ? 0 : 1;
        }
        Application.EnableVisualStyles();
        if (args is ["--docs", var directory])
        {
            DocumentationImages.Write(directory);
            return 0;
        }
        DiagnosticRun.Execute(() =>
        {
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
                DiagnosticRun.Execute(() => CheckSettings(scale, args.FirstOrDefault()), Errors.Add, "CheckSettings");
            DiagnosticRun.Execute(() => CheckLayerSearch(), Errors.Add, "CheckLayerSearch");
            DiagnosticRun.Execute(() => CheckSettingsSave(), Errors.Add, "CheckSettingsSave");
            DiagnosticRun.Execute(() => CheckTemplateReset(), Errors.Add, "CheckTemplateReset");
            DiagnosticRun.Execute(() => CheckXrefControls(), Errors.Add, "CheckXrefControls");
            DiagnosticRun.Execute(() => CheckOverview(), Errors.Add, "CheckOverview");
            DiagnosticRun.Execute(() => CheckOverviewLoading(), Errors.Add, "CheckOverviewLoading");
            DiagnosticRun.Execute(CheckOverviewFatalError, Errors.Add);
            DiagnosticRun.Execute(CheckTreeImagesDisposed, Errors.Add);
            DiagnosticRun.Execute(CheckOwnedFontsDisposed, Errors.Add);
            DiagnosticRun.Execute(() => CheckKlic(args.FirstOrDefault()), Errors.Add, "CheckKlic");
            DiagnosticRun.Execute(() => CheckNestedEditors(), Errors.Add, "CheckNestedEditors");
            DiagnosticRun.Execute(() => CheckCustomizationRoundtrip(), Errors.Add, "CheckCustomizationRoundtrip");
            DiagnosticRun.Execute(() => CheckStatusReassignment(), Errors.Add, "CheckStatusReassignment");
            Func<Form>[] factories = {
                () => new OverviewDialog(new[] { new OverviewLegend { Id = "test", Name = "Voorbeeld", Settings = new(), Entries = new[] { Entry("Regel") } } }, _ => null),
                () => new LayerChoiceDialog(new[] { new ManualEntry { Layer = "N-WE-GR-BOOM-V", Description = "Boom" } }),
                () => new ExampleDialog("voorbeelden.dwg", _ => new[] { new LegendExample("b", "Scheidingband met tekst en meerdere lijnen", "WEG", 200, Array.Empty<string>()) }),
                () => new XrefInclusionDialog(new[] { "Een lange xrefnaam met veel informatie.dwg" }, new()),
                () => new CustomStatusDialog(new[] { new CustomStatus { Name = "Eigen status" } }, new[] { ("key", "Voorbeeldregel") }),
                () => new RemarksEditDialog("Notities", StandardTexts.DrawingRemarks),
                () => new DescriptionsDialog(DescriptionCatalog.Default()),
                () => new TextEditDialog("VH|ELEMENT", new DescriptionEntry { Specifiek = "Een langere omschrijving" }, new()),
                () => new BlankEntryDialog(new[] { new BlankEntry() }),
                () => new CustomLayerDialog(new[] { new CustomLayerRule { Layer = "Eigen laag" } }),
                () => new LegendManageDialog(new[] { new EntryCheckItem("key", "Regel", "DATA") }, new()),
                () => new LayerEditDialog(new NlcsLayerComponents(), "N-WE-KL-DATA-G", 10, false, null),
                () => new HelpDialog("test"),
                () => new KlicOptionsDialog(new KlicOptions()),
                () => new KlicDetailsDialog(new LegendEntry { Status = NlcsStatus.Bestaand, Discipline = "WE", Hoofdgroep = "KL", Element = "WATER", Description = "Waterleiding", KlicObjects = new[] {
                    new KlicObjectData("C:/voorbeeld/een lange bronnaam.dwg", "ABC", "KLIC", new[] {
                        new KlicRecord(KlicSource.InfraCAD, "KLIC_LeidingLijn", new Dictionary<string,string> { ["DiameterM"] = "0.11", ["Materiaal"] = "PE", ["Bijlage"] = "C:/voorbeeld/bijlage.pdf" }) },
                        new[] { "Object Data niet gecontroleerd in deze testhost." }, KlicSource.InfraCAD) } })
            };
            foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
            foreach (var factory in factories)
                DiagnosticRun.Execute(() => CheckForm(factory, scale, args.FirstOrDefault()), Errors.Add, $"venster {scale}");
        }, Errors.Add);
        foreach(var error in Errors) Console.WriteLine("FAIL " + error);
        Console.WriteLine($"UI: {_checks} controles; {Errors.Count} fouten");
        return Errors.Count == 0 ? 0 : 1;
    }

    private static void CheckForm(Func<Form> factory, float scale, string? output)
    {
        using var form = factory();
        OpenOffscreen(form);
        form.AutoScaleMode = AutoScaleMode.None;
        form.Scale(new SizeF(scale, scale));
        form.Font = new Font(form.Font.FontFamily, 9f * scale);
        form.MinimumSize = new Size(480, 340);
        form.ClientSize = new Size(720, 540);
        Application.DoEvents();
        CheckButtons(form);
        Check(Descendants(form).All(c => c is not TextBox || c.Height >= c.Font.Height), form.Text + ": teksthoogte");
        if (form is ExampleDialog && output is not null)
        {
            Directory.CreateDirectory(output);
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Join(output, $"examples-{scale:0.00}.png"));
        }
        var controls = Descendants(form).ToArray();
        string title = form.Text;
        form.Close();
        form.Dispose();
        Check(controls.All(control => control.IsDisposed), title + ": venster geeft alle onderliggende controls vrij");
    }

    private static void CheckOverviewFatalError()
    {
        var legend = new OverviewLegend { Id = "fatal", Name = "Proef", Settings = new(), Entries = new[] { Entry("Bestaand") } };
        bool fail = false;
        var error = new OutOfMemoryException("simulatie");
        using var dialog = new OverviewDialog(new[] { legend }, _ => null,
            _ => fail ? throw error : legend.Entries);
        fail = true;
        legend.NeedsAnalysis = true;
        bool propagated = false;
        try
        {
            typeof(OverviewDialog).GetMethod("LoadLegend", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(dialog, null);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ReferenceEquals(ex.InnerException, error))
        { propagated = true; }
        Check(propagated, "Overzicht behandelt geheugenuitputting niet als een gewone bronfout");
        Check(!dialog.UseWaitCursor, "Wachtcursor hersteld na ernstige bronfout");
        Check(legend.Entries.Single().Description == "Bestaand", "Ernstige bronfout overschrijft de bestaande lijst niet");
    }

    private static void CheckRibbonIcons()
    {
        string[] commands = { "NLCSLEGENDA", "NLCSLEGENDAUPDATE", "NLCSLEGENDAINFO",
            "NLCSLEGENDASAMENSTELLEN", "NLCSLEGENDAOMSCHRIJVINGEN", "NLCSLEGENDATEKST",
            "NLCSLEGENDASTATUS", "NLCSLEGENDAXREFS", "NLCSLEGENDALAAGNAAM",
            "NLCSLEGENDAVIEWPORT", "NLCSLEGENDAEXPORT", "NLCSLEGENDABATCH",
            "NLCSLEGENDAOPTIES", "NLCSLEGENDASTANDAARD", "NLCSLEGENDAPRESET",
            "NLCSLEGENDABEHEER", "NLCSLEGENDAELEMENT", "NLCSLEGENDACONFIG", "NLCSLEGENDAHELP" };
        foreach (string command in commands)
        foreach (int size in new[] { 16, 32 })
        {
            var image = RibbonIcons.Get(command, size) as System.Windows.Media.Imaging.BitmapSource;
            Check(image is not null, command + ": pictogram aanwezig");
            if (image is null) continue;
            Check(image.IsFrozen && image.PixelWidth == size && image.PixelHeight == size,
                command + ": vaste afbeelding op de gevraagde maat");
            var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var pixels = new byte[size * size * 4];
            converted.CopyPixels(pixels, size * 4, 0);
            int white = 0;
            for (int i = 0; i < pixels.Length; i += 4)
                if (pixels[i] > 220 && pixels[i + 1] > 150 && pixels[i + 2] > 50 && pixels[i + 3] > 127) white++;
            Check(white > 4, command + ": herkenbaar teken binnen de blauwe achtergrond");
            Check(ReferenceEquals(image, RibbonIcons.Get(command, size)), command + ": afbeelding hergebruikt");
        }
    }

    private static void CheckOwnedFontsDisposed()
    {
        using var image = new Bitmap(100, 100);
        using var graphics = Graphics.FromImage(image);
        using var settings = new SettingsDialog(new(), "Fontproef");
        using var layer = new LayerEditDialog(new NlcsLayerComponents(), "N-WE-KL-DATA-G", 0, false, null);
        foreach (var dialog in new Form[] { settings, layer })
        {
            var font = Descendants(dialog).OfType<Label>().Single(label => label.Font.Bold).Font;
            Check(graphics.MeasureString("Voorbeeld", font).Width > 0, "Eigen lettertype werkt voor sluiten");
            dialog.Dispose();
            bool disposed = false;
            try { graphics.MeasureString("Voorbeeld", font); }
            catch (ArgumentException) { disposed = true; }
            Check(disposed, dialog.Text + ": eigen lettertype vrijgegeven");
        }
        Check(graphics.MeasureString("Voorbeeld", SystemFonts.MessageBoxFont!).Width > 0,
            "Gedeeld Windows-lettertype blijft bruikbaar");
    }

    private static void CheckTreeImagesDisposed()
    {
        foreach (bool createHandle in new[] { false, true })
        {
            using var tree = new TriStateTree();
            var images = tree.StateImageList!;
            bool disposed = false;
            images.Disposed += (_, _) => disposed = true;
            if (createHandle)
            {
                tree.Nodes.Add(new TreeNode("Voorbeeld") { StateImageIndex = 1 });
                _ = tree.Handle;
                using var bitmap = new Bitmap(160, 100);
                tree.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            }
            tree.Dispose();
            Check(disposed, $"Samenstellen geeft eigen icoonlijst vrij (handle={createHandle})");
            Check(!images.HandleCreated, "Gesloten boom houdt geen afbeeldingshandle vast");
        }
    }

    private static void CheckSettingsSave()
    {
        int saves = 0;
        using var dialog = new SettingsDialog(new(), "Legenda: A") { SaveChanges = () => { saves++; return null; } };
        OpenOffscreen(dialog);
        Descendants(dialog).OfType<Button>().Single(b => b.Text == "Toepassen").PerformClick();
        Check(saves == 1 && dialog.Visible, "Toepassen slaat onmiddellijk op en houdt het venster open");
        dialog.DialogResult = DialogResult.OK;
        dialog.Close();
        Check(saves == 2, "Opslaan gebruikt hetzelfde toepaspad");
    }

    private static void CheckLayerSearch()
    {
        using var picker = new LayerChoiceDialog(new[] {
            new ManualEntry { Layer = "N-WE-GR-BOOM-V", Description = "Boom" },
            new ManualEntry { Layer = "N-WE-GR-GRAS-V", Description = "Gras" } });
        OpenOffscreen(picker);
        var search = Descendants(picker).OfType<TextBox>().Single();
        var grid = Descendants(picker).OfType<DataGridView>().Single();
        search.Text = "*gr*bOo*";
        Check(grid.Rows.Count == 1 && picker.Selected?.Layer == "N-WE-GR-BOOM-V", "Laag toevoegen zoekt in het midden, zonder hoofdletterverschil");
        search.Text = "ontbreekt";
        Check(grid.Rows.Count == 0 && picker.Selected is null, "Lege zoekuitslag kan geen oude laag toevoegen");
        picker.Close();
        using var manage = new LegendManageDialog(new[] {
            new EntryCheckItem("a", "Waterleiding", "KLIC", "BRON|B-WE-KL-WATER-G"),
            new EntryCheckItem("b", "Datakabel", "KLIC", "BRON|B-WE-KL-DATA-G") }, new());
        OpenOffscreen(manage);
        Descendants(manage).OfType<TextBox>().Single().Text = "-water-";
        var tree = Descendants(manage).OfType<TriStateTree>().Single();
        Check(tree.Nodes.Count == 1 && tree.Nodes[0].Nodes.Count == 1, "Samenstellen verbergt niet-passende lagen binnen de groep");
        tree.SelectedNode = tree.Nodes[0];
        typeof(TriStateTree).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(tree, new object[] { new KeyEventArgs(Keys.Space) });
        Check(manage.ExcludedKeys.SetEquals(new[] { "a" }), "Gefilterde groep schakelt alleen zichtbare lagen uit");
        manage.Close();
    }

    private static void CheckStatusReassignment()
    {
        using var dialog = new CustomStatusDialog(new[] {
            new CustomStatus { Name = "Fase 1", Members = new() { "regel" } },
            new CustomStatus { Name = "Fase 2" } }, new[] { ("regel", "Waterleiding") });
        OpenOffscreen(dialog);
        Descendants(dialog).OfType<ListBox>().Single(c => c is not CheckedListBox).SelectedIndex = 1;
        Descendants(dialog).OfType<CheckedListBox>().Single().SetItemChecked(0, true);
        Check(dialog.Result[0].Members.Count == 0 && dialog.Result[1].Members.SequenceEqual(new[] { "regel" }),
            "Toewijzen aan een andere status verplaatst de regel");
        dialog.Close();
    }

    private static void CheckCustomizationRoundtrip()
    {
        var settings = new LegendSettings { ManualEntries = new() {
            new() { Layer = "EIGEN", Type = NlcsDrawType.Symbool, SymbolBlock = "PUT", Description = "Put", Hoofdgroep = "RI", Status = NlcsStatus.Overig },
            new() { Layer = "EIGEN", Type = NlcsDrawType.Arcering, HatchPattern = "ANSI31", Description = "Vlak", Hoofdgroep = "VH" } } };
        using (var dialog = new LegendManageDialog(Array.Empty<EntryCheckItem>(), settings))
        {
            OpenOffscreen(dialog);
            var actual = dialog.ManualEntries;
            Check(actual[0].SymbolBlock == "PUT", "Samenstellen behoudt het eigen symboolblok");
            Check(actual[0].Hoofdgroep == "RI" && actual[1].Hoofdgroep == "VH", "Samenstellen behoudt eigen hoofdgroepen");
            Check(actual[0].Status == NlcsStatus.Overig, "Samenstellen behoudt status Overig");
            Check(actual[1].HatchPattern == "ANSI31", "Samenstellen behoudt eigen arceringspatroon");
            dialog.Close();
        }
        var options = new KlicOptions { Columns = new() { "Materiaal", "Thema" }, DescriptionFields = new() { "DiameterM", "Materiaal" } };
        using var klic = new KlicOptionsDialog(options);
        OpenOffscreen(klic);
        klic.DialogResult = DialogResult.OK;
        klic.Close();
        Check(klic.Result.Columns.SequenceEqual(options.Columns), "KLIC-venster behoudt kolomvolgorde zonder wijziging");
        Check(klic.Result.DescriptionFields.SequenceEqual(options.DescriptionFields), "KLIC-venster behoudt volgorde van omschrijvingsvelden");
    }

    private static void CheckTemplateReset()
    {
        var settings = new LegendSettings { Columns = 7, MaxRowsPerColumn = 2, MaxLegendHeightMm = 12,
            LineSpacingFactor = 1.35, Scale = 500, RemarksText = "Eigen notitie" };
        using var form = new SettingsDialog(settings, "Templateherstel");
        OpenOffscreen(form);
        Descendants(form).OfType<Button>().Single(b => b.Text == "Templateopmaak herstellen").PerformClick();
        Check(settings.Columns == 0 && settings.MaxRowsPerColumn == 30 && Math.Abs(settings.MaxLegendHeightMm) < 1e-10,
            "Templateherstel herstelt de kolomindeling");
        Check(Math.Abs(settings.LineSpacingFactor - 0.624) < 1e-10, "Templateherstel bewaart de exacte DWT-regelafstand");
        Check(Math.Abs(settings.Scale - 500) < 1e-10 && settings.RemarksText == "Eigen notitie", "Templateherstel bewaart schaal en eigen tekst");
        var tabs = Descendants(form).OfType<TabControl>().Single();
        tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Maten");
        Application.DoEvents();
        Check(Descendants(tabs.SelectedTab!).OfType<NumericUpDown>().Any(n => n.Value == 0.624M && n.DecimalPlaces == 3),
            "Maten toont regelafstand zonder afronden");
        form.Close();
    }

    private static void CheckXrefControls()
    {
        var settings = new LegendSettings { IncludeXrefLayers = false };
        settings.XrefInclusion["KLIC"] = true;
        using var form = new SettingsDialog(settings, "Test", xrefNames: new[] { "KLIC", "WEGEN" });
        OpenOffscreen(form);
        var tabs = Descendants(form).OfType<TabControl>().Single();
        var navigation = Descendants(form).OfType<ListBox>().Single();
        navigation.SelectedIndex = 1;
        Application.DoEvents();
        Check(tabs.SelectedIndex == 1, "Navigatie kiest bronnen");
        Check(Descendants(form).OfType<CheckBox>().Count(c => c.Text == "Titel tonen") == 1, "Titelinstelling heeft een vaste plek");
        var checkbox = Descendants(form).OfType<CheckBox>().Single(c => c.Text.StartsWith("Xrefs meenemen"));
        Check(checkbox.CheckState == CheckState.Indeterminate, "Gemengde xref-keuze zichtbaar");
        void Click() => typeof(CheckBox).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(checkbox, new object[] { EventArgs.Empty });
        Click();
        Check(settings.IsXrefIncluded("KLIC") && settings.IsXrefIncluded("WEGEN"), "Algemeen aan overschrijft oude uitzondering");
        Click();
        Check(!settings.IsXrefIncluded("KLIC") && !settings.IsXrefIncluded("WEGEN"), "Algemeen uit overschrijft oude uitzondering");
        Check(Descendants(form).OfType<Button>().Count(b => b.Text.StartsWith("Opmerkingen")) == 1, "Opmerkingen heeft een vaste plek");
        Check(tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Schaalbalk").Controls.Cast<Control>().SelectMany(Descendants).OfType<NumericUpDown>().Count() == 3,
            "Schaalbalk bevat alleen eigen maatvelden");
        form.Close();
    }

    private static void CheckSettings(float scale, string? output)
    {
        var settings = new LegendSettings();
        using var form = new SettingsDialog(settings, "Legenda: voorbeeld", new[] { new EntryCheckItem("key", "Data", "DATA") });
        OpenOffscreen(form);
        form.AutoScaleMode = AutoScaleMode.None;
        form.Scale(new SizeF(scale, scale));
        form.Font = new Font(form.Font.FontFamily, 9f * scale);
        form.MinimumSize = new Size(560, 420);
        form.ClientSize = new Size(scale.Equals(1f) ? 720 : 900, 620);
        var tabs = Descendants(form).OfType<TabControl>().Single();
        foreach(TabPage page in tabs.TabPages)
        {
            tabs.SelectedTab = page;
            Application.DoEvents();
            foreach(var group in Descendants(page).OfType<GroupBox>())
            {
                Check(group.Width > 100 && group.Height > group.Font.Height * 2, $"{scale} / {page.Text} / {group.Text}: niet ingeklapt");
                Check(group.Controls[0].Height >= 25, $"{group.Text}: inhoud zichtbaar");
            }
            foreach(var control in Descendants(page).Where(c => c is NumericUpDown or TextBox or ComboBox or CheckBox))
            {
                page.ScrollControlIntoView(control);
                Application.DoEvents();
                Check(control.Width >= 30 && control.Height >= control.Font.Height, $"{scale} / {page.Text} / {control.Text}: bedienbare afmetingen");
            }
            page.AutoScrollPosition = Point.Empty;
            CheckButtons(form);
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Join(output, $"settings-{tabs.SelectedIndex}-{scale:0.00}.png"));
            }
        }
        tabs.SelectedIndex = 1;
        var number = Descendants(tabs.SelectedTab!).OfType<CheckBox>().Single(c => c.Text == "Volgnummer samenvoegen");
        number.Checked = true;
        Check(settings.SamenvoegenNummer, "KLIC-keuze wijzigt de instelling");
        number.Checked = false;
        Check(!settings.SamenvoegenNummer, "KLIC-keuze is omkeerbaar");
        form.Close();
    }

    private static void CheckNestedEditors()
    {
        var settings = new LegendSettings();
        settings.ManualEntries.Add(new ManualEntry { Layer = "Bron", Description = "Oud" });
        int loads = 0;
        using var parent = new SettingsDialog(settings, "Test", compositionSource: _ => { loads++; return new[] { new EntryCheckItem("key", "Data", "DATA") }; });
        OpenOffscreen(parent);
        Check(loads == 0, "Instellingen analyseert pas bij openen Samenstellen");
        Descendants(parent).OfType<TabControl>().Single().SelectedIndex = 1;
        Application.DoEvents();
        InModal("NLCS Legenda – samenstellen", dialog =>
        {
            var grid = Descendants(dialog).OfType<DataGridView>().Single();
            grid.Rows[0].Cells[2].Value = "Nieuw";
            grid.EndEdit();
            Descendants(dialog).OfType<Button>().Single(b => b.Text == "Toepassen").PerformClick();
            dialog.DialogResult = DialogResult.Cancel;
            dialog.Close();
        }, () => Descendants(parent).OfType<Button>().First(b => b.Visible && b.Text.StartsWith("Samenstellen")).PerformClick());
        Check(loads == 1 && settings.ManualEntries.Single().Description == "Nieuw", "onderliggende Samenstellen past werkkopie toe");
        var tabs = Descendants(parent).OfType<TabControl>().Single();
        tabs.SelectedIndex = 3;
        InModal("NLCS Legenda – omschrijvingen", dialog =>
        {
            var grid = Descendants(dialog).OfType<DataGridView>().Single();
            Check(grid.Rows.Count > 10, "standaardomschrijvingen zijn zichtbaar");
            grid.Rows[0].Cells[2].Value = "Eigen omschrijving";
            grid.EndEdit();
            Descendants(dialog).OfType<Button>().Single(b => b.Text == "Toepassen").PerformClick();
            dialog.DialogResult = DialogResult.Cancel;
            dialog.Close();
        }, () => Descendants(parent).OfType<Button>().First(b => b.Visible && b.Text.StartsWith("Omschrijvingen")).PerformClick());
        Check(settings.DescriptionOverrides.Elementen.Count == 1, "omschrijvingen bewaart alleen gewijzigde tekst");
        parent.Close();
        using var custom = new CustomLayerDialog(new[] { new CustomLayerRule { Layer = "Bron", Element = "DATA", Description = "Oud" } });
        OpenOffscreen(custom);
        InModal("Eigen laag", dialog =>
        {
            CheckButtons(dialog);
            Descendants(dialog).OfType<TextBox>().Single(t => t.Text == "Oud").Text = "Gewijzigd";
            Descendants(dialog).OfType<Button>().Single(b => b.Text == "OK").PerformClick();
        }, () => Descendants(custom).OfType<Button>().Single(b => b.Text.StartsWith("Bewerken")).PerformClick());
        Check(custom.Result.Single().Description == "Gewijzigd", "bestaande laag bewerken zonder valse duplicaatmelding");
        custom.Close();
        using var remarks = new RemarksEditDialog("Kop", "\nRegel\n\n");
        Check(remarks.RemarksText == "\nRegel\n\n", "bewuste lege regels blijven behouden");
    }

    private static void InModal(string title, Action<Form> inspect, Action open)
    {
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        bool seen = false;
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == title);
            if (dialog is null) return;
            timer.Stop();
            seen = true;
            if (!DiagnosticRun.Execute(() => inspect(dialog), Errors.Add, title))
            { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
        };
        timer.Start();
        open();
        timer.Stop();
        Check(seen, "onderliggend venster geopend: " + title);
    }

    private static void CheckKlic(string? output)
    {
        Check(KlicDetailsDialog.ResolveAttachmentPath(@"..\bijlagen\info.pdf", @"C:\bron\dwg\klic.dwg") == @"C:\bron\bijlagen\info.pdf", "bijlagepad volgt brontekening");
        foreach (string path in new[] { @"C:info.pdf", @"\info.pdf", "https://example.invalid/file.pdf", "script.exe", @"file.pdf:stream.pdf" })
        {
            bool rejected = false;
            try { KlicDetailsDialog.ResolveAttachmentPath(path, @"C:\bron\klic.dwg"); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "onbruikbaar bijlagepad geweigerd: " + path);
        }
        var original = new KlicOptions();
        foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            using var options = new KlicOptionsDialog(original);
            OpenOffscreen(options);
            options.AutoScaleMode = AutoScaleMode.None;
            options.Scale(new SizeF(scale, scale));
            options.Font = new Font(options.Font.FontFamily, 9f * scale);
            options.MinimumSize = new Size(480, 340);
            options.ClientSize = new Size(720, 540);
            var tabs = Descendants(options).OfType<TabControl>().Single();
            foreach (TabPage page in tabs.TabPages)
            {
                tabs.SelectedTab = page; Application.DoEvents(); CheckButtons(options);
                foreach (var list in Descendants(page).OfType<CheckedListBox>())
                {
                    Check(list.Height > list.ItemHeight * 3, "KLIC-velden bereikbaar bij vergroting");
                    list.TopIndex = list.Items.Count - 1;
                    list.SetItemChecked(list.Items.Count - 1, true);
                    Check(list.CheckedItems.Cast<KlicField>().Any(f => f.Key == "Disclaimer"), "laatste KLIC-veld te kiezen");
                    list.TopIndex = 0;
                }
                if (output is not null)
                {
                    Directory.CreateDirectory(output);
                    using var image = new Bitmap(options.Width, options.Height);
                    options.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Join(output, $"klic-{tabs.SelectedIndex}-{scale}.png"));
                }
            }
            options.DialogResult = DialogResult.Cancel; options.Close();
            Check(original.GroupFields.Count == 0 && original.Columns.Count == 3, "annuleren wijzigt KLIC-keuzes niet");
        }
        using var edit = new KlicOptionsDialog(original);
        OpenOffscreen(edit);
        var lists = Descendants(edit).OfType<CheckedListBox>().ToArray();
        foreach (var list in lists)
        {
            for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, ((KlicField)list.Items[i]).Key == "DiameterM");
        }
        Descendants(edit).OfType<ComboBox>().Single().SelectedIndex = (int)KlicSource.PropertySets;
        edit.DialogResult = DialogResult.OK; edit.Close();
        Check(edit.Result.GroupFields.SequenceEqual(new[] { "DiameterM" }) && edit.Result.PreferredSource == KlicSource.PropertySets,
            "KLIC-keuzes bevestigd");
        var entry = new LegendEntry { Status = NlcsStatus.Bestaand, Discipline = "WE", Hoofdgroep = "KL", Element = "WATER", Description = "Water",
            KlicObjects = new[] { new KlicObjectData("bron.dwg", "AB", "XREF", new[] {
            new KlicRecord(KlicSource.InfraCAD, "KLIC_LeidingLijn", new Dictionary<string,string> { ["Materiaal"] = "PE" }),
            new KlicRecord(KlicSource.PropertySets, "KLIC_LeidingLijn", new Dictionary<string,string> { ["Materiaal"] = "PVC" })
        }, Array.Empty<string>(), KlicSource.PropertySets) } };
        var legend = new OverviewLegend { Id = "A", Name = "KLIC", Settings = new() { SortMode = LegendSortMode.Handmatig }, Entries = new[] { entry, Entry("Leeg") } };
        using var overview = new OverviewDialog(new[] { legend }, _ => null);
        OpenOffscreen(overview);
        var grid = Descendants(overview).OfType<DataGridView>().Single();
        var search = Descendants(overview).OfType<TextBox>().Single();
        search.Text = "PVC";
        Check(grid.Rows.Count == 1, "KLIC-waarde filtert overzicht");
        Check(!Descendants(overview).OfType<Button>().Single(b => b.Text == "Naar boven").Enabled, "verplaatsen bij filter uitgeschakeld");
        search.Clear();
        Descendants(overview).OfType<CheckBox>().Single().Checked = true;
        Check(grid.Rows.Count == 1, "bronverschillen filtert overzicht");
        using var details = new KlicDetailsDialog(entry);
        OpenOffscreen(details);
        var detailTabs = Descendants(details).OfType<TabControl>().Single();
        var values = detailTabs.TabPages[0].Controls.OfType<DataGridView>().Single();
        Check(values.Rows.Count == 2 && values.Rows.Cast<DataGridViewRow>().Count(r => (string)r.Cells[4].Value == "Ja") == 1,
            "beide bronwaarden en gekozen bron zichtbaar");
        details.Close(); overview.Close();
    }

    private static void CheckOverviewLoading()
    {
        var a = new OverviewLegend { Id = "A", Name = "A", Settings = new(), Entries = Array.Empty<LegendEntry>(), NeedsAnalysis = true };
        var b = new OverviewLegend { Id = "B", Name = "B", Settings = new(), Entries = Array.Empty<LegendEntry>(), NeedsAnalysis = true };
        var calls = new List<string>();
        bool failB = true;
        using var form = new OverviewDialog(new[] { a, b }, _ => null, item =>
        {
            calls.Add(item.Id);
            if (item.Id == "B" && failB) throw new InvalidOperationException("Xref niet beschikbaar");
            return new[] { Entry(item.Name) };
        });
        OpenOffscreen(form);
        var controls = Descendants(form).ToList();
        var legends = controls.OfType<ComboBox>().Single(c => c.Items.OfType<OverviewLegend>().Any());
        var grid = controls.OfType<DataGridView>().Single();
        Check(calls.SequenceEqual(new[] { "A" }), "Overzicht leest bij openen alleen de gekozen legenda");
        Check(grid.ColumnHeadersHeight >= grid.Font.Height + grid.DefaultCellStyle.Padding.Vertical + 2,
            "Kolomkoppen hebben ruimte voor tekst en padding");
        Check(grid.Columns.Cast<DataGridViewColumn>().Where(c => c.HeaderText is "Netbeheerder" or "Samengevoegd" or "Broncontrole").All(c => !c.Visible),
            "Lege informatiekolommen worden verborgen; gekozen KLIC-velden blijven staan");
        legends.SelectedIndex = 1;
        Check(grid.Rows.Count == 0 && b.NeedsAnalysis && controls.OfType<Label>().Any(l => l.Text.Contains("Xref niet beschikbaar")),
            "Mislukte bronlezing toont een melding zonder oude regels");
        Check(!controls.OfType<Button>().Single(c => c.Text == "Exporteer lijst…").Enabled,
            "Mislukte bronlezing kan geen oude lijst exporteren");
        legends.SelectedIndex = 0;
        Check(calls.SequenceEqual(new[] { "A", "B" }) && grid.Rows.Count == 1,
            "Andere legenda blijft bereikbaar zonder herhaalde analyse");
        failB = false;
        legends.SelectedIndex = 1;
        Check(!b.NeedsAnalysis && grid.Rows.Count == 1, "Bronlezing opnieuw proberen na fout");
        var sort = controls.OfType<ComboBox>().Single(c => c.Items.Contains("Handmatig"));
        sort.SelectedItem = "Handmatig";
        controls.OfType<Button>().Single(c => c.Text == "Vernieuwen").PerformClick();
        Check(calls.SequenceEqual(new[] { "A", "B", "B", "B" }) && b.Changed && b.Settings.SortMode == LegendSortMode.Handmatig,
            "Vernieuwen leest opnieuw en bewaart nog niet opgeslagen keuzes");
        form.Close();
    }

    private static void CheckOverview()
    {
        var entries = new[] { Entry("A"), Entry("B"), Entry("C") };
        var a = new OverviewLegend { Id = "A", Name = "Legenda A", Settings = new(), Entries = entries };
        var b = new OverviewLegend { Id = "B", Name = "Legenda B", Settings = new(), Entries = entries };
        int saves = 0;
        using var form = new OverviewDialog(new[] { a, b }, changes => { Check(changes.Count == 1 && changes[0].Id == "A", "alleen gewijzigde legenda opslaan"); saves++; return null; });
        OpenOffscreen(form);
        var controls = Descendants(form).ToList();
        var sort = controls.OfType<ComboBox>().Single(c => c.Items.Contains("Handmatig"));
        sort.SelectedItem = "Handmatig";
        var grid = controls.OfType<DataGridView>().Single();
        grid.CurrentCell = grid.Rows[2].Cells[1];
        controls.OfType<Button>().Single(c => c.Text == "Naar begin").PerformClick();
        Check((string)grid.Rows[0].Cells[1].Value == "C", "regel naar begin verplaatsen");
        var legends = controls.OfType<ComboBox>().Single(c => c.Items.OfType<OverviewLegend>().Any());
        legends.SelectedIndex = 1;
        Check((string)grid.Rows[0].Cells[1].Value == "A" && !b.Changed, "andere legenda blijft gelijk");
        legends.SelectedIndex = 0;
        Check((string)grid.Rows[0].Cells[1].Value == "C", "volgorde blijft bij wisselen");
        controls.OfType<Button>().Single(c => c.Text == "Toepassen").PerformClick();
        Check(saves == 1 && !a.Changed, "Toepassen slaat eenmaal op");
        CheckButtons(form);
        form.Close();
        var cancelled = new OverviewLegend { Id = "A", Name = "Annuleren", Settings = new(), Entries = entries };
        using var cancel = new OverviewDialog(new[] { cancelled }, _ => { saves++; return null; });
        OpenOffscreen(cancel);
        Descendants(cancel).OfType<ComboBox>().Single(c => c.Items.Contains("Handmatig")).SelectedItem = "Handmatig";
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Close();
        Check(saves == 1, "Annuleren schrijft niets");
    }
    private static LegendEntry Entry(string name) => new() { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "KL", Element = name, Description = name };
    private static void CheckButtons(Form form)
    {
        foreach (var button in Descendants(form).OfType<Button>().Where(b => b.Visible))
        {
            for (Control? parent = button.Parent; parent is not null; parent = parent.Parent)
                if (parent is ScrollableControl { AutoScroll: true } scroll) scroll.ScrollControlIntoView(button);
            Application.DoEvents();
            var bounds = form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
            Check(form.ClientRectangle.Contains(bounds), form.Text + ": knop bereikbaar: " + button.Text + " " + bounds + " binnen " + form.ClientRectangle);
            Check(button.ClientSize.Width >= TextRenderer.MeasureText(button.Text, button.Font).Width, form.Text + ": knoptekst past: " + button.Text);
        }
    }
    private static void OpenOffscreen(Form form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20000, -20000);
        form.Show();
        Application.DoEvents();
    }
    private static void Check(bool ok, string message) { _checks++; if (!ok) Errors.Add(message); }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach(var nested in Descendants(child)) yield return nested;
        }
    }
}
