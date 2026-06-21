using Godot;
using System.Collections.Generic;

namespace Render
{
    /// <summary>
    /// Per-frame 3D sphere-outline target marker positioned and sized directly from UniObject,
    /// with no dependency on the body being in the live rendered mesh set (D-50/D-51/D-52/D-53).
    ///
    /// Placement uses the UniMath LCA path (RelativeMetres) and the canonical Phase-5
    /// observer-unit conversion:
    ///   position:  relMetres × (factor / ship.LocalPos.Scale)
    ///   radius:    (rawRadiusMeters / ship.LocalPos.Scale) × factor
    /// This mirrors WorldRenderer.ComputeStarRenderPosFromHierarchy (lines 401–406) and
    /// WorldRenderer.RenderBodyAt (line 455) exactly, so the outline sphere sits flush over
    /// the body mesh in every space.
    ///
    /// A D-52 minimum on-screen size floor ensures the marker never shrinks to a sub-pixel
    /// speck: the analytic projected pixel radius is clamped to [MinMarkerRadius,MaxMarkerRadius]
    /// and the mesh scale is adjusted to hold the floor at any distance.
    ///
    /// Cross-space (D-51): the marker works for any body in the full hierarchy — including a
    /// galaxy in Universe space that has NO rendered mesh. All position math goes through
    /// UniMath; never body.LocalPos.ToDouble3() across spaces.
    ///
    /// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
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
        [Export] public float MinMarkerRadius { get; set; } = 6f;

        /// <summary>Maximum projected screen radius in pixels — clamps very-close bodies.</summary>
        [Export] public float MaxMarkerRadius { get; set; } = 200f;

        /// <summary>
        /// Padding multiplier applied to the physics radius when computing the outline sphere size.
        /// 1.15 = 15% larger than the body's true radius so the outline visibly rings the body.
        /// </summary>
        [Export] public float MarkerPadding { get; set; } = 1.15f;

        /// <summary>Fallback radius in metres for bodies without an authored RadiusMeters (Earth-radius).</summary>
        [Export] public float DefaultBodyRadius { get; set; } = 6.371e6f;

        // ── Ring-thickness adaptive rim_width knobs ──────────────────────────────

        /// <summary>
        /// Minimum visible ring pixel thickness (D-52 ring floor, default ~2px).
        /// The fresnel rim_width is widened automatically so the ring's screen-space
        /// pixel thickness never falls below this value, regardless of how far away the
        /// target is. Increase if the ring still looks faint on your display.
        /// </summary>
        [Export] public float MinRingThicknessPx { get; set; } = 2.0f;

        /// <summary>
        /// Baseline rim_width used for large / close-up targets where no minimum-thickness
        /// widening is needed. Produces a thin, clean outline ring. (Default 0.25.)
        /// </summary>
        [Export] public float BaseRimWidth { get; set; } = 0.25f;

        /// <summary>
        /// Maximum rim_width the adaptive formula is allowed to reach.
        /// Prevents the ring from growing into a filled disc for extremely small spheres.
        /// (Default 0.85 — leaves a small darkened center even at the floor radius.)
        /// </summary>
        [Export] public float MaxRimWidth { get; set; } = 0.85f;

        /// <summary>
        /// Empirical scale constant relating rim_width to approximate ring pixel thickness.
        /// ring_px ≈ pixelRadius × RimWidthScale × rim_width.
        /// The relationship is slightly sub-linear near the silhouette; 1.0 is a safe
        /// conservative default (may slightly over-widen; reduce toward 0.8 if the ring
        /// looks too thick on close targets after testing).
        /// </summary>
        [Export] public float RimWidthScale { get; set; } = 1.0f;

        // ── Render factors (mirror WorldRenderer per-space values) ─────────────

        /// <summary>
        /// Render units per observer-unit for Planet-space frames.
        /// Must match WorldRenderer.PlanetRenderFactor (both 1e-8f).
        /// </summary>
        private float PlanetRenderFactor { get; set; } = 1e-8f;

        /// <summary>
        /// Render units per observer-unit for Star-space frames.
        /// Aliased from WorldRenderer.StarRenderFactor (= LuminousDescriptorBuilder.StarRenderFactor = 1e-8f).
        /// Single source of truth — do not hardcode 1e-8 here independently.
        /// </summary>
        private float StarRenderFactor => WorldRenderer.StarRenderFactor;

        /// <summary>Render units per observer-unit for Galaxy-space frames.</summary>
        private float GalaxyRenderFactor { get; set; } = 1e-8f;

        /// <summary>Render units per observer-unit for Universe-space frames.</summary>
        private float UniverseRenderFactor { get; set; } = 1e-8f;

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

            // ── Render factor for the ship's current space ────────────────────
            float factor = RenderFactorFor(ship.CurrentSpace);

            // ── Position (D-51 cross-space, UniMath LCA path) ─────────────────
            // relMetres = target − ship in metres via LCA; never body.LocalPos.ToDouble3().
            Double3 relMetres = UniMath.RelativeMetres(ship, targetObj, gameObjects);

            // Convert to observer units then render units.
            // This is the canonical Phase-5 formula (05-03-SUMMARY §Observer-Unit Render Conversion):
            //   position = relMetres × (factor / ship.LocalPos.Scale)
            // Mirrors WorldRenderer.ComputeStarRenderPosFromHierarchy lines 401–406.
            double obsFactor = factor / ship.LocalPos.Scale;
            var markerPos = new Vector3(
                (float)(relMetres.X * obsFactor),
                (float)(relMetres.Y * obsFactor),
                (float)(relMetres.Z * obsFactor));

            // ── Radius (D-51) ─────────────────────────────────────────────────
            // radius = (rawRadiusMeters / ship.LocalPos.Scale) × factor
            // Mirrors WorldRenderer.RenderBodyAt line 455.
            double rawRadiusMeters = targetObj.RadiusMeters > 0.0 ? targetObj.RadiusMeters : DefaultBodyRadius;
            float r = (float)((rawRadiusMeters / ship.LocalPos.Scale) * factor) * MarkerPadding;

            // ── D-52 min/max pixel radius floor ──────────────────────────────
            // Analytic on-screen radius formula (ported from Hud.cs lines 418–433).
            // depth = distance along camera forward axis to the marker.
            var markerMesh = GetOrCreateMarkerMesh();

            if (_camera != null)
            {
                // Transform marker world position to camera-local space.
                // The marker's global position = GlobalPosition (this node at world origin) + markerPos.
                // Because this Node3D sits at the world origin (global Position = Vector3.Zero,
                // the same floating-origin frame as WorldRenderer), markerPos IS the global position
                // of the marker relative to the camera.
                Vector3 camLocal = _camera.GlobalTransform.AffineInverse() * markerPos;
                float depth = -camLocal.Z;   // Godot camera looks down -Z; depth is positive in front

                if (depth > 1e-4f)
                {
                    var viewport = GetViewport();
                    Vector2 vpSize = viewport != null ? viewport.GetVisibleRect().Size : new Vector2(1152f, 648f);

                    float fovRad     = Mathf.DegToRad(_camera.Fov);
                    float tanHalfFov = Mathf.Tan(fovRad * 0.5f);
                    float refExtent  = _camera.KeepAspect == Camera3D.KeepAspectEnum.Height
                        ? vpSize.Y * 0.5f
                        : vpSize.X * 0.5f;

                    // Projected pixel radius for the current physics-based render radius r.
                    float pixelRadius = refExtent * (r / depth) / tanHalfFov;

                    // Clamp to [MinMarkerRadius, MaxMarkerRadius] pixels.
                    float clampedPixelRadius = Mathf.Clamp(pixelRadius, MinMarkerRadius, MaxMarkerRadius);

                    // If we had to clamp, compute the world-space radius that yields the clamped pixel radius.
                    if (!Mathf.IsEqualApprox(pixelRadius, clampedPixelRadius, 0.01f))
                    {
                        // Inverse-project: r_world = (pixelRadius * depth * tanHalfFov) / refExtent
                        r = (clampedPixelRadius * depth * tanHalfFov) / refExtent;
                    }
                }
                // If depth <= 0 (behind camera or coincident) keep r unchanged;
                // the mesh will be hidden by the camera-behind guard in the label section below.
            }

            // ── Update mesh transform ─────────────────────────────────────────
            markerMesh.Position = markerPos;
            markerMesh.Scale    = new Vector3(r, r, r);
            markerMesh.Visible  = true;

            // Push phosphor-green outline color each frame.
            _outlineMaterial.SetShaderParameter("outline_color",
                new Color(PhosphorGreen.R, PhosphorGreen.G, PhosphorGreen.B, 1.0f));

            // ── Adaptive rim_width for minimum ring pixel thickness (D-52 ring floor) ──
            // The fresnel band spans rim ∈ [0, rim_width] across the sphere surface.
            // Its approximate screen-space pixel thickness is:
            //   ring_px ≈ pixelRadius × RimWidthScale × rim_width
            // To guarantee ring_px >= MinRingThicknessPx we solve for rim_width:
            //   rim_width = MinRingThicknessPx / (pixelRadius × RimWidthScale)
            // Clamped to [BaseRimWidth, MaxRimWidth] so:
            //   - Near/large targets keep the clean thin ring (BaseRimWidth).
            //   - Far/small targets auto-widen but never become a filled disc (MaxRimWidth).
            // pixelRadius here is the *effective* pixel radius after the D-52 floor clamp,
            // so the ring thickness floor is calibrated against the actual drawn size.
            {
                // Re-derive the effective pixel radius from the (possibly clamped) render radius r.
                float rimPx = MinMarkerRadius; // safe fallback if camera unavailable
                if (_camera != null)
                {
                    Vector3 camLocalForRim = _camera.GlobalTransform.AffineInverse() * markerPos;
                    float depthForRim = -camLocalForRim.Z;
                    if (depthForRim > 1e-4f)
                    {
                        var viewportForRim = GetViewport();
                        Vector2 vpSizeForRim = viewportForRim != null
                            ? viewportForRim.GetVisibleRect().Size
                            : new Vector2(1152f, 648f);
                        float fovRadForRim     = Mathf.DegToRad(_camera.Fov);
                        float tanHalfFovForRim = Mathf.Tan(fovRadForRim * 0.5f);
                        float refExtentForRim  = _camera.KeepAspect == Camera3D.KeepAspectEnum.Height
                            ? vpSizeForRim.Y * 0.5f
                            : vpSizeForRim.X * 0.5f;
                        // Pixel radius of the marker as currently rendered (after D-52 clamp).
                        rimPx = refExtentForRim * (r / depthForRim) / tanHalfFovForRim;
                        rimPx = Mathf.Max(rimPx, MinMarkerRadius);
                    }
                }

                float adaptiveRimWidth = rimPx * RimWidthScale > 0f
                    ? Mathf.Clamp(MinRingThicknessPx / (rimPx * RimWidthScale), BaseRimWidth, MaxRimWidth)
                    : BaseRimWidth;

                _outlineMaterial.SetShaderParameter("rim_width", adaptiveRimWidth);
            }

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

            // Project to screen using the camera's actual global position + the world-space offset.
            // Since the floating-origin camera IS at the world origin, GlobalPosition ~ Vector3.Zero,
            // so GlobalPosition + markerPos == markerPos. Use the camera GlobalPosition as the base
            // to match Hud.UpdateDirectionMarker's pattern.
            Vector2 screenPos = _camera.UnprojectPosition(_camera.GlobalPosition + markerPos);

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

        /// <summary>
        /// Maps the observer (ship) space to the appropriate per-space render factor.
        /// Mirrors WorldRenderer.RenderFactorFor exactly so the marker uses the same
        /// render-unit basis as the body meshes.
        /// </summary>
        private float RenderFactorFor(UniObject.Space space) => space switch
        {
            UniObject.Space.Planet   => PlanetRenderFactor,
            UniObject.Space.Star     => StarRenderFactor,
            UniObject.Space.Galaxy   => GalaxyRenderFactor,
            UniObject.Space.Universe => UniverseRenderFactor,
            _                        => StarRenderFactor,
        };
    }
}
