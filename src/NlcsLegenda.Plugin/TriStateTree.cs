using System.Drawing;
using System.Windows.Forms;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class TriStateTree : TreeView
{
    private CompositionTree _model = new();

    public TriStateTree()
    {
        CheckBoxes = false;
        HideSelection = false;
        ShowRootLines = true;
        ShowNodeToolTips = true;
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
            foreach (var leaf in _model.VisibleLeaves(group, filter))
                gnode.Nodes.Add(new TreeNode(leaf.Label) { Tag = leaf, ToolTipText = leaf.SearchText });
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
        if (e.Node is not null && hit.Location == TreeViewHitTestLocations.StateImage)
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
            case CompositionTree.Group:
                var visible = node.Nodes.Cast<TreeNode>().Select(n => n.Tag).OfType<CompositionTree.Leaf>().ToArray();
                bool include = visible.Any(l => !l.Included);
                foreach (var leaf in visible) leaf.Included = include;
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
            if (gnode.Tag is CompositionTree.Group)
            {
                var leaves = gnode.Nodes.Cast<TreeNode>().Select(n => n.Tag).OfType<CompositionTree.Leaf>().ToArray();
                int count = leaves.Count(l => l.Included);
                gnode.StateImageIndex = count == 0 ? 0 : count == leaves.Length ? 1 : 2;
            }
            foreach (TreeNode cnode in gnode.Nodes)
                if (cnode.Tag is CompositionTree.Leaf l)
                    cnode.StateImageIndex = l.Included ? 1 : 0;
        }
    }

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
            g.DrawRectangle(Pens.Gray, 1, 1, 12, 12);
            if (state == System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal)
                g.DrawString("x", SystemFonts.DefaultFont, Brushes.Black, 2, 0);
            else if (mixed)
                g.FillRectangle(Brushes.Gray, 4, 4, 6, 6);
        }
        return bmp;
    }
}
