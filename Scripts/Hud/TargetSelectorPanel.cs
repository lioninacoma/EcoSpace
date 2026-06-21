using Godot;

namespace Hud
{
    /// <summary>
    /// Cross-space target selector panel (D-54 / 06-02).
    ///
    /// Read-only consumer of GameWorld state — MUST NOT mutate sim state.
    ///
    /// Displays a compact right-side panel grouped by tier (GALAXY / STAR / PLANET),
    /// listing every targetable body with name + live distance. The active target row
    /// is marked with a left-pointing glyph. The player selects via keyboard (Up/Down
    /// + Enter) or mouse click; selection commits exclusively through Hud.SetTargetIndex
    /// (never via direct GameObjects or LocalPos mutation — D-53).
    ///
    /// Toggle: Tab (toggle_target_panel action from 06-01 project.godot).
    ///   Open  → Input.MouseMode = Visible,  MouseFilter = Stop  (rows clickable; steering halts)
    ///   Close → Input.MouseMode = Captured, MouseFilter = Ignore (flight resumes)
    ///
    /// Cursor reconciliation with FlightController T-key mode:
    ///   Both this panel (_UnhandledInput: toggle_target_panel / Tab) and FlightController
    ///   (_UnhandledInput: T key) are in _UnhandledInput but handle DIFFERENT actions/keys,
    ///   so they do not conflict. The panel always re-asserts MouseMode.Visible while open.
    ///   The toggle event is marked handled via GetViewport().SetInputAsHandled() so it
    ///   does not propagate further.
    ///
    /// NEVER writes GameObjects[..] or .LocalPos = (D-53 anti-pattern).
    /// All distances via UniMath.Distance (LCA path — CLAUDE.md §Position Math).
    /// </summary>
    public partial class TargetSelectorPanel : Control
    {
        // ── Exports ───────────────────────────────────────────────────────────

        /// <summary>NodePath to the TestSetup (world) node. Falls back to FindChild("Main").</summary>
        [Export] public NodePath WorldPath { get; set; }

        /// <summary>NodePath to the Hud node for SetTargetIndex / GetTargetCandidates / ActiveTargetIndex.</summary>
        [Export] public NodePath HudPath { get; set; }

        /// <summary>NodePath to the FlightController node (resolved but not mutated — reserved for future use).</summary>
        [Export] public NodePath FlightPath { get; set; }

        /// <summary>Phosphor-green CRT color for the panel (D-09). Defaults to the project standard.</summary>
        [Export] public Color PhosphorGreen { get; set; } = new Color(0.1f, 1.0f, 0.3f);

        // ── Private references ────────────────────────────────────────────────

        private TestSetup _world;
        private Hud _hud;
        // FlightController ref is held but not used to mutate state — reconciliation
        // is achieved by restoring Input.MouseMode, not by calling any FlightController method.
        private Flight.FlightController _flight;

        // ── UI children ───────────────────────────────────────────────────────

        private VBoxContainer _vbox;

        // ── Panel selection state ─────────────────────────────────────────────

        /// <summary>Keyboard highlight cursor within the visible body-row list (not the full VBox).</summary>
        private int _highlightRow = 0;

        // ── Godot callbacks ───────────────────────────────────────────────────

        public override void _Ready()
        {
            // Resolve world reference (same fallback pattern as Hud._Ready)
            if (WorldPath != null && !WorldPath.IsEmpty)
                _world = GetNode<TestSetup>(WorldPath);
            else
                _world = GetTree().Root.FindChild("Main", true, false) as TestSetup;

            // Resolve Hud reference
            if (HudPath != null && !HudPath.IsEmpty)
                _hud = GetNode<Hud>(HudPath);
            else
                _hud = GetTree().Root.FindChild("Hud", true, false) as Hud;

            // Resolve FlightController reference (not mutated — reserved)
            if (FlightPath != null && !FlightPath.IsEmpty)
                _flight = GetNode<Flight.FlightController>(FlightPath);
            else
                _flight = GetTree().Root.FindChild("FlightController", true, false) as Flight.FlightController;

            // Build the VBoxContainer that holds tier headers + body rows
            _vbox = new VBoxContainer();
            AddChild(_vbox);

            // Start hidden; mouse events pass through to nodes below
            Visible = false;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            // Toggle the panel on toggle_target_panel (Tab, from 06-01 project.godot).
            // FlightController also lives in _UnhandledInput but handles the T key —
            // different action, no conflict.
            if (@event.IsActionPressed("toggle_target_panel"))
            {
                if (Visible)
                    ClosePanel();
                else
                    OpenPanel();

                GetViewport().SetInputAsHandled();
                return;
            }

            // Keyboard navigation while panel is open
            if (!Visible) return;

            if (@event.IsActionPressed("ui_up"))
            {
                _highlightRow = Mathf.Max(0, _highlightRow - 1);
                RefreshList();
                GetViewport().SetInputAsHandled();
            }
            else if (@event.IsActionPressed("ui_down"))
            {
                var candidates = _hud?.GetTargetCandidates();
                int count = candidates?.Count ?? 0;
                _highlightRow = Mathf.Min(count - 1, _highlightRow + 1);
                RefreshList();
                GetViewport().SetInputAsHandled();
            }
            else if (@event.IsActionPressed("ui_accept"))
            {
                CommitSelection(_highlightRow);
                GetViewport().SetInputAsHandled();
            }
        }

        public override void _Process(double delta)
        {
            // Refresh distance column while panel is open (live update per plan spec)
            if (Visible)
                RefreshList();
        }

        // ── Open / close ──────────────────────────────────────────────────────

        private void OpenPanel()
        {
            // Sync highlight row to match the currently active target so the panel
            // opens with the correct row pre-selected.
            if (_hud != null && _world != null)
            {
                var objs = _world.GameObjects;
                int shipIdx = _world.ShipIndex;
                if ((uint)shipIdx < (uint)(objs?.Count ?? 0))
                {
                    int activeGobjIdx = _hud.ActiveTargetIndex;
                    var candidates = _hud.GetTargetCandidates();
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (candidates[i] == activeGobjIdx)
                        {
                            _highlightRow = i;
                            break;
                        }
                    }
                }
            }

            Visible = true;
            // MouseFilter.Stop: the panel captures clicks so body rows are clickable
            // and mouse motion does not fall through to FlightController steering.
            MouseFilter = MouseFilterEnum.Stop;
            // Free the cursor — steering stops automatically because FlightController._Input
            // no-ops when MouseMode != Captured (line ~292 FlightController.cs).
            Input.MouseMode = Input.MouseModeEnum.Visible;

            RefreshList();
        }

        private void ClosePanel()
        {
            Visible = false;
            // MouseFilter.Ignore: pass all mouse events through to the scene below
            MouseFilter = MouseFilterEnum.Ignore;
            // Restore flight mouse capture
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        // ── List rendering ────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the VBox contents: title, then tier-grouped body rows with
        /// name + live distance and a highlight glyph on the active and keyboard-selected rows.
        /// Called each _Process while visible so distances stay live.
        /// </summary>
        private void RefreshList()
        {
            if (_vbox == null || _hud == null || _world == null) return;

            var objs = _world.GameObjects;
            int shipIdx = _world.ShipIndex;
            if ((uint)shipIdx >= (uint)(objs?.Count ?? 0)) return;
            var ship = objs[shipIdx];
            if (ship == null) return;

            var candidates = _hud.GetTargetCandidates();
            int activeGobjIdx = _hud.ActiveTargetIndex;

            // ── Clear old children ─────────────────────────────────────────────
            foreach (Node child in _vbox.GetChildren())
                child.QueueFree();

            // ── Title row ─────────────────────────────────────────────────────
            AddRow("TARGETS ────────", bold: true, highlight: false);

            // ── Tier grouping ─────────────────────────────────────────────────
            // Walk candidates (Galaxy→Star→Planet order from GetTargetCandidates).
            // Emit a tier header only when the first body of that tier appears.
            bool galaxyHeaderEmitted = false;
            bool starHeaderEmitted   = false;
            bool planetHeaderEmitted = false;

            for (int candidatePos = 0; candidatePos < candidates.Count; candidatePos++)
            {
                int gobjIdx = candidates[candidatePos];
                if ((uint)gobjIdx >= (uint)objs.Count) continue;
                var body = objs[gobjIdx];
                if (body == null) continue;

                // Emit tier header when the body type changes to a new tier
                switch (body.ObjectType)
                {
                    case UniObject.Type.Galaxy:
                        if (!galaxyHeaderEmitted)
                        {
                            AddRow("GALAXY", bold: true, highlight: false);
                            galaxyHeaderEmitted = true;
                        }
                        break;
                    case UniObject.Type.Star:
                        if (!starHeaderEmitted)
                        {
                            AddRow("STAR", bold: true, highlight: false);
                            starHeaderEmitted = true;
                        }
                        break;
                    case UniObject.Type.Planet:
                    case UniObject.Type.Orb:
                    case UniObject.Type.Asteroid:
                        if (!planetHeaderEmitted)
                        {
                            AddRow("PLANET", bold: true, highlight: false);
                            planetHeaderEmitted = true;
                        }
                        break;
                }

                // Compute live distance via UniMath.Distance (LCA path — never raw LocalPos)
                double dist = UniMath.Distance(ship, body, objs);
                string distStr = Hud.FormatDistance(dist);
                string name = body.Name ?? "?";

                // Active-target marker and keyboard highlight
                bool isActiveTgt  = gobjIdx == activeGobjIdx;
                bool isKeyboardHl = candidatePos == _highlightRow;

                // Format row text:  " ◀ NAME   dist"  or  "   NAME   dist"
                // The active-target glyph (◀) mirrors the mock in 06-CONTEXT.md §Specific Ideas.
                string prefix = isActiveTgt ? " ◀ " : "   ";
                string rowText = $"{prefix}{name}  {distStr}";

                // Keyboard highlight adds a different visual marker
                string displayText = isKeyboardHl ? $">{rowText}" : $" {rowText}";

                var label = AddRow(displayText, bold: false, highlight: isKeyboardHl || isActiveTgt);

                // Wire click selection: capture candidatePos for this iteration
                int capturedPos = candidatePos;
                label.GuiInput += (@inputEvent) =>
                {
                    if (@inputEvent is InputEventMouseButton mb
                        && mb.ButtonIndex == MouseButton.Left
                        && mb.Pressed)
                    {
                        _highlightRow = capturedPos;
                        CommitSelection(capturedPos);
                        GetViewport().SetInputAsHandled();
                    }
                };
                // Enable mouse input on individual rows so clicks register
                label.MouseFilter = MouseFilterEnum.Stop;
            }
        }

        /// <summary>
        /// Adds a Label row to the VBox, styled with PhosphorGreen.
        /// Returns the label so the caller can wire events.
        /// </summary>
        private Label AddRow(string text, bool bold, bool highlight)
        {
            var label = new Label();
            label.Text = text;
            label.Modulate = PhosphorGreen;
            if (highlight)
            {
                // Brighten the active/highlighted row slightly for visibility
                label.Modulate = new Color(
                    Mathf.Min(1f, PhosphorGreen.R + 0.2f),
                    Mathf.Min(1f, PhosphorGreen.G + 0.1f),
                    Mathf.Min(1f, PhosphorGreen.B + 0.2f),
                    PhosphorGreen.A);
            }
            // Default: mouse events pass through (caller overrides for body rows)
            label.MouseFilter = MouseFilterEnum.Ignore;
            _vbox.AddChild(label);
            return label;
        }

        // ── Selection ─────────────────────────────────────────────────────────

        /// <summary>
        /// Commits selection for the given candidate list position.
        /// Writes ONLY through Hud.SetTargetIndex — never mutates GameObjects or LocalPos (D-53).
        /// </summary>
        private void CommitSelection(int candidatePos)
        {
            _hud?.SetTargetIndex(candidatePos);
            _highlightRow = candidatePos;
            // Close the panel after selection so flight resumes immediately
            ClosePanel();
        }
    }
}
