using System.IO;
using System.Security.Cryptography;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    // De privébron blijft buiten de repository. Deze aanvullende acceptatieproef leest
    // de werkelijke DWT; een ontbrekende bron is een fout, nooit een geslaagde controle.
    [CommandMethod("NLCSLEGENDATEMPLATEFILETEST", CommandFlags.Modal)]
    public void NlcsLegendaTemplateFileTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        try
        {
            string path = Environment.GetEnvironmentVariable("NLCS_TEMPLATE_REFERENCE") ?? string.Empty;
            if (!File.Exists(path)) throw new InvalidOperationException("Stel NLCS_TEMPLATE_REFERENCE in op de referentie-DWT.");
            string Hash() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var before = Hash();
            using var source = new Database(false, true);
            source.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            source.CloseInput(true);
            using var sourceTr = source.TransactionManager.StartTransaction();
            var model = (BlockTableRecord)sourceTr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(source), OpenMode.ForRead);
            var texts = model.Cast<ObjectId>().Select(id => sourceTr.GetObject(id, OpenMode.ForRead)).OfType<DBText>().ToArray();
            void Near(double actual, double expected, string what, double tolerance = 0.002)
            {
                if (Math.Abs(actual - expected) > tolerance)
                    throw new InvalidOperationException($"{what}: {actual:R}; DWT: {expected:R}.");
            }
            foreach (double scale in new[] { 200d, 500d, 1000d })
            {
                double unit = scale / 1000;
                var title = texts.Where(t => t.TextString == "%%ULEGENDA" && Math.Abs(t.Height / unit - 5) < 0.001).OrderBy(t => t.Position.X).First();
                var description = texts.Where(t => t.TextString == "Geometrie" && Math.Abs(t.Height / unit - 2.5) < 0.001
                    && t.Position.X > title.Position.X).OrderBy(t => t.Position.X).First();
                var header = texts.Where(t => t.TextString == "%%UBestaande situatie:" && Math.Abs(t.Position.X - title.Position.X) < 0.002).OrderByDescending(t => t.Position.Y).First();
                using var tr = doc.Database.TransactionManager.StartTransaction();
                var settings = new LegendSettings { Scale = scale };
                var entry = new LegendEntry { Status = NlcsStatus.Bestaand, Discipline = "WE", Hoofdgroep = "VH", Element = "GEOMETRIE", Description = "Geometrie",
                    LayersByType = new() { [NlcsDrawType.Geometrie] = "TEMPLATE_FILE_LINE" } };
                var id = LegendBuilder.BuildBlock(doc.Database, tr, new AnalysisResult { Entries = new[] { entry } }, settings, out _, out _);
                var drawn = ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>().Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Entity>().ToArray();
                var actualTitle = drawn.OfType<MText>().Single(t => t.Text == "LEGENDA");
                var actualHeader = drawn.OfType<MText>().Single(t => t.Text == "Bestaande situatie:");
                var actualDescription = drawn.OfType<MText>().Single(t => t.Text == "Geometrie");
                Near(actualTitle.TextHeight / unit, title.Height / unit, "Titelhoogte");
                Near(actualHeader.TextHeight / unit, header.Height / unit, "Statushoogte");
                Near(actualDescription.TextHeight / unit, description.Height / unit, "Omschrijvinghoogte");
                Near((actualDescription.Location.X - actualDescription.TextHeight * 0.1) / unit,
                    (description.Position.X - title.Position.X) / unit, "Tekstkolom");
                Near((actualTitle.Location.Y - actualTitle.TextHeight - actualHeader.Location.Y + actualHeader.TextHeight) / unit,
                    (title.Position.Y - header.Position.Y) / unit, "Titel-status");
                Near((actualTitle.Location.Y - actualTitle.TextHeight - actualDescription.Location.Y + actualDescription.TextHeight) / unit,
                    (title.Position.Y - description.Position.Y) / unit, "Titel-omschrijving");
                var sourceStyle = (TextStyleTableRecord)sourceTr.GetObject(description.TextStyleId, OpenMode.ForRead);
                var actualStyle = (TextStyleTableRecord)tr.GetObject(actualDescription.TextStyleId, OpenMode.ForRead);
                if (!Path.GetFileName(actualStyle.FileName).Equals(Path.GetFileName(sourceStyle.FileName), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Lettertype verschilt van de DWT.");
                Near(actualStyle.XScale, sourceStyle.XScale, "Breedtefactor");
                tr.Abort();
            }
            // Gewone meerregelige omschrijvingen, zonder een lokale alinea-opmaakcode.
            var wrapping = model.Cast<ObjectId>().Select(id => sourceTr.GetObject(id, OpenMode.ForRead)).OfType<MText>()
                .Where(t => t.Text.StartsWith("Scheidingsband 100/300x100mm", StringComparison.Ordinal) && Math.Abs(t.TextHeight - 0.5) < 1e-8).First();
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var settings = new LegendSettings { Scale = 200 };
                settings.ColumnWidthMm = settings.SwatchWidthMm + settings.TextGapMm + 0.25 + wrapping.Width / 0.2;
                var entry = new LegendEntry { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "VH", Element = "BAND", Description = wrapping.Text };
                var id = LegendBuilder.BuildBlock(doc.Database, tr, new AnalysisResult { Entries = new[] { entry } }, settings, out _, out _);
                var actual = ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>().Select(i => tr.GetObject(i, OpenMode.ForRead))
                    .OfType<MText>().Single(t => t.Text == wrapping.Text);
                Near(actual.LineSpacingFactor, wrapping.LineSpacingFactor, "Regelafstandfactor", 1e-9);
                Near(actual.Width, wrapping.Width, "Afbreekbreedte", 1e-9);
                Near(actual.ActualHeight / 0.2, wrapping.ActualHeight / 0.2, "Meerregelige teksthoogte");
                tr.Abort();
            }
            sourceTr.Abort();
            if (before != Hash()) throw new InvalidOperationException("Referentietemplate gewijzigd.");
            doc.Editor.WriteMessage("\nTEMPLATEFILE echte DWT: tekst, stijl, posities op drie schalen en meerregelige omschrijving -> OK");
        }
        catch (System.Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or Autodesk.AutoCAD.Runtime.Exception)
        { doc.Editor.WriteMessage($"\nTEMPLATEFILE FAIL: {ex}"); }
    }
}
