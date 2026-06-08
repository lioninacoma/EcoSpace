using System.Collections.Generic;
using Godot;

// Minimal sparse octree. Leaf voxels stored at MaxDepth resolution.
// Upper depths are derived on-the-fly by checking if any child is filled.
public class Octree
{
    public struct Cell
    {
        public Vector3I Coord;
        public int Depth;
    }

    public int MaxDepth = 5; // grid is (2^MaxDepth)^3 = 32^3

    private HashSet<Vector3I> _filled = new();

    public int MaxZ => 1 << MaxDepth;

    public Octree()
    {
        SeedTestData();
    }

    private void SeedTestData()
    {
        int size = 1 << MaxDepth; // 32
        int half = size / 2;
        for (int x = 0; x < size; x++)
        for (int y = 0; y < size; y++)
        for (int z = 0; z < size; z++)
        {
            float cx = x - half, cy = y - half, cz = z - half;
            float r = Mathf.Sqrt(cx * cx + cy * cy + cz * cz);
            // Hollow sphere shell + a solid inner cube
            if ((r > 8 && r < 13) || (Mathf.Abs(cx) < 4 && Mathf.Abs(cy) < 4 && Mathf.Abs(cz) < 4))
                _filled.Add(new Vector3I(x, y, z));
        }
    }

    // Returns all filled cells visible at a given depth for a Z slice.
    // sliceZ is in MaxDepth voxel coordinates.
    public List<Cell> GetSlice(int depth, int sliceZ)
    {
        var result = new List<Cell>();
        int scale = 1 << (MaxDepth - depth); // how many MaxDepth voxels per cell edge
        int gridZ = sliceZ / scale;
        int gridSize = 1 << depth;

        for (int x = 0; x < gridSize; x++)
        for (int y = 0; y < gridSize; y++)
        {
            if (IsFilled(depth, new Vector3I(x, y, gridZ)))
                result.Add(new Cell { Coord = new Vector3I(x, y, gridZ), Depth = depth });
        }
        return result;
    }

    private bool IsFilled(int depth, Vector3I coord)
    {
        if (depth == MaxDepth)
            return _filled.Contains(coord);

        int scale = 1 << (MaxDepth - depth);
        Vector3I origin = coord * scale;
        for (int dx = 0; dx < scale; dx++)
        for (int dy = 0; dy < scale; dy++)
        for (int dz = 0; dz < scale; dz++)
        {
            if (_filled.Contains(origin + new Vector3I(dx, dy, dz)))
                return true;
        }
        return false;
    }
}
