using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace NlcsLegenda.Plugin;

public sealed class LegendJig : EntityJig
{
    private Point3d _position;

    public LegendJig(BlockReference reference) : base(reference)
    {
        _position = reference.Position;
    }

    public Point3d Position => _position;

    protected override SamplerStatus Sampler(JigPrompts prompts)
    {
        var options = new JigPromptPointOptions("\nPlaats legenda (basispunt): ")
        {
            UserInputControls = UserInputControls.Accept3dCoordinates
                                | UserInputControls.NoNegativeResponseAccepted
                                | UserInputControls.GovernedByOrthoMode
        };

        var result = prompts.AcquirePoint(options);
        if (result.Status != PromptStatus.OK)
            return SamplerStatus.Cancel;

        if (result.Value.IsEqualTo(_position))
            return SamplerStatus.NoChange;

        _position = result.Value;
        return SamplerStatus.OK;
    }

    protected override bool Update()
    {
        ((BlockReference)Entity).Position = _position;
        return true;
    }
}
