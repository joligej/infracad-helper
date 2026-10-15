using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDATEMPLATESTYLETEST", CommandFlags.Modal)]
    public void NlcsLegendaTemplateStyleTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var styles = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForWrite);
            TextStyleTableRecord conflicting;
            if (styles.Has("NLCS-ISO")) conflicting = (TextStyleTableRecord)tr.GetObject(styles["NLCS-ISO"], OpenMode.ForWrite);
            else
            {
                conflicting = new TextStyleTableRecord { Name = "NLCS-ISO" };
                styles.Add(conflicting); tr.AddNewlyCreatedDBObject(conflicting, true);
            }
            conflicting.FileName = "arial.ttf";
            conflicting.XScale = 0.6;
            conflicting.ObliquingAngle = 0.2;
            var analysis = new AnalysisResult { Entries = new[] { new LegendEntry
                { Status = NlcsStatus.Bestaand, Discipline = "WE", Hoofdgroep = "VH", Element = "TEST", Description = "Geometrie" } } };
            MText Body(LegendSettings settings)
            {
                var id = LegendBuilder.BuildBlock(db, tr, analysis, settings, out _, out _);
                return ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<MText>().Single(t => t.Text == "Geometrie");
            }
            var first = Body(new LegendSettings());
            var generated = (TextStyleTableRecord)tr.GetObject(first.TextStyleId, OpenMode.ForRead);
            if (!Path.GetFileName(generated.FileName).Equals("NLCS-ISO.ttf", StringComparison.OrdinalIgnoreCase)
                || Math.Abs(generated.XScale - 1) > 1e-9 || Math.Abs(generated.ObliquingAngle) > 1e-9
                || first.TextStyleId == conflicting.ObjectId)
                throw new InvalidOperationException("De standaard neemt de afwijkende hoststijl over.");
            if (Body(new LegendSettings()).TextStyleId != first.TextStyleId)
                throw new InvalidOperationException("Opnieuw bouwen maakt onnodig een extra tekststijl.");
            if (conflicting.FileName != "arial.ttf" || Math.Abs(conflicting.XScale - 0.6) > 1e-9
                || Math.Abs(conflicting.ObliquingAngle - 0.2) > 1e-9)
                throw new InvalidOperationException("Bestaande tekststijl gewijzigd.");
            if (Body(new LegendSettings { TextStyle = "NLCS-ISO" }).TextStyleId != conflicting.ObjectId)
                throw new InvalidOperationException("Expliciet gekozen tekststijl genegeerd.");
            bool rejected = false;
            try { Body(new LegendSettings { TextStyle = "NIET_BESTAANDE_TESTSTIJL" }); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("ontbreekt in deze tekening", StringComparison.Ordinal)) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Ontbrekende gekozen tekststijl stil vervangen.");
            var custom = new LegendSettings { TitleTextHeightMm = 50, HeaderTextHeightMm = 20, HeaderSpacingMm = 0 };
            var customId = LegendBuilder.BuildBlock(db, tr, analysis, custom, out _, out _);
            var customTexts = ((BlockTableRecord)tr.GetObject(customId, OpenMode.ForRead)).Cast<ObjectId>()
                .Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<MText>().ToArray();
            var title = customTexts.Single(t => t.Text == "LEGENDA");
            var header = customTexts.Single(t => t.Text == "Bestaande situatie:");
            var notesTitle = customTexts.Single(t => t.Text == "OPMERKINGEN");
            var notesBody = customTexts.Single(t => t.Text.Contains("Maten in meters", StringComparison.Ordinal));
            if (header.GeometricExtents.MaxPoint.Y >= title.GeometricExtents.MinPoint.Y
                || notesBody.GeometricExtents.MaxPoint.Y >= notesTitle.GeometricExtents.MinPoint.Y)
                throw new InvalidOperationException($"Kopafstand: titelonder={title.GeometricExtents.MinPoint.Y:R}, statusboven={header.GeometricExtents.MaxPoint.Y:R}; opmerkingenonder={notesTitle.GeometricExtents.MinPoint.Y:R}, tekstboven={notesBody.GeometricExtents.MaxPoint.Y:R}.");
            tr.Abort();
            doc.Editor.WriteMessage("\nTEMPLATESTYLE standaardfont, bestaande stijl behouden, eigen keuze en ontbrekende stijl -> OK");
        }
        catch (System.Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or Autodesk.AutoCAD.Runtime.Exception)
        { doc.Editor.WriteMessage($"\nTEMPLATESTYLE FAIL: {ex}"); }
    }
}
