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
    ///   • Cycle-able target readout over current-space siblings (D-12)
    ///   • Off-screen edge direction marker pointing toward active target (findability)
    ///   • Phosphor-green CRT aesthetic throughout (D-09)
    ///
    /// Anti-patterns honored:
    ///   • mouse_filter = Ignore on all HUD Controls (set in Main.tscn and here)
    ///   • Reads FlightController.CurrentSpeed (not a delta-position estimate)
    ///   • All index lookups null-guarded via (uint)i cast trick
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

        // ── HUD label nodes ───────────────────────────────────────────────────

        private Label _speedLabel;
        private Label _contextLabel;
        private Label _targetLabel;
        private Control _dirMarker;   // off-screen direction arrow container

        // ── Target cycling state ──────────────────────────────────────────────

        /// <summary>Current index into the list of cycle-able siblings.</summary>
        private int _targetIndex = 0;

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

            // Find nearest body: scan parent's ChildIndices, skip ship, skip null
            int parentIdx = ship.ParentIndex;
            string nearestName = "---";
            if ((uint)parentIdx < (uint)gameObjects.Count)
            {
                var parent = gameObjects[parentIdx];
                if (parent != null)
                {
                    double minDist = double.MaxValue;
                    foreach (int idx in parent.ChildIndices)
                    {
                        if (idx == shipIndex) continue;
                        if ((uint)idx >= (uint)gameObjects.Count) continue;
                        var body = gameObjects[idx];
                        if (body == null) continue;

                        double dist = UniVec3.Distance(ship.LocalPos, body.LocalPos);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearestName = body.Name ?? "?";
                        }
                    }
                }
            }

            _contextLabel.Text = $"{tier} · nearest: {nearestName}";
        }

        // ── Target cycle + readout (D-12) ─────────────────────────────────────

        private void UpdateTargetReadout(UniObject ship, System.Collections.Generic.List<UniObject> gameObjects, int shipIndex)
        {
            // Build current-space siblings list (excluding the ship, null-skipping)
            int parentIdx = ship.ParentIndex;
            var siblings = BuildSiblingList(parentIdx, shipIndex, gameObjects);

            // Clamp/reset _targetIndex across SOI transitions (T-04-02 mitigation)
            if (siblings.Count == 0)
            {
                _targetIndex = 0;
                if (_targetLabel != null) _targetLabel.Text = "TGT  ---";
                HideMarker();
                return;
            }

            _targetIndex = Mathf.Clamp(_targetIndex, 0, siblings.Count - 1);
            int targetObjIdx = siblings[_targetIndex];
            var targetObj = (uint)targetObjIdx < (uint)gameObjects.Count ? gameObjects[targetObjIdx] : null;

            if (targetObj == null)
            {
                if (_targetLabel != null) _targetLabel.Text = "TGT  ---";
                HideMarker();
                return;
            }

            // Distance readout
            double distM = UniVec3.Distance(ship.LocalPos, targetObj.LocalPos);
            if (_targetLabel != null)
                _targetLabel.Text = $"TGT  {targetObj.Name ?? "?"} · {FormatDistance(distM)}";

            // Off-screen direction marker
            UpdateDirectionMarker(ship, targetObj);
        }

        // ── Off-screen direction marker ───────────────────────────────────────

        private void UpdateDirectionMarker(UniObject ship, UniObject targetObj)
        {
            if (_dirMarker == null || _camera == null) return;

            // Get target direction in world-render space (ship-relative meters)
            Double3 relD = targetObj.LocalPos.ToLocalDouble(ship.LocalPos);
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

        // ── Input: cycle target ───────────────────────────────────────────────

        public override void _Input(InputEvent @event)
        {
            // cycle_target must be read in _Input (not _UnhandledInput) so it
            // is not swallowed by Controls with mouse_filter != Ignore.
            if (@event.IsActionPressed("cycle_target"))
            {
                if (_world == null) return;
                var gameObjects = _world.GameObjects;
                int shipIndex = _world.ShipIndex;
                if ((uint)shipIndex >= (uint)(gameObjects?.Count ?? 0)) return;
                var ship = gameObjects[shipIndex];
                if (ship == null) return;

                var siblings = BuildSiblingList(ship.ParentIndex, shipIndex, gameObjects);
                if (siblings.Count > 0)
                    _targetIndex = (_targetIndex + 1) % siblings.Count;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Builds a list of current-space sibling indices (ship excluded, null-skipped).</summary>
        private System.Collections.Generic.List<int> BuildSiblingList(
            int parentIdx, int shipIndex,
            System.Collections.Generic.List<UniObject> gameObjects)
        {
            var result = new System.Collections.Generic.List<int>();
            if ((uint)parentIdx >= (uint)(gameObjects?.Count ?? 0)) return result;
            var parent = gameObjects[parentIdx];
            if (parent == null) return result;

            foreach (int idx in parent.ChildIndices)
            {
                if (idx == shipIndex) continue;
                if ((uint)idx >= (uint)gameObjects.Count) continue;
                if (gameObjects[idx] == null) continue;
                result.Add(idx);
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
