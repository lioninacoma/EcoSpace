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
    ///   • Cross-space target readout over the full hierarchy (D-55/D-56)
    ///   • Off-screen edge direction marker pointing toward active target (findability)
    ///   • Phosphor-green CRT aesthetic throughout (D-09)
    ///   • Panel API: SetTargetIndex / GetTargetCandidates (consumed by TargetSelectorPanel, 06-02)
    ///
    /// Anti-patterns honored:
    ///   • mouse_filter = Ignore on all HUD Controls (set in Main.tscn and here)
    ///   • Reads FlightController.CurrentSpeed (not a delta-position estimate)
    ///   • All index lookups null-guarded via (uint)i cast trick
    ///
    /// Targetable set = ALL bodies in the full hierarchy, ordered Galaxy → Star → Planet (D-55).
    /// All ship-relative distances and vectors use UniMath.RelativeMetres / UniMath.Distance
    /// (LCA-based path) to avoid catastrophic cancellation for cross-space bodies.
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

        /// <summary>Current index into the full-hierarchy candidate list (D-55).</summary>
        private int _targetIndex = 0;

        /// <summary>
        /// Index into GameObjects of the active cross-space target, or -1 if none is available.
        /// Read-only — never mutates _targetIndex or any sim state (Hud is a read-only
        /// consumer; HUD mutating sim state is an anti-pattern). Returns -1 when:
        ///   • _world is null (scene not yet ready)
        ///   • ship index is out of range or ship object is null
        ///   • the full-hierarchy candidate list is empty (no targetable bodies found)
        /// Rebuilds the candidate list fresh each call (cheap — ≤ ~30 bodies in MVP scene)
        /// so a stale _targetIndex after an SOI transition is always clamped to live bounds.
        /// Contract unchanged for FlightController D-43 ease-out consumers.
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
                var targets = BuildFullHierarchyTargetList(shipIdx, objs);
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

            // Find nearest body: scan the full cross-space hierarchy (D-55).
            // UniMath.RelativeMetres uses the LCA path for all bodies — avoids catastrophic
            // cancellation for cross-space bodies and the identical-magnitude tie that
            // occurred when a sibling sat at the parent-frame origin.
            var targets = BuildFullHierarchyTargetList(shipIndex, gameObjects);
            string nearestName = "---";
            double minDist = double.MaxValue;
            foreach (var entry in targets)
            {
                if ((uint)entry.Index >= (uint)gameObjects.Count) continue;
                var body = gameObjects[entry.Index];
                if (body == null) continue;

                // Skip Galaxy bodies as "nearest" candidates (04-02 play-test fix):
                // the galaxy parent sits at the frame origin alongside the home star,
                // so both produced near-identical distances and the nearest label
                // flickered between STAR and HOME GALAXY each frame. A galaxy is a
                // diffuse sky body, not a flyable body you are "near" — exclude it
                // (mirrors the FlightController proximity-damp galaxy exclusion).
                if (body.ObjectType == UniObject.Type.Galaxy) continue;

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
            // Build full-hierarchy candidate list: Galaxy → Star → Planet (D-55).
            var targets = BuildFullHierarchyTargetList(shipIndex, gameObjects);

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

        // ── Panel API (D-55/D-56) ────────────────────────────────────────────

        /// <summary>
        /// Sets the active target to the given index into the full-hierarchy candidate list.
        /// Read-only of sim state — only writes the internal HUD _targetIndex field,
        /// never mutates any GameObjects element, LocalPos, or ChildIndices (D-53).
        /// Called by TargetSelectorPanel (06-02) when the player picks a body in the panel.
        /// No-op when the candidate list is empty.
        /// </summary>
        public void SetTargetIndex(int candidateIndex)
        {
            var candidates = GetTargetCandidates();
            if (candidates.Count == 0) return;
            _targetIndex = Mathf.Clamp(candidateIndex, 0, candidates.Count - 1);
        }

        /// <summary>
        /// Returns the ordered GameObjects indices of every targetable body in the full
        /// hierarchy, in Galaxy → Star → Planet tier order (D-55 / user mock).
        /// Read-only of sim state — builds the list from GameObjects without mutating anything (D-53).
        /// Returns an empty list when the ship or world is not ready.
        /// Consumed by TargetSelectorPanel (06-02) for the candidate list to render.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<int> GetTargetCandidates()
        {
            var result = new System.Collections.Generic.List<int>();
            if (_world == null) return result;
            var objs = _world.GameObjects;
            int shipIdx = _world.ShipIndex;
            if ((uint)shipIdx >= (uint)(objs?.Count ?? 0)) return result;
            var ship = objs[shipIdx];
            if (ship == null) return result;

            var entries = BuildFullHierarchyTargetList(shipIdx, objs);
            foreach (var entry in entries)
                result.Add(entry.Index);
            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the ordered cross-space targetable body list over the full hierarchy (D-55).
        /// Returns entries in Galaxy → Star → Planet tier order (matches user mock in 06-CONTEXT.md).
        ///
        /// Walks ALL entries in gameObjects. Skips:
        ///   • the ship itself (shipIndex)
        ///   • null entries and out-of-range indices ((uint) bounds-check guard)
        ///   • Root-space and Universe-space container objects (not selectable bodies)
        ///   • Ship-type objects
        /// Galaxies ARE included (D-55 — cross-space selection; Galaxy exclusion was current-tier only).
        ///
        /// Read-only consumer — MUST NOT mutate any UniObject or sim state (D-53).
        /// All distance math routes through UniMath.RelativeMetres (LCA path) — never raw
        /// body.LocalPos.ToDouble3() for cross-space bodies.
        /// </summary>
        private System.Collections.Generic.List<TargetEntry> BuildFullHierarchyTargetList(
            int shipIndex,
            System.Collections.Generic.List<UniObject> gameObjects)
        {
            var galaxies = new System.Collections.Generic.List<TargetEntry>();
            var stars    = new System.Collections.Generic.List<TargetEntry>();
            var planets  = new System.Collections.Generic.List<TargetEntry>();

            if (gameObjects == null) return galaxies;   // return empty list

            for (int i = 0; i < gameObjects.Count; i++)
            {
                if (i == shipIndex) continue;
                if ((uint)i >= (uint)gameObjects.Count) continue;   // belt-and-suspenders
                var body = gameObjects[i];
                if (body == null) continue;

                // Skip non-selectable types: Ship, None, and container spaces (Root/Universe).
                // Root and Universe objects have ObjectType None or are pure container nodes.
                var objType = body.ObjectType;
                if (objType == UniObject.Type.Ship) continue;
                if (objType == UniObject.Type.None) continue;

                // Skip bodies in Root space (index 0 / container with no physical presence).
                if (body.CurrentSpace == UniObject.Space.Root) continue;
                if (body.CurrentSpace == UniObject.Space.Universe) continue;

                // Bucket by body type in tier order: Galaxy → Star → Planet.
                // Orb/Asteroid treated as planets (selectable physical bodies).
                switch (objType)
                {
                    case UniObject.Type.Galaxy:
                        galaxies.Add(new TargetEntry(i));
                        break;
                    case UniObject.Type.Star:
                        stars.Add(new TargetEntry(i));
                        break;
                    case UniObject.Type.Planet:
                    case UniObject.Type.Orb:
                    case UniObject.Type.Asteroid:
                        planets.Add(new TargetEntry(i));
                        break;
                }
            }

            // Merge in tier order: Galaxy, Star, Planet.
            var result = new System.Collections.Generic.List<TargetEntry>(
                galaxies.Count + stars.Count + planets.Count);
            result.AddRange(galaxies);
            result.AddRange(stars);
            result.AddRange(planets);
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
