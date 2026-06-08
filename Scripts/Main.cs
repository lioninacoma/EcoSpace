using Godot;

public partial class Main : Node3D
{
    private Octree _octree = new();
    private OctreeRenderer _renderer;
    private Camera3D _camera;
    private Label _label;

    private Vector3 _camTarget = Vector3.Zero; // pan offset
    private float _zoom = 40f;                 // orthographic size
    private int _sliceZ = 16;                  // current Z in MaxDepth coords

    private bool _panning = false;
    private Vector2 _panStartMouse;
    private Vector3 _panStartTarget;

    private int Depth => ZoomToDepth(_zoom);

    public override void _Ready()
    {
        // Center camera on grid initially
        int half = _octree.MaxZ / 2;
        _camTarget = new Vector3(half, half, 0);
        _sliceZ = half;

        SetupCamera();
        SetupRenderer();
        SetupUI();
        Redraw();
    }

    private void SetupCamera()
    {
        _camera = new Camera3D();
        _camera.Projection = Camera3D.ProjectionType.Orthogonal;
        _camera.Size = _zoom;
        AddChild(_camera);
        PositionCamera();
    }

    private void SetupRenderer()
    {
        _renderer = new OctreeRenderer();
        AddChild(_renderer);
    }

    private void SetupUI()
    {
        _label = new Label();
        _label.Position = new Vector2(10, 10);
        _label.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        _label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0));
        _label.AddThemeFontSizeOverride("font_size", 14);

        var canvas = new CanvasLayer();
        canvas.AddChild(_label);
        AddChild(canvas);
    }

    public override void _Process(double delta)
    {
        bool dirty = false;

        if (Input.IsActionJustPressed("ui_up"))
        {
            _sliceZ = Mathf.Min(_sliceZ + 1, _octree.MaxZ - 1);
            dirty = true;
        }
        if (Input.IsActionJustPressed("ui_down"))
        {
            _sliceZ = Mathf.Max(_sliceZ - 1, 0);
            dirty = true;
        }

        if (dirty) Redraw();

        _label.Text =
            $"Z slice : {_sliceZ} / {_octree.MaxZ - 1}\n" +
            $"Depth   : {Depth} / {_octree.MaxDepth}\n" +
            $"Zoom    : {_zoom:F1}\n" +
            $"\n[↑/↓] step Z   [Scroll] zoom   [MMB/RMB drag] pan";
    }

    public override void _Input(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)   { _zoom = Mathf.Max(_zoom * 0.9f, 2f);  ApplyZoom(); }
            if (mb.ButtonIndex == MouseButton.WheelDown) { _zoom = Mathf.Min(_zoom * 1.1f, 80f); ApplyZoom(); }

            bool isPanBtn = mb.ButtonIndex == MouseButton.Middle || mb.ButtonIndex == MouseButton.Right;
            if (isPanBtn)
            {
                _panning = mb.Pressed;
                _panStartMouse = mb.Position;
                _panStartTarget = _camTarget;
            }
        }

        if (ev is InputEventMouseMotion mm && _panning)
        {
            float worldPerPixel = _zoom / GetViewport().GetVisibleRect().Size.Y;
            Vector2 d = (mm.Position - _panStartMouse) * worldPerPixel;
            _camTarget = _panStartTarget + new Vector3(-d.X, d.Y, 0);
            PositionCamera();
        }
    }

    private void ApplyZoom()
    {
        _camera.Size = _zoom;
        Redraw();
    }

    private void PositionCamera()
    {
        _camera.Position = _camTarget + new Vector3(0, 0, 100);
        _camera.LookAt(_camTarget, Vector3.Up);
    }

    private void Redraw()
    {
        var cells = _octree.GetSlice(Depth, _sliceZ);
        _renderer.DrawSlice(cells, Depth, _octree.MaxDepth);
    }

    // Map zoom level to octree depth.
    // Small zoom (zoomed in) = high depth (fine).
    // Large zoom (zoomed out) = low depth (coarse).
    private int ZoomToDepth(float zoom)
    {
        float minZoom = 2f, maxZoom = 80f;
        float t = 1f - Mathf.Clamp((zoom - minZoom) / (maxZoom - minZoom), 0f, 1f);
        int depth = 1 + (int)(t * (_octree.MaxDepth - 1));
        return Mathf.Clamp(depth, 1, _octree.MaxDepth);
    }
}
