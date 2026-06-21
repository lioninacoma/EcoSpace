using Godot;
using System.Collections.Generic;

namespace Hud
{
    /// <summary>
    /// Cross-space target selector panel (D-54 / 06-02 rework).
    ///
    /// Read-only consumer of GameWorld state — MUST NOT mutate sim state.
    ///
    /// Displays a compact LEFT-side hierarchical tree panel: galaxies expand into
    /// stars, stars expand into planets. ALL body types (galaxy, star, planet) are
    /// selectable targets via Enter. Galaxies are ALSO navigable containers via d/Right.
    /// Selection commits exclusively through Hud.SetTargetIndex (never via direct
    /// GameObjects or LocalPos mutation — D-53).
    ///
    /// Toggle: Tab (toggle_target_panel action from 06-01 project.godot).
    ///   Open  → Input.MouseMode = Visible,  MouseFilter = Stop  (rows clickable)
    ///   Close → Input.MouseMode = Captured, MouseFilter = Ignore (flight resumes)
    ///
    /// Tree navigation (WASD and arrow keys both work):
    ///   w / Up    = move highlight UP within current level
    ///   s / Down  = move highlight DOWN within current level
    ///   a / Left  = go UP one tree level (back to parent / collapse branch)
    ///   d / Right = descend into highlighted node's children
    ///   Enter     = SELECT highlighted node (galaxies, stars, and planets are all selectable)
    ///
    /// Flight input suppression:
    ///   All FlightController input (WASD throttle/roll, mouse steering) is suppressed
    ///   while the panel is open via the FlightController.IsPanelOpen gate property.
    ///
    /// Cursor reconciliation with FlightController T-key mode:
    ///   Both this panel (_UnhandledInput: toggle_target_panel / Tab) and FlightController
    ///   (_UnhandledInput: T key) are in _UnhandledInput but handle DIFFERENT actions/keys,
    ///   so they do not conflict. The panel asserts MouseMode.Visible while open.
    ///   The toggle event is marked handled via GetViewport().SetInputAsHandled().
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

        /// <summary>NodePath to the FlightController node (for IsPanelOpen flight-gate).</summary>
        [Export] public NodePath FlightPath { get; set; }

        /// <summary>Phosphor-green CRT color for the panel (D-09). Defaults to the project standard.</summary>
        [Export] public Color PhosphorGreen { get; set; } = new Color(0.1f, 1.0f, 0.3f);

        // ── Private references ────────────────────────────────────────────────

        private TestSetup _world;
        private Hud _hud;
        private Flight.FlightController _flight;

        // ── UI children ───────────────────────────────────────────────────────

        private VBoxContainer _vbox;

        // ── Tree state ────────────────────────────────────────────────────────

        /// <summary>
        /// Which level we are browsing:
        ///   0 = galaxy list (top level)
        ///   1 = star list under _expandedGalaxyIdx
        ///   2 = planet list under _expandedStarIdx
        /// </summary>
        private int _treeLevel = 0;

        /// <summary>Highlight row index within the current level's visible list.</summary>
        private int _highlightRow = 0;

        /// <summary>GameObjects index of the galaxy whose children are currently expanded (level 1).</summary>
        private int _expandedGalaxyIdx = -1;

        /// <summary>GameObjects index of the star whose children are currently expanded (level 2).</summary>
        private int _expandedStarIdx = -1;

        // ── Flat list for the currently visible level (rebuilt each frame while open) ──

        /// <summary>
        /// Each entry in the current level's visible list:
        ///   GobjIndex    = GameObjects index of the body
        ///   CandidatePos = position in Hud.GetTargetCandidates() (for SetTargetIndex).
        ///                  All body types (galaxy, star, planet) are selectable; -1 only
        ///                  when a body is not in the candidate list (should not happen in
        ///                  normal operation).
        /// </summary>
        private readonly struct LevelEntry
        {
            public readonly int GobjIndex;
            public readonly int CandidatePos;
            public LevelEntry(int gobjIndex, int candidatePos) { GobjIndex = gobjIndex; CandidatePos = candidatePos; }
        }

        private List<LevelEntry> _currentLevelList = new List<LevelEntry>();

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

            // Resolve FlightController reference (for IsPanelOpen flight-gate)
            if (FlightPath != null && !FlightPath.IsEmpty)
                _flight = GetNode<Flight.FlightController>(FlightPath);
            else
                _flight = GetTree().Root.FindChild("FlightController", true, false) as Flight.FlightController;

            // Build the VBoxContainer that holds tree rows
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

            // Keyboard navigation while panel is open.
            // Both WASD and arrow keys work. WASD is consumed here before it
            // reaches FlightController because IsPanelOpen suppresses FlightController
            // input polling — the WASD actions are still dispatched by Godot but the
            // FlightController early-returns on IsPanelOpen.
            if (!Visible) return;

            bool isUp    = @event.IsActionPressed("ui_up")   || (@event is InputEventKey k1 && k1.PhysicalKeycode == Key.W && k1.Pressed && !k1.Echo);
            bool isDown  = @event.IsActionPressed("ui_down")  || (@event is InputEventKey k2 && k2.PhysicalKeycode == Key.S && k2.Pressed && !k2.Echo);
            bool isLeft  = @event.IsActionPressed("ui_left")  || (@event is InputEventKey k3 && k3.PhysicalKeycode == Key.A && k3.Pressed && !k3.Echo);
            bool isRight = @event.IsActionPressed("ui_right") || (@event is InputEventKey k4 && k4.PhysicalKeycode == Key.D && k4.Pressed && !k4.Echo);

            if (isUp)
            {
                _highlightRow = Mathf.Max(0, _highlightRow - 1);
                RefreshList();
                GetViewport().SetInputAsHandled();
            }
            else if (isDown)
            {
                _highlightRow = Mathf.Min(_currentLevelList.Count - 1, _highlightRow + 1);
                RefreshList();
                GetViewport().SetInputAsHandled();
            }
            else if (isLeft)
            {
                NavigateUp();
                GetViewport().SetInputAsHandled();
            }
            else if (isRight)
            {
                NavigateDown();
                GetViewport().SetInputAsHandled();
            }
            else if (@event.IsActionPressed("ui_accept"))
            {
                CommitSelection();
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
            // Reset tree to galaxy level when opening
            _treeLevel = 0;
            _expandedGalaxyIdx = -1;
            _expandedStarIdx = -1;
            _highlightRow = 0;

            // Pre-select the row that matches the current active target if possible
            SyncHighlightToActiveTarget();

            Visible = true;
            // MouseFilter.Stop: the panel captures clicks so body rows are clickable
            MouseFilter = MouseFilterEnum.Stop;
            // Free the cursor
            Input.MouseMode = Input.MouseModeEnum.Visible;
            // Notify FlightController to suppress all flight input
            if (_flight != null) _flight.IsPanelOpen = true;

            RefreshList();
        }

        private void ClosePanel()
        {
            Visible = false;
            MouseFilter = MouseFilterEnum.Ignore;
            // Restore flight mouse capture
            Input.MouseMode = Input.MouseModeEnum.Captured;
            // Re-enable FlightController input
            if (_flight != null) _flight.IsPanelOpen = false;
        }

        // ── Tree navigation ────────────────────────────────────────────────────

        /// <summary>
        /// Navigates up one tree level (a / Left).
        /// Level 2 → 1: collapse planets back to star list.
        /// Level 1 → 0: collapse stars back to galaxy list.
        /// Level 0: already at root — no-op.
        /// </summary>
        private void NavigateUp()
        {
            if (_treeLevel == 0) return;

            if (_treeLevel == 2)
            {
                // Capture the star index before clearing it, so we can find its row after level change.
                int prevStarIdx = _expandedStarIdx;
                _treeLevel = 1;
                _expandedStarIdx = -1;
                // Rebuild the star list (level 1), then move highlight to the star we were under.
                BuildCurrentLevelList();
                _highlightRow = prevStarIdx >= 0 ? Mathf.Max(0, FindGobjInCurrentList(prevStarIdx)) : 0;
            }
            else // level 1 → 0
            {
                // Capture the galaxy index before clearing it.
                int prevGalIdx = _expandedGalaxyIdx;
                _treeLevel = 0;
                _expandedGalaxyIdx = -1;
                _expandedStarIdx = -1;
                // Rebuild the galaxy list (level 0), then move highlight to the galaxy we were under.
                BuildCurrentLevelList();
                _highlightRow = prevGalIdx >= 0 ? Mathf.Max(0, FindGobjInCurrentList(prevGalIdx)) : 0;
            }

            _highlightRow = Mathf.Clamp(_highlightRow, 0, Mathf.Max(0, _currentLevelList.Count - 1));
            RefreshList();
        }

        /// <summary>
        /// Navigates down into the highlighted node's children (d / Right).
        /// Level 0 → 1: expand a galaxy's stars.
        /// Level 1 → 2: expand a star's planets.
        /// Level 2: planets have no children — no-op (select with Enter instead).
        /// Galaxies are containers, not selectable targets.
        /// </summary>
        private void NavigateDown()
        {
            if (_currentLevelList.Count == 0) return;
            _highlightRow = Mathf.Clamp(_highlightRow, 0, _currentLevelList.Count - 1);
            var entry = _currentLevelList[_highlightRow];

            if (_treeLevel == 0)
            {
                // Descend into galaxy's children (stars in this galaxy)
                var objs = _world?.GameObjects;
                if (objs == null) return;
                if ((uint)entry.GobjIndex >= (uint)objs.Count) return;
                var gobj = objs[entry.GobjIndex];
                if (gobj == null || gobj.ObjectType != UniObject.Type.Galaxy) return;

                _expandedGalaxyIdx = entry.GobjIndex;
                _treeLevel = 1;
                _highlightRow = 0;
                BuildCurrentLevelList();
                RefreshList();
            }
            else if (_treeLevel == 1)
            {
                // Descend into star's children (planets)
                var objs = _world?.GameObjects;
                if (objs == null) return;
                if ((uint)entry.GobjIndex >= (uint)objs.Count) return;
                var gobj = objs[entry.GobjIndex];
                if (gobj == null || gobj.ObjectType != UniObject.Type.Star) return;

                // Only descend if this star has planet children
                BuildPlanetListForStar(entry.GobjIndex, out var planets);
                if (planets.Count == 0) return;

                _expandedStarIdx = entry.GobjIndex;
                _treeLevel = 2;
                _highlightRow = 0;
                BuildCurrentLevelList();
                RefreshList();
            }
            // Level 2: planets have no children; player uses Enter to select
        }

        // ── Selection ─────────────────────────────────────────────────────────

        /// <summary>
        /// Commits selection for the currently highlighted row.
        /// ALL body types (galaxy, star, planet) are selectable via Enter.
        /// Writes ONLY through Hud.SetTargetIndex — never mutates GameObjects or LocalPos (D-53).
        /// </summary>
        private void CommitSelection()
        {
            if (_currentLevelList.Count == 0) return;
            _highlightRow = Mathf.Clamp(_highlightRow, 0, _currentLevelList.Count - 1);
            var entry = _currentLevelList[_highlightRow];

            // All selectable bodies have a valid CandidatePos in the full-hierarchy list.
            // -1 means the body was not found in Hud.GetTargetCandidates() — skip gracefully.
            if (entry.CandidatePos < 0) return;

            _hud?.SetTargetIndex(entry.CandidatePos);
            // Close the panel after selection so flight resumes immediately
            ClosePanel();
        }

        // ── List building ─────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds _currentLevelList from current tree state without rendering.
        /// Called by navigation handlers before RefreshList.
        /// </summary>
        private void BuildCurrentLevelList()
        {
            _currentLevelList.Clear();
            var objs = _world?.GameObjects;
            if (objs == null) return;
            var candidates = _hud?.GetTargetCandidates();

            if (_treeLevel == 0)
            {
                // Top level: show all galaxies.
                // Galaxies live in Universe space (children of Root → ChildSpace(Root) = Universe),
                // so we match on ObjectType alone — NOT CurrentSpace.
                // The old CurrentSpace == Universe guard was wrong: it excluded every galaxy.
                for (int i = 0; i < objs.Count; i++)
                {
                    if ((uint)i >= (uint)objs.Count) continue;
                    var body = objs[i];
                    if (body == null) continue;
                    if (body.ObjectType != UniObject.Type.Galaxy) continue;
                    // Skip the Root container (index 0, parentIndex -1 / ObjectType None) — already
                    // excluded by the Galaxy type check above. No CurrentSpace filter needed.
                    int candPos = FindCandidatePos(i, candidates);
                    _currentLevelList.Add(new LevelEntry(i, candPos));
                }
            }
            else if (_treeLevel == 1 && _expandedGalaxyIdx >= 0)
            {
                // Show stars that are children of the expanded galaxy
                if ((uint)_expandedGalaxyIdx < (uint)objs.Count)
                {
                    var galaxy = objs[_expandedGalaxyIdx];
                    if (galaxy != null)
                    {
                        foreach (int childIdx in galaxy.ChildIndices)
                        {
                            if ((uint)childIdx >= (uint)objs.Count) continue;
                            var child = objs[childIdx];
                            if (child == null) continue;
                            if (child.ObjectType != UniObject.Type.Star) continue;
                            int candPos = FindCandidatePos(childIdx, candidates);
                            _currentLevelList.Add(new LevelEntry(childIdx, candPos));
                        }
                    }
                }
            }
            else if (_treeLevel == 2 && _expandedStarIdx >= 0)
            {
                // Show planets that are children of the expanded star
                BuildPlanetListForStar(_expandedStarIdx, out var planets);
                _currentLevelList.AddRange(planets);
            }
        }

        private void BuildPlanetListForStar(int starGobjIdx, out List<LevelEntry> planets)
        {
            planets = new List<LevelEntry>();
            var objs = _world?.GameObjects;
            if (objs == null || (uint)starGobjIdx >= (uint)objs.Count) return;
            var star = objs[starGobjIdx];
            if (star == null) return;
            var candidates = _hud?.GetTargetCandidates();

            foreach (int childIdx in star.ChildIndices)
            {
                if ((uint)childIdx >= (uint)objs.Count) continue;
                var child = objs[childIdx];
                if (child == null) continue;
                if (child.ObjectType == UniObject.Type.Ship) continue;
                if (child.ObjectType == UniObject.Type.None) continue;
                // Planets, Orbs, Asteroids are selectable
                if (child.ObjectType == UniObject.Type.Planet ||
                    child.ObjectType == UniObject.Type.Orb    ||
                    child.ObjectType == UniObject.Type.Asteroid)
                {
                    int candPos = FindCandidatePos(childIdx, candidates);
                    planets.Add(new LevelEntry(childIdx, candPos));
                }
            }
        }

        /// <summary>
        /// Finds the candidate list position for a given GameObjects index.
        /// Returns -1 if not found (non-selectable or galaxy).
        /// </summary>
        private static int FindCandidatePos(int gobjIdx, System.Collections.Generic.IReadOnlyList<int> candidates)
        {
            if (candidates == null) return -1;
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i] == gobjIdx) return i;
            return -1;
        }

        /// <summary>
        /// Tries to find the row in the CURRENT level list whose GobjIndex matches gobjIdx.
        /// Returns 0 if not found (safe default).
        /// </summary>
        private int FindGobjInCurrentList(int gobjIdx)
        {
            for (int i = 0; i < _currentLevelList.Count; i++)
                if (_currentLevelList[i].GobjIndex == gobjIdx) return i;
            return 0;
        }

        /// <summary>
        /// When opening the panel, tries to set _treeLevel and _highlightRow so the
        /// currently active target is already highlighted (QoL: panel opens at the active body).
        /// Falls back to level 0 / row 0 if the active target cannot be located.
        /// </summary>
        private void SyncHighlightToActiveTarget()
        {
            if (_hud == null || _world == null) return;
            int activeGobjIdx = _hud.ActiveTargetIndex;
            if (activeGobjIdx < 0) return;
            var objs = _world.GameObjects;
            if (objs == null || (uint)activeGobjIdx >= (uint)objs.Count) return;
            var activeObj = objs[activeGobjIdx];
            if (activeObj == null) return;

            // If it's a planet/orb/asteroid, open at planet level under the parent star
            if (activeObj.ObjectType == UniObject.Type.Planet ||
                activeObj.ObjectType == UniObject.Type.Orb    ||
                activeObj.ObjectType == UniObject.Type.Asteroid)
            {
                // Find the star parent of this planet
                int starIdx = activeObj.ParentIndex;
                if ((uint)starIdx < (uint)objs.Count)
                {
                    var star = objs[starIdx];
                    if (star != null && star.ObjectType == UniObject.Type.Star)
                    {
                        // Find the galaxy parent of this star
                        int galIdx = star.ParentIndex;
                        if ((uint)galIdx < (uint)objs.Count)
                        {
                            _expandedGalaxyIdx = galIdx;
                            _expandedStarIdx = starIdx;
                            _treeLevel = 2;
                            BuildCurrentLevelList();
                            _highlightRow = Mathf.Max(0, FindGobjInCurrentList(activeGobjIdx));
                            return;
                        }
                    }
                }
            }

            // If it's a star, open at star level under the parent galaxy
            if (activeObj.ObjectType == UniObject.Type.Star)
            {
                int galIdx = activeObj.ParentIndex;
                if ((uint)galIdx < (uint)objs.Count)
                {
                    _expandedGalaxyIdx = galIdx;
                    _treeLevel = 1;
                    BuildCurrentLevelList();
                    _highlightRow = Mathf.Max(0, FindGobjInCurrentList(activeGobjIdx));
                    return;
                }
            }

            // If it's a galaxy (now selectable), open at galaxy level with the galaxy highlighted
            if (activeObj.ObjectType == UniObject.Type.Galaxy)
            {
                _treeLevel = 0;
                _expandedGalaxyIdx = -1;
                _expandedStarIdx = -1;
                BuildCurrentLevelList();
                _highlightRow = Mathf.Max(0, FindGobjInCurrentList(activeGobjIdx));
                return;
            }

            // Fallback: galaxy level, row 0
            _treeLevel = 0;
            _expandedGalaxyIdx = -1;
            _expandedStarIdx = -1;
            _highlightRow = 0;
        }

        // ── List rendering ────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the VBox contents: breadcrumb header, then current-level rows
        /// with name + live distance and a highlight glyph on the active and
        /// keyboard-selected rows. Called each _Process while visible.
        /// </summary>
        private void RefreshList()
        {
            if (_vbox == null || _hud == null || _world == null) return;

            var objs = _world.GameObjects;
            int shipIdx = _world.ShipIndex;
            if ((uint)shipIdx >= (uint)(objs?.Count ?? 0)) return;
            var ship = objs[shipIdx];
            if (ship == null) return;

            int activeGobjIdx = _hud.ActiveTargetIndex;

            // Rebuild the current level list (cheap — small hierarchy)
            BuildCurrentLevelList();

            // Clamp highlight
            if (_currentLevelList.Count > 0)
                _highlightRow = Mathf.Clamp(_highlightRow, 0, _currentLevelList.Count - 1);

            // ── Clear old children ─────────────────────────────────────────────
            foreach (Node child in _vbox.GetChildren())
                child.QueueFree();

            // ── Breadcrumb / header ────────────────────────────────────────────
            string breadcrumb = BuildBreadcrumb(objs);
            AddRow(breadcrumb, bold: true, highlight: false, selectable: false);
            AddRow("─────────────────", bold: false, highlight: false, selectable: false);

            // ── Nav hint ──────────────────────────────────────────────────────
            string hint = _treeLevel == 0
                ? "[d/→] expand  [Enter] select galaxy"
                : "[a/←] back  [Enter] select";
            AddRow(hint, bold: false, highlight: false, selectable: false);
            AddRow("", bold: false, highlight: false, selectable: false);

            // ── Level rows ────────────────────────────────────────────────────
            for (int row = 0; row < _currentLevelList.Count; row++)
            {
                var entry = _currentLevelList[row];
                if ((uint)entry.GobjIndex >= (uint)objs.Count) continue;
                var body = objs[entry.GobjIndex];
                if (body == null) continue;

                // Live distance via UniMath.Distance (LCA path — CLAUDE.md §Position Math)
                double dist = UniMath.Distance(ship, body, objs);
                string distStr = Hud.FormatDistance(dist);
                string name = body.Name ?? "?";

                bool isActiveTgt  = entry.GobjIndex == activeGobjIdx;
                bool isKeyboardHl = row == _highlightRow;
                bool isGalaxy     = body.ObjectType == UniObject.Type.Galaxy;

                // Indent level for tree readability
                string indent = _treeLevel == 0 ? "" : (_treeLevel == 1 ? "  " : "    ");

                // Active-target marker (◀) and keyboard highlight (>)
                string activeGlyph = isActiveTgt ? "◀" : " ";
                string hlGlyph     = isKeyboardHl ? ">" : " ";

                // All rows show live distance. Galaxies also show the [d→] expand hint
                // to remind the player they can descend into the galaxy's star list.
                string rowText = isGalaxy
                    ? $"{hlGlyph}{activeGlyph}{indent}{name}  {distStr}  [d→]"
                    : $"{hlGlyph}{activeGlyph}{indent}{name}  {distStr}";

                var label = AddRow(rowText, bold: isGalaxy, highlight: isKeyboardHl || isActiveTgt, selectable: true);

                // Wire click selection:
                //   Single click on any row — highlight it, then SELECT the body
                //   (galaxies, stars, and planets are all selectable via click, same as Enter).
                //   Use d/Right key to descend into a galaxy via keyboard.
                int capturedRow = row;
                var capturedEntry = entry;
                label.GuiInput += (@inputEvent) =>
                {
                    if (@inputEvent is InputEventMouseButton mb
                        && mb.ButtonIndex == MouseButton.Left
                        && mb.Pressed)
                    {
                        _highlightRow = capturedRow;
                        if (capturedEntry.CandidatePos >= 0)
                        {
                            // All body types: select and close
                            _hud?.SetTargetIndex(capturedEntry.CandidatePos);
                            ClosePanel();
                        }
                        else
                        {
                            // Body not in candidate list (should not happen) — just highlight
                        }
                        GetViewport().SetInputAsHandled();
                    }
                };
                label.MouseFilter = MouseFilterEnum.Stop;
            }

            if (_currentLevelList.Count == 0)
            {
                AddRow("(empty)", bold: false, highlight: false, selectable: false);
            }
        }

        /// <summary>Builds the breadcrumb title for the current tree level.</summary>
        private string BuildBreadcrumb(List<UniObject> objs)
        {
            if (_treeLevel == 0) return "TARGETS — GALAXIES";

            string galName = "?";
            if ((uint)_expandedGalaxyIdx < (uint)(objs?.Count ?? 0))
                galName = objs[_expandedGalaxyIdx]?.Name ?? "?";

            if (_treeLevel == 1) return $"TARGETS — {galName} — STARS";

            string starName = "?";
            if ((uint)_expandedStarIdx < (uint)(objs?.Count ?? 0))
                starName = objs[_expandedStarIdx]?.Name ?? "?";

            return $"TARGETS — {galName} — {starName} — PLANETS";
        }

        /// <summary>
        /// Adds a Label row to the VBox, styled with PhosphorGreen.
        /// Returns the label so the caller can wire events.
        /// </summary>
        private Label AddRow(string text, bool bold, bool highlight, bool selectable)
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
            // Default: mouse events pass through (caller overrides for body rows)
            label.MouseFilter = MouseFilterEnum.Ignore;
            _vbox.AddChild(label);
            return label;
        }
    }
}
