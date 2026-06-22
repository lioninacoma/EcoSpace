using Godot;

namespace Hud
{
    /// <summary>
    /// Warp confirmation panel (Phase 7, Plan 02).
    ///
    /// Read-only consumer of GameWorld state — MUST NOT mutate sim state (D-53).
    /// NEVER writes GameObjects[..], .LocalPos, or .ChildIndices.
    ///
    /// Opens on J (warp_engage) when an active target is set (D-02); no-op otherwise.
    /// Shows: target name, distance, travel-time slider (1–60 min, default 2 min), computed warp speed.
    /// Enter: ClosePanel() THEN EngageWarp(_selectedTravelTimeSec) — order is critical (Pitfall 3 / T-07-09).
    /// Esc / J while open: cancel (ClosePanel).
    ///
    /// Mirrors TargetSelectorPanel exactly for structure, lifecycle, and phosphor-green style.
    /// All distances via UniMath.Distance only (LCA path — CLAUDE.md §Position Math).
    /// </summary>
    public partial class WarpConfirmationScreen : Control
    {
        // ── Exports ───────────────────────────────────────────────────────────

        /// <summary>NodePath to the TestSetup (world) node. Falls back to FindChild("Main").</summary>
        [Export] public NodePath WorldPath { get; set; }

        /// <summary>NodePath to the Hud node for ActiveTargetIndex / FormatSpeed / FormatDistance.</summary>
        [Export] public NodePath HudPath { get; set; }

        /// <summary>NodePath to the FlightController node (for IsPanelOpen gate and EngageWarp).</summary>
        [Export] public NodePath FlightPath { get; set; }

        /// <summary>Phosphor-green CRT color for the panel. Defaults to the project standard.</summary>
        [Export] public Color PhosphorGreen { get; set; } = new Color(0.1f, 1.0f, 0.3f);

        // ── Private references ────────────────────────────────────────────────

        private TestSetup _world;
        private Hud _hud;
        private Flight.FlightController _flight;

        // ── UI children ───────────────────────────────────────────────────────

        private VBoxContainer _vbox;
        private HSlider _timeSlider;
        private Label _targetLabel;
        private Label _distLabel;
        private Label _timeLabel;
        private Label _speedLabel;

        // ── Runtime state ─────────────────────────────────────────────────────

        /// <summary>
        /// Selected travel time in seconds (D-17). Default 120 s = 2 minutes.
        /// Not persisted between sessions; reset to default on next _Ready.
        /// Slider operates directly in seconds (5 s–3600 s, step 5 s).
        /// </summary>
        private double _selectedTravelTimeSec = 120.0;

        // ── Godot callbacks ───────────────────────────────────────────────────

        public override void _Ready()
        {
            // Resolve world reference (FindChild fallback mirrors TargetSelectorPanel pattern)
            if (WorldPath != null && !WorldPath.IsEmpty)
                _world = GetNode<TestSetup>(WorldPath);
            else
                _world = GetTree().Root.FindChild("Main", true, false) as TestSetup;

            // Resolve Hud reference
            if (HudPath != null && !HudPath.IsEmpty)
                _hud = GetNode<Hud>(HudPath);
            else
                _hud = GetTree().Root.FindChild("Hud", true, false) as Hud;

            // Resolve FlightController reference
            if (FlightPath != null && !FlightPath.IsEmpty)
                _flight = GetNode<Flight.FlightController>(FlightPath);
            else
                _flight = GetTree().Root.FindChild("FlightController", true, false) as Flight.FlightController;

            // Build the VBoxContainer
            _vbox = new VBoxContainer();
            AddChild(_vbox);

            // ── Build panel rows ───────────────────────────────────────────────

            // Title
            AddRow("WARP DRIVE", highlight: true);

            // Target name row
            _targetLabel = AddRow("TARGET  ---", highlight: false);

            // Distance row
            _distLabel = AddRow("DIST  ---", highlight: false);

            // Travel time row
            _timeLabel = AddRow("TIME  2m 0s", highlight: false);

            // Travel time slider — operates in seconds (5 s–3600 s = 1 h, step 5 s, default 120 s).
            // Direct second values let the player dial short warps (e.g. 30 s to a nearby planet).
            _timeSlider = new HSlider();
            _timeSlider.MinValue = 5.0;
            _timeSlider.MaxValue = 3600.0;
            _timeSlider.Step = 5.0;
            _timeSlider.Value = 120.0;
            _timeSlider.MouseFilter = MouseFilterEnum.Stop;
            // Disable keyboard focus so the HSlider never consumes ui_accept (Enter) before
            // _UnhandledInput fires — that was the bug preventing warp from engaging.
            _timeSlider.FocusMode = FocusModeEnum.None;
            _vbox.AddChild(_timeSlider);

            // Subscribe ValueChanged AFTER label refs exist — guard against early fire (Assumption A4).
            _timeSlider.ValueChanged += (double value) =>
            {
                _selectedTravelTimeSec = value;   // slider is already in seconds
                // Guard: refs may not be ready if ValueChanged fires during initialization
                if (_speedLabel != null && _hud != null)
                    RefreshDisplay();
            };

            // Computed warp speed row
            _speedLabel = AddRow("WARP  ---", highlight: false);

            // Hint row — slider range hint (5 s–60 min) and key bindings
            AddRow("5s–60m  ENTER engage  ESC/J cancel", highlight: false);

            // Start hidden; mouse events pass through (D-16)
            Visible = false;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            // J key (warp_engage): toggle the panel.
            // If visible → cancel (close). If not visible → open only when target is set (D-02).
            if (@event.IsActionPressed("warp_engage"))
            {
                if (Visible)
                    ClosePanel();
                else if (_hud?.ActiveTargetIndex >= 0)
                    OpenPanel();

                GetViewport().SetInputAsHandled();
                return;
            }

            // All other navigation only matters while panel is visible.
            if (!Visible) return;

            if (@event.IsActionPressed("ui_cancel"))
            {
                // Esc: cancel warp, resume manual flight.
                ClosePanel();
                GetViewport().SetInputAsHandled();
            }
            else if (@event.IsActionPressed("ui_accept"))
            {
                // Enter: engage warp.
                // ORDER IS CRITICAL (Pitfall 3 / T-07-09): ClosePanel FIRST (sets IsPanelOpen=false),
                // then EngageWarp — so _Process is not gated when warp begins.
                ClosePanel();
                _flight?.EngageWarp(_selectedTravelTimeSec);
                GetViewport().SetInputAsHandled();
            }
        }

        // ── Open / close ──────────────────────────────────────────────────────

        /// <summary>
        /// Opens the warp confirmation panel. Sets IsPanelOpen=true to suppress flight input
        /// while the player configures travel time (D-04).
        /// </summary>
        private void OpenPanel()
        {
            Visible = true;
            MouseFilter = MouseFilterEnum.Stop;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            if (_flight != null) _flight.IsPanelOpen = true;
            RefreshDisplay();
        }

        /// <summary>
        /// Closes the warp confirmation panel. Sets IsPanelOpen=false to restore flight input (D-04).
        /// Called before EngageWarp on Enter, and directly on Esc/J cancel.
        /// </summary>
        private void ClosePanel()
        {
            Visible = false;
            MouseFilter = MouseFilterEnum.Ignore;
            Input.MouseMode = Input.MouseModeEnum.Captured;
            if (_flight != null) _flight.IsPanelOpen = false;
        }

        // ── Display refresh ───────────────────────────────────────────────────

        /// <summary>
        /// Updates all display rows from current world state. Read-only — never mutates
        /// GameObjects, LocalPos, or ChildIndices (D-53).
        /// All distances computed via UniMath.Distance (LCA path — CLAUDE.md §Position Math).
        /// </summary>
        private void RefreshDisplay()
        {
            if (_world == null || _hud == null) return;

            var gameObjects = _world.GameObjects;
            int shipIdx = _world.ShipIndex;

            // Bounds-safe ship lookup
            var ship = (uint)shipIdx < (uint)(gameObjects?.Count ?? 0) ? gameObjects[shipIdx] : null;
            if (ship == null) { ShowNoTarget(); return; }

            // Bounds-safe target lookup from Hud.ActiveTargetIndex
            int tgtIdx = _hud.ActiveTargetIndex;
            var target = (tgtIdx >= 0 && (uint)tgtIdx < (uint)gameObjects.Count) ? gameObjects[tgtIdx] : null;
            if (target == null) { ShowNoTarget(); return; }

            // Target name
            if (_targetLabel != null)
                _targetLabel.Text = $"TARGET  {target.Name ?? "?"}";

            // Distance via UniMath.Distance (LCA path — the only safe cross-frame distance)
            double dist = UniMath.Distance(ship, target, gameObjects);
            if (_distLabel != null)
                _distLabel.Text = $"DIST  {Hud.FormatDistance(dist)}";

            // Travel time display — show as "Xs" for < 60 s, or "Xm Ys" for >= 60 s.
            if (_timeLabel != null)
                _timeLabel.Text = $"TIME  {FormatTravelTime(_selectedTravelTimeSec)}";

            // Peak cruise speed v_c = (profileDist − rampContrib) / (T_sel·(1−f)) (D-12).
            // Shows the maximum speed the ship reaches during this warp leg.
            // WarpMaxSpeed cap removed (D-05); _flight?.WarpAccelFraction provides f.
            double profileDist = System.Math.Max(0.0, dist - target.SOIMeters);
            double f           = _flight?.WarpAccelFraction ?? (1.0 / 3.0);
            double tSel        = System.Math.Max(1.0, _selectedTravelTimeSec);
            double rampContrib = f * tSel * 2.0 * (_flight?.ManualMaxSpeed ?? 1e6) * 0.5;
            double vc          = System.Math.Max(0.0, (profileDist - rampContrib)) / System.Math.Max(1e-11, tSel * (1.0 - f));
            if (_speedLabel != null)
                _speedLabel.Text = $"WARP  {Hud.FormatSpeed(vc)}";
        }

        /// <summary>
        /// Shows a "no target" placeholder state on all data rows.
        /// Called when ship or target reference is unavailable.
        /// </summary>
        private void ShowNoTarget()
        {
            if (_targetLabel != null) _targetLabel.Text = "TARGET  NO TARGET";
            if (_distLabel   != null) _distLabel.Text   = "DIST  ---";
            if (_timeLabel   != null) _timeLabel.Text   = "TIME  ---";
            if (_speedLabel  != null) _speedLabel.Text  = "WARP  ---";
        }

        // ── Formatters ────────────────────────────────────────────────────────

        /// <summary>
        /// Formats a travel time in seconds as a human-readable string.
        /// Under 60 s: "Xs". 60 s or more: "Xm Ys".
        /// Examples: "30s", "2m 0s", "1h 5m 0s" (for >= 3600 s).
        /// </summary>
        private static string FormatTravelTime(double seconds)
        {
            int totalSec = (int)System.Math.Round(seconds);
            if (totalSec < 60)
                return $"{totalSec}s";
            int hours = totalSec / 3600;
            int mins  = (totalSec % 3600) / 60;
            int secs  = totalSec % 60;
            if (hours > 0)
                return $"{hours}h {mins}m {secs}s";
            return $"{mins}m {secs}s";
        }

        // ── Row builder ───────────────────────────────────────────────────────

        /// <summary>
        /// Adds a Label row to the VBox, styled with PhosphorGreen.
        /// Copied from TargetSelectorPanel.AddRow for consistent phosphor-green styling.
        /// Returns the label so the caller can store a reference for live updates.
        /// </summary>
        private Label AddRow(string text, bool highlight)
        {
            var label = new Label();
            label.Text = text;
            label.Modulate = highlight
                ? new Color(
                    Mathf.Min(1f, PhosphorGreen.R + 0.2f),
                    Mathf.Min(1f, PhosphorGreen.G + 0.1f),
                    Mathf.Min(1f, PhosphorGreen.B + 0.2f),
                    PhosphorGreen.A)
                : PhosphorGreen;
            label.MouseFilter = MouseFilterEnum.Ignore;
            _vbox.AddChild(label);
            return label;
        }
    }
}
