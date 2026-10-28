using System.Security.Cryptography;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;

namespace NlcsLegenda.Plugin;

internal static class LegendLinetypes
{
    public static bool IsLocal(Database db, Transaction tr, ObjectId id)
    {
        if (id.IsNull || id.Database != db || id.OriginalDatabase != db) return false;
        var record = (LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead);
        if (record.IsDependent) return false;
        for (int i = 0; i < record.NumDashes; i++)
        {
            var style = record.ShapeStyleAt(i);
            if (!style.IsNull && !IsLocalStyle(db, tr, style)) return false;
        }
        return true;
    }

    public static ObjectId Resolve(Database db, Transaction tr, ObjectId id)
    {
        if (IsLocal(db, tr, id)) return id;
        var source = (LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead);
        string signature = DrawingAnalyzer.LinetypeSignature(id, tr);
        var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        string name = "NLCS_LT_" + Hash(signature), candidate = name;
        for (int suffix = 2; table.Has(candidate); suffix++)
        {
            var existing = table[candidate];
            if (IsLocal(db, tr, existing) && DrawingAnalyzer.LinetypeSignature(existing, tr) == signature) return existing;
            candidate = name + "_" + suffix;
        }
        // een kloon kan de xref-vlag behouden
        using var copy = new LinetypeTableRecord { Name = candidate, AsciiDescription = source.AsciiDescription,
            PatternLength = source.PatternLength, IsScaledToFit = source.IsScaledToFit, NumDashes = source.NumDashes };
        for (int i = 0; i < source.NumDashes; i++)
        {
            copy.SetDashLengthAt(i, source.DashLengthAt(i));
            if (source.ShapeStyleAt(i).IsNull) continue;
            copy.SetShapeStyleAt(i, ResolveStyle(db, tr, source.ShapeStyleAt(i)));
            copy.SetShapeNumberAt(i, source.ShapeNumberAt(i));
            if (!((TextStyleTableRecord)tr.GetObject(source.ShapeStyleAt(i), OpenMode.ForRead)).IsShapeFile)
                copy.SetTextAt(i, source.TextAt(i));
            copy.SetShapeScaleAt(i, source.ShapeScaleAt(i)); copy.SetShapeRotationAt(i, source.ShapeRotationAt(i));
            copy.SetShapeOffsetAt(i, source.ShapeOffsetAt(i));
            copy.SetShapeIsUcsOrientedAt(i, source.ShapeIsUcsOrientedAt(i));
            copy.SetShapeIsUprightAt(i, source.ShapeIsUprightAt(i));
        }
        table.UpgradeOpen(); table.Add(copy); tr.AddNewlyCreatedDBObject(copy, true);
        if (!IsLocal(db, tr, copy.ObjectId) || DrawingAnalyzer.LinetypeSignature(copy.ObjectId, tr) != signature)
            throw new InvalidOperationException("Het lijnpatroon kon niet zelfstandig worden overgenomen.");
        return copy.ObjectId;
    }

    private static ObjectId ResolveStyle(Database db, Transaction tr, ObjectId id)
    {
        if (IsLocalStyle(db, tr, id)) return id;
        var source = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
        string signature = DrawingAnalyzer.TextStyleContent(source);
        var table = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        if (source.IsShapeFile)
            foreach (ObjectId existing in table)
                if (IsLocalStyle(db, tr, existing) && DrawingAnalyzer.TextStyleContent((TextStyleTableRecord)tr.GetObject(existing, OpenMode.ForRead)) == signature)
                    return existing;
        string name = "NLCS_LT_FONT_" + Hash(signature), candidate = name;
        for (int suffix = 2; table.Has(candidate); suffix++)
        {
            var existing = table[candidate];
            if (IsLocalStyle(db, tr, existing) && DrawingAnalyzer.TextStyleContent((TextStyleTableRecord)tr.GetObject(existing, OpenMode.ForRead)) == signature)
                return existing;
            candidate = name + "_" + suffix;
        }
        using var copy = new TextStyleTableRecord { Name = candidate, IsShapeFile = source.IsShapeFile,
            IsVertical = source.IsVertical, TextSize = source.TextSize, XScale = source.XScale,
            ObliquingAngle = source.ObliquingAngle, FlagBits = source.FlagBits, PriorSize = source.PriorSize };
        if (!source.IsShapeFile && !string.IsNullOrEmpty(source.Font.TypeFace)) copy.Font = source.Font;
        copy.FileName = source.FileName; copy.BigFontFileName = source.BigFontFileName;
        if (source.IsShapeFile)
        {
            // één shaperegistratie per bestand, ook bij al geladen xrefs
            using var staging = new Database(true, true);
            ObjectId staged;
            using (var stage = staging.TransactionManager.StartTransaction())
            {
                var styles = (TextStyleTable)stage.GetObject(staging.TextStyleTableId, OpenMode.ForWrite);
                staged = styles.Add(copy); stage.AddNewlyCreatedDBObject(copy, true); stage.Commit();
            }
            using var mapping = new IdMapping();
            using var objects = new ObjectIdCollection { staged };
            staging.WblockCloneObjects(objects, db.TextStyleTableId, mapping, DuplicateRecordCloning.Replace, false);
            var result = mapping[staged].Value;
            if (!IsLocalStyle(db, tr, result) || DrawingAnalyzer.TextStyleContent((TextStyleTableRecord)tr.GetObject(result, OpenMode.ForRead)) != signature)
                throw new InvalidOperationException("Shapestijl niet zelfstandig gekopieerd.");
            return result;
        }
        table.UpgradeOpen(); table.Add(copy); tr.AddNewlyCreatedDBObject(copy, true);
        if (DrawingAnalyzer.TextStyleContent(copy) != signature)
            throw new InvalidOperationException("De tekst- of shapestijl van het lijnpatroon kon niet worden overgenomen.");
        return copy.ObjectId;
    }

    private static bool IsLocalStyle(Database db, Transaction tr, ObjectId id) =>
        id.Database == db && id.OriginalDatabase == db && tr.GetObject(id, OpenMode.ForRead) is TextStyleTableRecord { IsDependent: false };

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..20];
}
