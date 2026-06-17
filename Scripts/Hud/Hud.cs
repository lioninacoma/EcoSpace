using Godot;

namespace Hud
{
    /// <summary>
    /// Full minimal retro HUD (HUD-01/02/04, D-09/D-10/D-11/D-12 + findability).
    ///
    /// Read-only consumer of GameWorld state — MUST NOT mutate sim state.
    ///
    /// Provides:
    ///   • Adaptive-unit speed readout (FormatSpeed): m/s → km/s → AU/s → ly/s (D-10)
    ///   • Context label: space tier + nearest body (D-11)
    ///   • Cycle-able target readout over current-space parent + siblings (D-12)
    ///   • Off-screen edge direction marker pointing toward active target (findability)
    ///   • Phosphor-green CRT aesthetic throughout (D-09)
    ///
    /// Anti-patterns honored:
    ///   • mouse_filter = Ignore on all HUD Controls (set in Main.tscn and here)
    ///   • Reads FlightController.CurrentSpeed (not a delta-position estimate)
    ///   • All index lookups null-guarded via (uint)i cast trick
    ///
    /// Targetable set = parent body (at SOI origin) + siblings (other children of parent).
    /// All ship-relative distances and vectors use UniMath.RelativeMetres / UniMath.Distance
    /// (LCA-based path) so that bodies at the parent-frame origin do not produce identical
    /// distances to the parent itself, preventing per-frame nearest-winner flicker.
    /// </summary>
    public partial class Hud : Control
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Astronomical Unit in metres.</summary>
        private const double AU = 1.495978707e11;

        /// <summary>Light-year in metres.</summary>
        private const double LY = 9.4607304725808e15;

        // ── Exports ───────────────────────────────────────────────────────────

        /// <summary>NodePath to the GameWorld / TestSetup node in the scene.</summary>
        [Export] public NodePath WorldPath { get; set; }

        /// <summary>NodePath to the FlightController node.</summary>
        [Export] public NodePath FlightPath { get; set; }

        /// <summary>NodePath to the Camera3D (for off-screen marker projection).</summary>
        [Export] public NodePath CameraPath { get; set; }

        /// <summary>Phosphor-green CRT color for the HUD (D-09).</summary>
        [Export] public Color PhosphorGreen { get; set; } = new Color(0.1f, 1.0f, 0.3f);

        // ── Private references ────────────────────────────────────────────────

        private TestSetup _world;
        private Flight.FlightController _flight;
        private Camera3D _camera;
        private Render.WorldRenderer _worldRenderer;

        // ── Target circle state (D-46) ────────────────────────────────────────

        private bool    _showTargetCircle;
        private Vector2 _targetCirclePos;
        private float   _targetCircleRadius;

        // ── HUD label nodes ───────────────────────────────────────────────────

        private Label _speedLabel;
        private Label _contextLabel;
        private Label _targetLabel;
        private Control _dirMarker;   // off-screen direction arrow container

        // ── Target cycling state ──────────────────────────────────────────────

        /// <summary>
        /// Represents one entry in the targetable body list.
        /// All distance math routes through UniMath.RelativeMetres (LCA path),
        /// so no IsParent flag is needed — parent and siblings are treated uniformly.
        /// </summary>
        private readonly struct TargetEntry
        {
            public readonly int Index;
            public TargetEntry(int index) { Index = index; }
        }

        /// <summary>Current index into the targetable body list (parent + siblings).</summary>
        private int _targetIndex = 0;

        /// <summary>
        /// Index into GameObjects of the active target, or -1 if none is available.
        /// Read-only — never mutates _targetIndex or any sim state (Hud is a read-only
        /// consumer; HUD mutating sim state is an anti-pattern). Returns -1 when:
        ///   • _world is null (scene not yet ready)
        ///   • ship index is out of range or ship object is null
        ///   • the targetable list is empty (no parent / all siblings null)
        /// Rebuilds the targetable list fresh each call (cheap — ≤ ~10 children)
        /// so a stale _targetIndex after an SOI transition is always clamped to
        /// the live list bounds (T-04-03 mitigation, D-45 / D-12 preserved).
        /// </summary>
        public int ActiveTargetIndex
        {
            get
            {
                if (_world == null) return -1;
                var objs = _world.GameObjects;
                int shipIdx = _world.ShipIndex;
                if ((uint)shipIdx >= (uint)(objs?.Count ?? 0)) return -1;
                var ship = objs[shipIdx];
                if (ship == null) return -1;
                var targets = BuildTargetableList(ship.ParentIndex, shipIdx, objs);
                if (targets.Count == 0) return -1;
                int clamped = Mathf.Clamp(_targetIndex, 0, targets.Count - 1);
                return targets[clamped].Index;
            }
        }

        // ── Godot callbacks ───────────────────────────────────────────────────

        public override void _Ready()
        {
            // Resolve world
            if (WorldPath != null && !WorldPath.IsEmpty)
                _world = GetNode<TestSetup>(WorldPath);
            else
                _world = GetTree().Root.FindChild("Main", true, false) as TestSetup;

            // Resolve flight controller
            if (FlightPath != null && !FlightPath.IsEmpty)
                _flight = GetNode<Flight.FlightController>(FlightPath);
            else
                _flight = GetTree().Root.FindChild("FlightController", true, false) as Flight.FlightController;

            // Resolve camera
            if (CameraPath != null && !CameraPath.IsEmpty)
                _camera = GetNode<Camera3D>(CameraPath);
            else
                _camera = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;

            // Resolve world renderer (for target circle render-set gate, D-46)
            _worldRenderer = GetTree().Root.FindChild("WorldRenderer", true, false) as Render.WorldRenderer;

            // Resolve child label nodes
            _speedLabel   = GetNodeOrNull<Label>("SpeedLabel");
            _contextLabel = GetNodeOrNull<Label>("ContextLabel");
            _targetLabel  = GetNodeOrNull<Label>("TargetLabel");
            // DirMarker lives as a sibling under CanvasLayer (not a child of Hud)
            // so it can be positioned in full viewport coordinates without offset math.
            _dirMarker    = GetParent()?.GetNodeOrNull<Control>("DirMarker");

            // Apply phosphor-green to all child labels
            ApplyPhosphorGreen(_speedLabel);
            ApplyPhosphorGreen(_contextLabel);
            ApplyPhosphorGreen(_targetLabel);
            if (_dirMarker != null)
                _dirMarker.Modulate = PhosphorGreen;

            // Ensure this control doesn't swallow mouse motion (advisory anti-pattern)
            MouseFilter = MouseFilterEnum.Ignore;

            // Run format smoke tests in debug (RED turns GREEN here)
            HudFormatTests.Run();
        }

        public override void _Process(double delta)
        {
            if (_world == null) return;

            var gameObjects = _world.GameObjects;
            int shipIndex = _world.ShipIndex;
            if ((uint)shipIndex >= (uint)(gameObjects?.Count ?? 0)) return;

            var ship = gameObjects[shipIndex];
            if (ship == null) return;

            UpdateSpeedLabel();
            UpdateContextLabel(ship, gameObjects, shipIndex);
            UpdateTargetReadout(ship, gameObjects, shipIndex);
            UpdateTargetCircle(ship, gameObjects);
            QueueRedraw();  // triggers _Draw each frame — required (D-46, Pitfall 4)
        }

        // ── Speed label ───────────────────────────────────────────────────────

        private void UpdateSpeedLabel()
        {
            if (_speedLabel == null) return;

            double speed = _flight != null ? _flight.CurrentSpeed : 0.0;
            _speedLabel.Text = $"SPD  {FormatSpeed(speed)}";
        }

        // ── Context label (D-11) ──────────────────────────────────────────────

        private void UpdateContextLabel(UniObject ship, System.Collections.Generic.List<UniObject> gameObjects, int shipIndex)
        {
            if (_contextLabel == null) return;

            string tier = SpaceTierName(ship.CurrentSpace);

            // Find nearest body: scan parent + siblings (the same targetable set as D-12).
            // UniMath.RelativeMetres uses the LCA path for all bodies — avoids the
            // identical-magnitude tie that occurs when a sibling sits at the parent-frame
            // origin (body at 0,0,0 and parent-body produce the same distance via the old
            // ship.LocalPos.ToDouble3() formula, causing per-frame nearest-winner flicker).
            var targets = BuildTargetableList(ship.ParentIndex, shipIndex, gameObjects);
            string nearestName = "---";
            double minDist = double.MaxValue;
            foreach (var entry in targets)
            {
                if ((uint)entry.Index >= (uint)gameObjects.Count) continue;
                var body = gameObjects[entry.Index];
                if (body == null) continue;

                double dist = UniMath.Distance(ship, body, gameObjects);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearestName = body.Name ?? "?";
                }
            }

            _contextLabel.Text = $"{tier} · nearest: {nearestName}";
        }

        // ── Target cycle + readout (D-12) ─────────────────────────────────────

        private void UpdateTargetReadout(UniObject ship, System.Collections.Generic.List<UniObject> gameObjects, int shipIndex)
        {
            // Build targetable list: parent body first, then siblings (ship excluded, null-skipped).
            var targets = BuildTargetableList(ship.ParentIndex, shipIndex, gameObjects);

            // Clamp/reset _targetIndex across SOI transitions (T-04-02 mitigation)
            if (targets.Count == 0)
            {
                _targetIndex = 0;
                if (_targetLabel != null) _targetLabel.Text = "TGT  ---";
                HideMarker();
                return;
            }

            _targetIndex = Mathf.Clamp(_targetIndex, 0, targets.Count - 1);
            var entry = targets[_targetIndex];
            var targetObj = (uint)entry.Index < (uint)gameObjects.Count ? gameObjects[entry.Index] : null;

            if (targetObj == null)
            {
                if (_targetLabel != null) _targetLabel.Text = "TGT  ---";
                HideMarker();
                return;
            }

            // Ship-relative vector in metres — LCA path handles parent and sibling uniformly.
            Double3 relMeters = UniMath.RelativeMetres(ship, targetObj, gameObjects);
            double distM = relMeters.Magnitude();
            if (_targetLabel != null)
                _targetLabel.Text = $"TGT  {targetObj.Name ?? "?"} · {FormatDistance(distM)}";

            // Off-screen direction marker (pre-computed relative vector avoids double dispatch)
            UpdateDirectionMarker(relMeters);
        }

        // ── Off-screen direction marker ───────────────────────────────────────

        /// <summary>
        /// Positions the off-screen marker using the precomputed ship-relative vector
        /// (metres) from UniMath.RelativeMetres. This method never touches LocalPos directly.
        /// </summary>
        private void UpdateDirectionMarker(Double3 relD)
        {
            if (_dirMarker == null || _camera == null) return;

            // Convert ship-relative metres to a Godot Vector3 for projection.
            var relVec = new Vector3((float)relD.X, (float)relD.Y, (float)relD.Z);

            // Project to screen
            var viewport = GetViewport();
            if (viewport == null) { HideMarker(); return; }
            Vector2 vpSize = viewport.GetVisibleRect().Size;

            // Camera frustum check: is target in front of camera?
            // Transform to camera-local space
            Vector3 cameraLocal = _camera.GlobalTransform.AffineInverse() * relVec;

            // If behind camera (cameraLocal.Z > 0 in Godot's -Z-forward convention)
            // or projects outside screen, show the marker
            bool isBehindCamera = cameraLocal.Z > 0;
            Vector2 screenPos = _camera.UnprojectPosition(_camera.GlobalPosition + relVec);

            bool isOffScreen = isBehindCamera ||
                               screenPos.X < 0 || screenPos.X > vpSize.X ||
                               screenPos.Y < 0 || screenPos.Y > vpSize.Y;

            if (!isOffScreen)
            {
                HideMarker();
                return;
            }

            // Compute viewport-edge position
            Vector2 center = vpSize / 2f;

            // Direction from center to projected point (or reversed if behind camera)
            Vector2 dir;
            if (isBehindCamera)
            {
                // When behind camera, flip the direction
                dir = (center - screenPos).Normalized();
                if (dir == Vector2.Zero) dir = Vector2.Down;
            }
            else
            {
                dir = (screenPos - center).Normalized();
            }

            // Pin to viewport edge with margin
            float margin = 24f;
            Vector2 halfSize = vpSize / 2f - new Vector2(margin, margin);
            float tx = halfSize.X / Mathf.Max(Mathf.Abs(dir.X), 0.001f);
            float ty = halfSize.Y / Mathf.Max(Mathf.Abs(dir.Y), 0.001f);
            float t = Mathf.Min(tx, ty);
            Vector2 edgePos = center + dir * t;

            // Position the marker and rotate to point toward target
            float markerHalf = _dirMarker.Size.X / 2f;
            _dirMarker.Position = edgePos - new Vector2(markerHalf, markerHalf);
            _dirMarker.Rotation = dir.Angle() + Mathf.Pi / 2f;
            _dirMarker.Visible = true;
        }

        private void HideMarker()
        {
            if (_dirMarker != null) _dirMarker.Visible = false;
        }

        // ── Target circle draw (D-46) ─────────────────────────────────────────

        // Minimum on-screen radius so a distant target is never sub-pixel (D-46 tuning knob).
        private const float MIN_CIRCLE_RADIUS = 20f;
        // Maximum on-screen radius so a close target doesn't fill the screen.
        private const float MAX_CIRCLE_RADIUS = 200f;

        /// <summary>
        /// Computes per-frame circle state (_showTargetCircle, _targetCirclePos, _targetCircleRadius).
        /// Sets _showTargetCircle = false at the top; only sets it true after ALL guards pass:
        ///   1. _worldRenderer + _camera null-guard
        ///   2. Resolve active target via BuildTargetableList (current-tier reach, D-45/D-12)
        ///   3. Render-set gate: WorldRenderer.GetRenderPosition (D-46) — off if not a current-space mesh
        ///   4. Behind-camera guard (camLocal.Z > 0, mirrors UpdateDirectionMarker, Pitfall 6)
        ///   5. Off-screen bounds check — both suppressed cases fall back to the edge marker
        ///   6. Clamp radius to [MIN_CIRCLE_RADIUS, MAX_CIRCLE_RADIUS] (D-46 minimum floor)
        ///
        /// This method is a read-only consumer — it MUST NOT mutate _targetIndex or any GameObjects element.
        /// </summary>
        private void UpdateTargetCircle(UniObject ship, System.Collections.Generic.List<UniObject> gameObjects)
        {
            _showTargetCircle = false;

            // Guard 1: require world renderer and camera
            if (_worldRenderer == null || _camera == null) return;

            // Guard 2: resolve active target via current-tier targetable list (D-45 / D-12)
            var targets = BuildTargetableList(ship.ParentIndex, _world.ShipIndex, gameObjects);
            if (targets.Count == 0) return;
            int clamped = Mathf.Clamp(_targetIndex, 0, targets.Count - 1);
            int tgtIdx = targets[clamped].Index;

            // Guard 3: render-set gate — is the body a mesh in the current space? (D-46)
            // Returns false when the target is in a different SOI space → edge marker handles findability.
            if (!_worldRenderer.GetRenderPosition(tgtIdx, out Vector3 renderPos)) return;

            // Convert render-space position to global (Pitfall 5 / A3 — explicit GlobalPosition is safe
            // regardless of WorldRenderer's actual world position, unlike assuming Vector3.Zero).
            Vector3 globalPos = _worldRenderer.GlobalPosition + renderPos;

            // Guard 4: behind-camera check — mirrors UpdateDirectionMarker (Pitfall 6)
            // Godot uses -Z-forward; cameraLocal.Z > 0 means the point is behind the camera.
            Vector3 camLocal = _camera.GlobalTransform.AffineInverse() * (globalPos - _camera.GlobalPosition);
            if (camLocal.Z > 0) return;  // behind camera → edge marker fallback

            // Guard 5: project to screen and check viewport bounds
            var viewport = GetViewport();
            if (viewport == null) return;
            Vector2 vpSize = viewport.GetVisibleRect().Size;
            Vector2 screenPos = _camera.UnprojectPosition(globalPos);
            if (screenPos.X < 0 || screenPos.X > vpSize.X || screenPos.Y < 0 || screenPos.Y > vpSize.Y)
                return;  // off-screen → edge marker fallback

            // All guards passed — compute circle and enable draw
            // Guard 6: minimum-radius floor for findability (D-46 "never a sub-pixel speck")
            _targetCirclePos    = screenPos;
            _targetCircleRadius = Mathf.Clamp(MIN_CIRCLE_RADIUS, MIN_CIRCLE_RADIUS, MAX_CIRCLE_RADIUS);
            _showTargetCircle   = true;
        }

        /// <summary>
        /// Draws the world-pinned target outline (D-46) when _showTargetCircle is set.
        /// Draws an unfilled arc (outline only — retro aesthetic) using the existing PhosphorGreen color.
        /// Called by Godot each frame when QueueRedraw() is invoked in _Process.
        /// </summary>
        public override void _Draw()
        {
            if (!_showTargetCircle) return;
            // DrawArc(center, radius, startAngle, endAngle, pointCount, color, lineWidth)
            // Unfilled outline — DrawArc not DrawCircle (retro aesthetic, D-46)
            DrawArc(_targetCirclePos, _targetCircleRadius, 0f, Mathf.Tau, 32, PhosphorGreen, 1.5f);
        }

        // ── Input: cycle target ───────────────────────────────────────────────

        public override void _Input(InputEvent @event)
        {
            // cycle_target (Tab) must be read in _Input — _Input runs BEFORE the GUI
            // focus system, so we see Tab before Godot's built-in ui_focus_next (also
            // Tab) can consume it. After handling, we mark the event handled so focus
            // navigation does not also fire. (_UnhandledInput would be too late — the
            // focus system runs before it and would swallow Tab.)
            if (@event.IsActionPressed("cycle_target"))
            {
                if (_world == null) return;
                var gameObjects = _world.GameObjects;
                int shipIndex = _world.ShipIndex;
                if ((uint)shipIndex >= (uint)(gameObjects?.Count ?? 0)) return;
                var ship = gameObjects[shipIndex];
                if (ship == null) return;

                var targets = BuildTargetableList(ship.ParentIndex, shipIndex, gameObjects);
                if (targets.Count > 0)
                    _targetIndex = (_targetIndex + 1) % targets.Count;

                // Consume so ui_focus_next does not also act on this Tab press.
                GetViewport().SetInputAsHandled();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the ordered targetable body list: parent body first, then each sibling
        /// child of the parent (ship excluded, null/out-of-range-skipped).
        ///
        /// The parent is always listed first so Tab-cycling has a stable order.
        /// Count ≥ 1 whenever the ship has a valid parent (which it always does in the
        /// current scene hierarchy).
        ///
        /// All distance math uses UniMath.RelativeMetres (LCA path), so all entries are
        /// treated uniformly — no IsParent flag is needed.
        /// </summary>
        private System.Collections.Generic.List<TargetEntry> BuildTargetableList(
            int parentIdx, int shipIndex,
            System.Collections.Generic.List<UniObject> gameObjects)
        {
            var result = new System.Collections.Generic.List<TargetEntry>();
            if ((uint)parentIdx >= (uint)(gameObjects?.Count ?? 0)) return result;
            var parent = gameObjects[parentIdx];
            if (parent == null) return result;

            // Parent body is always targetable (it is visible as the SOI origin).
            result.Add(new TargetEntry(parentIdx));

            // Siblings: other children of parent, excluding the ship itself.
            foreach (int idx in parent.ChildIndices)
            {
                if (idx == shipIndex) continue;
                if ((uint)idx >= (uint)gameObjects.Count) continue;
                if (gameObjects[idx] == null) continue;
                result.Add(new TargetEntry(idx));
            }
            return result;
        }

        /// <summary>Maps UniObject.Space to a display string for the context label.</summary>
        private static string SpaceTierName(UniObject.Space space) => space switch
        {
            UniObject.Space.Universe => "UNIVERSE SPACE",
            UniObject.Space.Galaxy   => "GALAXY SPACE",
            UniObject.Space.Star     => "STAR SPACE",
            UniObject.Space.Planet   => "PLANET SPACE",
            _                        => "SPACE"
        };

        private void ApplyPhosphorGreen(Label label)
        {
            if (label != null) label.Modulate = PhosphorGreen;
        }

        // ── Public formatting API (HUD-01 / D-10) ────────────────────────────

        /// <summary>
        /// Formats a speed in m/s using scale-adaptive units (D-10).
        /// Picks the largest unit that keeps the displayed number roughly 1–9999:
        ///   &lt;1 km/s  → m/s
        ///   &lt;1 AU/s  → km/s
        ///   &lt;1 ly/s  → AU/s
        ///              → ly/s
        /// Zero speed is handled by the m/s band (no division, T-04-03 mitigation).
        /// </summary>
        public static string FormatSpeed(double metersPerSecond)
        {
            double v = System.Math.Abs(metersPerSecond);
            if (v < 1_000.0) return $"{metersPerSecond:0.#} m/s";
            if (v < AU)      return $"{metersPerSecond / 1_000.0:0.#} km/s";
            if (v < LY)      return $"{metersPerSecond / AU:0.###} AU/s";
            return $"{metersPerSecond / LY:0.###} ly/s";
        }

        /// <summary>
        /// Formats a distance in metres using the same adaptive-unit ladder as
        /// FormatSpeed, but without the "/s" suffix.
        /// Zero distance is handled by the m band (T-04-03 mitigation).
        /// </summary>
        public static string FormatDistance(double meters)
        {
            double v = System.Math.Abs(meters);
            if (v < 1_000.0) return $"{meters:0.#} m";
            if (v < AU)      return $"{meters / 1_000.0:0.#} km";
            if (v < LY)      return $"{meters / AU:0.###} AU";
            return $"{meters / LY:0.###} ly";
        }
    }
}
