using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    private enum UpdateResult { Updated, NoEntries, Failed }

    // Bouwt één beheerde legenda opnieuw op basis van zijn scope en instellingen. Erase en
    // opbouw gebeuren in dezelfde transactie: mislukt de opbouw, dan blijft de oude staan.
    private static UpdateResult BuildManagedLegend(
        Database db, Transaction tr, LegendRegistry registry, LegendDefinition def,
        out int rows, out string note)
    {
        rows = 0;
        note = string.Empty;

        // Bron bepalen op basis van scope; eigen legenda-geometrie uitsluiten.
        var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
        ObjectId[]? selection = null;
        if (def.Scope == LegendScope.Selection)
        {
            selection = LegendManagement.ResolveHandles(db, def.SourceHandles, out var missing)
                .Where(id => !excluded.Contains(id)).ToArray();
            if (selection.Length == 0)
            {
                note = missing > 0
                    ? $"alle {missing} bronobjecten ontbreken"
                    : "geen bronobjecten meer";
                return UpdateResult.NoEntries;
            }
            if (missing > 0)
                note = $"{missing} bronobject(en) ontbreken";
        }

        var analysis = DrawingAnalyzer.Analyze(db, tr, def.Settings, selection, LoadCatalog(db), excluded);
        if (analysis.Entries.Count == 0)
            return UpdateResult.NoEntries;

        // Positie van de bestaande legenda vasthouden.
        Point3d topLeft = Point3d.Origin;
        bool hadGeometry = LegendManagement.TryEraseGroup(db, tr, def.GroupName, out topLeft);

        var btrId = LegendBuilder.BuildBlock(db, tr, analysis, def.Settings, out rows);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var br = new BlockReference(hadGeometry ? topLeft : ComputeInsertPoint(db, def.Settings), btrId);
        ms.AppendEntity(br);
        tr.AddNewlyCreatedDBObject(br, true);

        if (hadGeometry)
        {
            var ext = br.Bounds;
            if (ext.HasValue)
            {
                var shift = new Vector3d(topLeft.X - ext.Value.MinPoint.X, topLeft.Y - ext.Value.MaxPoint.Y, 0);
                if (!shift.IsZeroLength())
                    br.Position += shift;
            }
        }

        FinalizePlacement(tr, db, br, def.Settings, def.GroupName);
        _pendingPurge.Add(btrId);
        return UpdateResult.Updated;
    }

    private static readonly List<ObjectId> _pendingPurge = new();

    private static void PurgePending(Database db)
    {
        foreach (var id in _pendingPurge)
            PurgeTempBlock(db, id);
        _pendingPurge.Clear();
    }

    // Voert de eenmalige legacy-migratie uit binnen een bestaande transactie.
    private static void MaybeMigrate(Database db, Transaction tr, LegendRegistry registry)
    {
        try
        {
            LegendManagement.MigrateLegacyIfNeeded(db, tr, registry, LoadGlobalDefaults(), PluginVersion, out _);
        }
        catch
        {
            // Migratie faalt zacht; de tekening blijft bruikbaar.
        }
    }

    // Kiest de doel-legenda voor update/viewport/export: 0 = null, 1 = die ene, >1 = de
    // gebruiker klikt een legenda-object aan (of kiest uit een lijst als klikken faalt).
    private static LegendDefinition? ResolveTargetLegend(
        Editor ed, Database db, LegendRegistry registry, string actie)
    {
        if (registry.Legends.Count == 0)
            return null;
        if (registry.Legends.Count == 1)
            return registry.Legends[0];

        var peo = new PromptEntityOptions($"\nKlik een onderdeel van de legenda om te {actie} (of Enter voor een lijst)")
        {
            AllowNone = true
        };
        var per = ed.GetEntity(peo);
        if (per.Status == PromptStatus.OK)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var def = LegendManagement.FindLegendForEntity(db, tr, registry, per.ObjectId);
            tr.Commit();
            if (def is not null)
                return def;
            ed.WriteMessage("\nDat object hoort niet bij een beheerde legenda.");
        }

        return PickLegendFromList(ed, registry, actie);
    }

    private static LegendDefinition? PickLegendFromList(Editor ed, LegendRegistry registry, string actie)
    {
        ed.WriteMessage($"\nWelke legenda {actie}:");
        for (int i = 0; i < registry.Legends.Count; i++)
        {
            var l = registry.Legends[i];
            ed.WriteMessage($"\n  {i + 1}. {l.Name} ({l.Scope.ToDisplay()})");
        }
        var pio = new PromptIntegerOptions($"\nNummer (1-{registry.Legends.Count}, 0 = annuleren)")
        {
            LowerLimit = 0,
            UpperLimit = registry.Legends.Count,
            DefaultValue = 0,
            AllowNone = true
        };
        var r = ed.GetInteger(pio);
        if (r.Status != PromptStatus.OK || r.Value < 1)
            return null;
        return registry.Legends[r.Value - 1];
    }
}
