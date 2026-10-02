using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

// TreeView met drie-standen-vinkjes voor het samenstellen: een groep kan in één klik aan of
// uit, de groepsstand (aan/uit/gedeeltelijk) volgt uit de kinderen. WinForms-checkboxes zijn
// maar twee standen, dus de derde stand (gedeeltelijk) tekenen we zelf via StateImageList.
internal sealed class TriStateTree : TreeView
{
    private CompositionTree _model = new();

    public TriStateTree()
    {
        CheckBoxes = false;
        HideSelection = false;
        ShowRootLines = true;
        ShowPlusMinus = true;
        FullRowSelect = false;
        StateImageList = BuildStateImages();
    }

    public void SetModel(CompositionTree model, string? filter = null)
    {
        _model = model;
        BeginUpdate();
        Nodes.Clear();
        foreach (var group in _model.Filter(filter))
        {
            var gnode = new TreeNode(group.Label) { Tag = group };
            foreach (var leaf in group.Leaves)
                gnode.Nodes.Add(new TreeNode(leaf.Label) { Tag = leaf });
            Nodes.Add(gnode);
            gnode.Expand();
        }
        RefreshStates();
        EndUpdate();
    }

    public CompositionTree Model => _model;

    protected override void OnNodeMouseClick(TreeNodeMouseClickEventArgs e)
    {
        base.OnNodeMouseClick(e);
        var hit = HitTest(e.Location);
        if (hit.Location == TreeViewHitTestLocations.StateImage)
            Toggle(e.Node);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space && SelectedNode is not null)
        {
            Toggle(SelectedNode);
            e.Handled = true;
        }
    }

    private void Toggle(TreeNode node)
    {
        switch (node.Tag)
        {
            case CompositionTree.Group g:
                _model.SetGroup(g, g.State != TriState.On);
                break;
            case CompositionTree.Leaf l:
                l.Included = !l.Included;
                break;
            default:
                return;
        }
        RefreshStates();
    }

    private void RefreshStates()
    {
        foreach (TreeNode gnode in Nodes)
        {
            if (gnode.Tag is CompositionTree.Group g)
                gnode.StateImageIndex = g.State switch
                {
                    TriState.On => 1,
                    TriState.Partial => 2,
                    _ => 0
                };
            foreach (TreeNode cnode in gnode.Nodes)
                if (cnode.Tag is CompositionTree.Leaf l)
                    cnode.StateImageIndex = l.Included ? 1 : 0;
        }
    }

    // 0 = uit, 1 = aan, 2 = gedeeltelijk. Getekend met de systeem-checkbox-stijl zodat het
    // meeschaalt met het thema.
    private static ImageList BuildStateImages()
    {
        var images = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
        images.Images.Add(Glyph(System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal, false));
        images.Images.Add(Glyph(System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal, false));
        images.Images.Add(Glyph(System.Windows.Forms.VisualStyles.CheckBoxState.MixedNormal, true));
        return images;
    }

    private static Bitmap Glyph(System.Windows.Forms.VisualStyles.CheckBoxState state, bool mixed)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        var loc = new Point(0, 0);
        try
        {
            CheckBoxRenderer.DrawCheckBox(g, loc, state);
        }
        catch
        {
            // Zonder visual styles: eenvoudige vervanging zodat de boom bruikbaar blijft.
            g.DrawRectangle(Pens.Gray, 1, 1, 12, 12);
            if (state == System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal)
                g.DrawString("x", SystemFonts.DefaultFont, Brushes.Black, 2, 0);
            else if (mixed)
                g.FillRectangle(Brushes.Gray, 4, 4, 6, 6);
        }
        return bmp;
    }
}
