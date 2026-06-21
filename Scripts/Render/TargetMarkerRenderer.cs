using Godot;
using System.Collections.Generic;

namespace Render
{
    /// <summary>
    /// Per-frame 3D sphere-outline target marker placed on a sphere of fixed radius around the
    /// camera, in the direction of the target, sized to the target's ANGULAR size.
    ///
    /// Technique (play-test revision):
    ///   1. Direction: UniMath.RelativePosition(ship, target) → ToDouble3() on the small LCA delta
    ///      → normalize to a unit direction. Safe: this delta is SOI-bounded, so the single
    ///      ToDouble3() on the already-differenced vector is the sanctioned conversion.
    ///   2. Placement: markerPos = cameraGlobalPosition + dir * MarkerCameraDistance.
    ///      The marker rides a fixed-radius sphere around the camera. Depth is constant →
    ///      ring pixel-thickness is constant regardless of how far the real body is.
    ///   3. Angular size: angularRadius = atan(rawRadiusMeters / distanceMeters).
    ///      markerWorldRadius = MarkerCameraDistance * tan(angularRadius).
    ///      The marker subtends the same angle as the real body, landing at the correct
    ///      on-screen position and size.
    ///   4. D-52 floor: pixel radius is clamped to [MinMarkerRadius, MaxMarkerRadius].
    ///      Because depth is constant (MarkerCameraDistance), back-projection is trivial:
    ///      r_world = clampedPixelRadius * tanHalfFov * MarkerCameraDistance / refExtent.
    ///   5. Depth: depth_test_disabled in the shader (render_mode includes depth_test_disabled)
    ///      so the marker always draws on top of scene geometry — it cannot be occluded by
    ///      planets or stars between camera and target.
    ///   6. Far-plane: the marker is at MarkerCameraDistance from the camera, never near the
    ///      far plane, so it is never frustum-culled regardless of target distance.
    ///
    /// Preserved from original:
    ///   D-53 read-only (no GameObjects/LocalPos/TranslatePos writes);
    ///   D-51 cross-space including NO-MESH GALAXY (direction via UniMath LCA, no render-set gate);
    ///   D-57 tracking label (on-screen pin, rides edge marker off-screen);
    ///   D-52 min/max pixel radius floor;
    ///   perspective-correct distortion (real 3D sphere mesh).
    /// </summary>
    public partial class TargetMarkerRenderer : Node3D
    {
        // ── Exports ────────────────────────────────────────────────────────────

        /// <summary>NodePath to the TestSetup / GameWorld node.</summary>
        [Export] public NodePath WorldPath { get; set; }

        /// <summary>NodePath to the Hud node (reads ActiveTargetIndex).</summary>
        [Export] public NodePath HudPath { get; set; }

        /// <summary>NodePath to the Camera3D (for on-screen projection and D-52 floor).</summary>
        [Export] public NodePath CameraPath { get; set; }

        /// <summary>NodePath to the TrackingLabel (Label node under CanvasLayer).</summary>
        [Export] public NodePath TrackingLabelPath { get; set; }

        /// <summary>Phosphor-green outline and label color (D-09 / D-53).</summary>
        [Export] public Color PhosphorGreen { get; set; } = new Color(0.1f, 1.0f, 0.3f);

        /// <summary>Minimum projected screen radius in pixels (D-52 findability floor).</summary>
        [Export] public float MinMarkerRadius { get; set; } = 20f;

        /// <summary>Maximum projected screen radius in pixels — clamps very-close bodies.</summary>
        [Export] public float MaxMarkerRadius { get; set; } = 300f;

        /// <summary>
        /// Padding multiplier applied to the angular radius when computing the outline sphere size.
        /// 1.15 = 15% larger than the body's true angular radius so the outline visibly rings the body.
        /// </summary>
        [Export] public float MarkerPadding { get; set; } = 1.15f;

        /// <summary>Fallback radius in metres for bodies without an authored RadiusMeters (Earth-radius).</summary>
        [Export] public float DefaultBodyRadius { get; set; } = 6.371e6f;

        /// <summary>
        /// Distance from the camera at which the marker sphere is placed (world units).
        /// Chosen to sit between the camera near-plane and far-plane.
        /// Constant depth → constant ring pixel-thickness → no rim_width adaptive logic needed.
        /// Default 75 world units is safely between near (0.05) and far (1e10+).
        /// </summary>
        [Export] public float MarkerCameraDistance { get; set; } = 75f;

        /// <summary>
        /// Constant rim_width for the outline ring. Because depth is fixed at MarkerCameraDistance,
        /// ring pixel-thickness is constant — no adaptive widening needed.
        /// 0.25 gives a thin clean ring; increase toward 0.4 if it looks too thin.
        /// </summary>
        [Export] public float BaseRimWidth { get; set; } = 0.25f;

        // ── Private state ──────────────────────────────────────────────────────

        private TestSetup _world;
        private Hud.Hud _hud;
        private Camera3D _camera;
        private Label _trackingLabel;

        /// <summary>Outline shader loaded once in _Ready.</summary>
        private Shader _outlineShader;

        /// <summary>ShaderMaterial wrapping the outline shader; color pushed each frame.</summary>
        private ShaderMaterial _outlineMaterial;

        /// <summary>Single active-target marker mesh (one target at a time).</summary>
        private MeshInstance3D _markerMesh;

        // ── Godot callbacks ────────────────────────────────────────────────────

        public override void _Ready()
        {
            // Resolve world
            if (WorldPath != null && !WorldPath.IsEmpty)
                _world = GetNode<TestSetup>(WorldPath);
            else
                _world = GetParent<TestSetup>() ?? GetTree().Root.FindChild("Main", true, false) as TestSetup;

            // Resolve Hud
            if (HudPath != null && !HudPath.IsEmpty)
                _hud = GetNode<Hud.Hud>(HudPath);
            else
                _hud = GetTree().Root.FindChild("Hud", true, false) as Hud.Hud;

            // Resolve Camera3D
            if (CameraPath != null && !CameraPath.IsEmpty)
                _camera = GetNode<Camera3D>(CameraPath);
            else
                _camera = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;

            // Resolve TrackingLabel
            if (TrackingLabelPath != null && !TrackingLabelPath.IsEmpty)
                _trackingLabel = GetNode<Label>(TrackingLabelPath);
            else
                _trackingLabel = GetTree().Root.FindChild("TrackingLabel", true, false) as Label;

            // Apply phosphor-green to the label
            if (_trackingLabel != null)
                _trackingLabel.Modulate = PhosphorGreen;

            // Load outline shader and create shared material
            _outlineShader = GD.Load<Shader>("res://Shaders/target_outline.gdshader");
            _outlineMaterial = new ShaderMaterial { Shader = _outlineShader };
        }

        public override void _Process(double delta)
        {
            if (_world == null || _hud == null) return;

            var gameObjects = _world.GameObjects;
            int shipIndex   = _world.ShipIndex;
            if ((uint)shipIndex >= (uint)(gameObjects?.Count ?? 0)) { HideAll(); return; }

            var ship = gameObjects[shipIndex];
            if (ship == null) { HideAll(); return; }

            int tgtIdx = _hud.ActiveTargetIndex;
            if (tgtIdx < 0 || (uint)tgtIdx >= (uint)gameObjects.Count) { HideAll(); return; }

            var targetObj = gameObjects[tgtIdx];
            if (targetObj == null) { HideAll(); return; }

            // ── Step 1: Direction (cross-space via UniMath LCA path) ──────────
            // RelativePosition produces the LCA-frame UniVec3 delta; ToDouble3() on this
            // small SOI-bounded vector is the ONE sanctioned metres conversion.
            // No render-set gate — works for galaxies with no mesh (D-51).
            Double3 relMetres = UniMath.RelativeMetres(ship, targetObj, gameObjects);
            double relMag = relMetres.Magnitude();

            // Guard against coincident objects (division by zero / zero-direction).
            if (relMag < 1e-6) { HideAll(); return; }

            // Unit direction vector (ship → target) in the floating-origin world frame.
            // This is the same frame the Camera3D and marker meshes live in.
            Double3 dirD = new Double3(relMetres.X / relMag, relMetres.Y / relMag, relMetres.Z / relMag);
            var dir = new Vector3((float)dirD.X, (float)dirD.Y, (float)dirD.Z);

            // ── Step 2: Placement on fixed-radius camera sphere ───────────────
            // The marker rides at a constant distance from the camera, so depth is constant
            // → ring pixel-thickness is constant → no adaptive rim_width needed.
            // Camera's global position in the floating-origin render frame.
            Vector3 camPos = _camera != null ? _camera.GlobalPosition : Vector3.Zero;
            Vector3 markerPos = camPos + dir * MarkerCameraDistance;

            // ── Step 3: Angular size → world-space marker radius ──────────────
            // angularRadius = atan(rawRadius / distance)
            // markerWorldRadius = MarkerCameraDistance * tan(angularRadius)
            // This makes the marker subtend the same visual angle as the real body, so it
            // lands at the correct on-screen size and position as though it were the body.
            double rawRadiusMeters = targetObj.RadiusMeters > 0.0 ? targetObj.RadiusMeters : DefaultBodyRadius;
            double angularRadius = System.Math.Atan(rawRadiusMeters * MarkerPadding / relMag);
            float r = (float)(MarkerCameraDistance * System.Math.Tan(angularRadius));

            // ── Step 4: D-52 min/max pixel-radius floor ───────────────────────
            // Because depth = MarkerCameraDistance (constant), the back-projection is trivial:
            //   pixelRadius = refExtent * (r / depth) / tanHalfFov
            //   r_world = clampedPixelRadius * tanHalfFov * depth / refExtent
            var markerMesh = GetOrCreateMarkerMesh();

            if (_camera != null)
            {
                var viewport = GetViewport();
                Vector2 vpSize = viewport != null ? viewport.GetVisibleRect().Size : new Vector2(1152f, 648f);

                float fovRad     = Mathf.DegToRad(_camera.Fov);
                float tanHalfFov = Mathf.Tan(fovRad * 0.5f);
                float refExtent  = _camera.KeepAspect == Camera3D.KeepAspectEnum.Height
                    ? vpSize.Y * 0.5f
                    : vpSize.X * 0.5f;

                // Projected pixel radius using the constant depth.
                float pixelRadius = refExtent * (r / MarkerCameraDistance) / tanHalfFov;

                // Clamp to [MinMarkerRadius, MaxMarkerRadius] pixels.
                float clampedPixelRadius = Mathf.Clamp(pixelRadius, MinMarkerRadius, MaxMarkerRadius);

                // Back-project the clamped pixel radius to a world-space radius at the marker depth.
                // Because depth is constant (MarkerCameraDistance) this is always cheap and exact.
                r = (clampedPixelRadius * MarkerCameraDistance * tanHalfFov) / refExtent;
            }

            // ── Update mesh transform ─────────────────────────────────────────
            markerMesh.Position = markerPos;
            markerMesh.Scale    = new Vector3(r, r, r);
            markerMesh.Visible  = true;

            // Push phosphor-green outline color each frame.
            _outlineMaterial.SetShaderParameter("outline_color",
                new Color(PhosphorGreen.R, PhosphorGreen.G, PhosphorGreen.B, 1.0f));

            // ── Step 5: Constant rim_width (depth is fixed — no adaptive logic needed) ──
            // Because the marker always sits at MarkerCameraDistance from the camera, its
            // apparent size in view space is determined only by the clamped pixel radius.
            // A constant BaseRimWidth produces constant pixel-thickness ring.
            _outlineMaterial.SetShaderParameter("rim_width", BaseRimWidth);

            // ── Tracking label (D-57) ─────────────────────────────────────────
            UpdateTrackingLabel(ship, targetObj, gameObjects, markerPos);
        }

        // ── Tracking label ─────────────────────────────────────────────────────

        /// <summary>
        /// Positions the name+distance tracking label next to the marker on screen,
        /// or rides the off-screen edge marker (DirMarker) when the target is off-screen (D-57).
        /// Read-only — never mutates sim state.
        /// </summary>
        private void UpdateTrackingLabel(
            UniObject ship, UniObject targetObj,
            List<UniObject> gameObjects, Vector3 markerPos)
        {
            if (_trackingLabel == null) return;

            // Compose label text: "{name}  {distance}"
            double distM = UniMath.Distance(ship, targetObj, gameObjects);
            string distStr = Hud.Hud.FormatDistance(distM);
            _trackingLabel.Text = $"{targetObj.Name ?? "?"}  {distStr}";

            if (_camera == null)
            {
                _trackingLabel.Visible = false;
                return;
            }

            var viewport = GetViewport();
            Vector2 vpSize = viewport != null ? viewport.GetVisibleRect().Size : new Vector2(1152f, 648f);

            // Camera-local space transform to detect behind-camera.
            // markerPos is in the same floating-origin world frame as the camera.
            Vector3 camLocal = _camera.GlobalTransform.AffineInverse() * markerPos;
            bool isBehindCamera = camLocal.Z > 0;

            // Project to screen using the camera's actual global position.
            Vector2 screenPos = _camera.UnprojectPosition(markerPos);

            bool isOffScreen = isBehindCamera
                || screenPos.X < 0 || screenPos.X > vpSize.X
                || screenPos.Y < 0 || screenPos.Y > vpSize.Y;

            if (!isOffScreen)
            {
                // On-screen: pin label beside the marker with a small offset so it doesn't
                // overlap the reticle center.
                const float LabelOffset = 12f;
                _trackingLabel.Position = new Vector2(screenPos.X + LabelOffset, screenPos.Y - LabelOffset);
                _trackingLabel.Visible  = true;
            }
            else
            {
                // Off-screen: ride the edge marker (DirMarker).
                // Find the DirMarker sibling under CanvasLayer (same path as Hud uses).
                // If found and visible, position the label next to it.
                var canvasLayer = GetTree().Root.FindChild("CanvasLayer", true, false);
                var dirMarker   = canvasLayer?.GetNodeOrNull<Control>("DirMarker");

                if (dirMarker != null && dirMarker.Visible)
                {
                    // Position the label just to the right of the DirMarker with a small gap.
                    const float EdgeLabelOffset = 20f;
                    _trackingLabel.Position = dirMarker.Position + new Vector2(EdgeLabelOffset, 0f);
                    _trackingLabel.Visible  = true;
                }
                else
                {
                    // DirMarker not visible — hide the label too.
                    _trackingLabel.Visible = false;
                }
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Hides the marker mesh and tracking label when there is no active target.
        /// </summary>
        private void HideAll()
        {
            if (_markerMesh != null) _markerMesh.Visible = false;
            if (_trackingLabel != null) _trackingLabel.Visible = false;
        }

        /// <summary>
        /// Returns the existing marker MeshInstance3D, or creates it lazily on first call.
        /// One mesh instance per TargetMarkerRenderer (one active target at a time).
        /// Unit sphere (Radius=1, Height=2) scaled each frame via Scale; outline shader
        /// applied as MaterialOverride.
        /// </summary>
        private MeshInstance3D GetOrCreateMarkerMesh()
        {
            if (_markerMesh != null) return _markerMesh;

            var sphereMesh = new SphereMesh { Radius = 1f, Height = 2f };

            _markerMesh = new MeshInstance3D
            {
                Mesh             = sphereMesh,
                MaterialOverride = _outlineMaterial,
                Visible          = false,
            };

            AddChild(_markerMesh);
            return _markerMesh;
        }
    }
}
