using System.Collections.Generic;
using Godot;

// Renders octree slice cells as flat quads on the Z=0 plane.
public partial class OctreeRenderer : Node3D
{
    private MeshInstance3D _meshInst;
    private ImmediateMesh _mesh;

    public override void _Ready()
    {
        _mesh = new ImmediateMesh();

        _meshInst = new MeshInstance3D();
        _meshInst.Mesh = _mesh;

        var mat = new StandardMaterial3D();
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.VertexColorUseAsAlbedo = true;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        _meshInst.MaterialOverride = mat;

        AddChild(_meshInst);
    }

    public void DrawSlice(List<Octree.Cell> cells, int depth, int maxDepth)
    {
        _mesh.ClearSurfaces();
        if (cells.Count == 0) return;

        float scale = 1 << (maxDepth - depth); // world units per cell edge

        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);

        foreach (var cell in cells)
        {
            float wx = cell.Coord.X * scale;
            float wy = cell.Coord.Y * scale;

            // Blue (coarse) -> green -> red (fine)
            float t = (float)(depth - 1) / (maxDepth - 1);
            var color = new Color(t, 1f - Mathf.Abs(t - 0.5f) * 2f, 1f - t, 0.9f);

            Quad(wx, wy, scale, color);
        }

        _mesh.SurfaceEnd();
    }

    private void Quad(float x, float y, float size, Color color)
    {
        float pad = size * 0.05f;
        float x1 = x + pad, x2 = x + size - pad;
        float y1 = y + pad, y2 = y + size - pad;

        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(new Vector3(x1, y1, 0));
        _mesh.SurfaceAddVertex(new Vector3(x2, y1, 0));
        _mesh.SurfaceAddVertex(new Vector3(x2, y2, 0));

        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(new Vector3(x1, y1, 0));
        _mesh.SurfaceAddVertex(new Vector3(x2, y2, 0));
        _mesh.SurfaceAddVertex(new Vector3(x1, y2, 0));
    }
}
