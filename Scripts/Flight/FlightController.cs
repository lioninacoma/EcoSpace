using Godot;
using System.Collections.Generic;

namespace Flight
{
	/// <summary>
	/// Arcade flight controller implementing the locked flight model (FLT-01/02/03):
	///
	/// Steering (D-01/D-05): A software cursor accumulates mouse relative motion and
	/// is clamped to MaxCursorRadius. The ship rotates toward the cursor at a rate
	/// proportional to cursor distance from center. Cursor centered → rotation stops
	/// (hold-attitude, D-02). This uses accumulated relative deltas, NOT absolute
	/// mouse position queries (avoids Pitfall 8: captured-mouse recenter).
	///
	/// Attitude (D-02): Basis multiply + Orthonormalized() each frame prevents
	/// skew/drift accumulation over thousands of frames (T-03-01 mitigation).
	///
	/// Roll (D-04): Q/E on roll_left/roll_right InputMap actions.
	///
	/// Throttle (D-03): Persistent [-1,1] throttle raised/lowered by throttle_up/down;
	/// zeroed by full_stop. Persists hands-off (cockpit style, not hold-to-thrust).
	/// Negative throttle produces reverse thrust (ship moves along +shipBasis.Z).
	///
	/// Speed envelope (FLT-03/D-06/D-07/D-08): contextMax is derived from
	/// nearest-surface distance (Distance - RadiusMeters) eased frame-to-frame
	/// via Mathf.Lerp so SOI boundary crossings produce no speed snap (D-07).
	/// actualSpeed = throttle01 * contextMax (one control, auto-scaled, D-08).
	/// Speed clamped to [MinSpeed, MaxSpeed] before the lerp target (T-03-02 mitigation).
	///
	/// Motion: computes forward = -_shipBasis.Z, calls world.TranslatePos.
	///
	/// Reticle updates: sets positions on child Control nodes Crosshair and
	/// SteeringReticle (added in Main.tscn, Task 3).
	///
	/// Read accessors for the HUD: CurrentSpeed, Throttle01, SteerCursor, ShipBasis.
	/// </summary>
	public partial class FlightController : Node
	{
		// ── Constants ──────────────────────────────────────────────────────────

		/// <summary>Speed of light in m/s — reference only; no longer used as a cap on MaxSpeed
		/// (removed in Plan 03-02 to allow FTL-equivalent intergalactic speeds).</summary>
		// private const double SpeedOfLight = 3e8;

		// ── Exports (tuning knobs) ──────────────────────────────────────────────

		/// <summary>NodePath to the GameWorld / TestSetup node.</summary>
		[Export] public NodePath WorldPath { get; set; }

		/// <summary>NodePath to the Camera3D whose basis tracks ship attitude.</summary>
		[Export] public NodePath CameraPath { get; set; }

		private float _sensitivity = 0.3f;
		/// <summary>Mouse sensitivity: scales raw relative motion into software cursor pixels.</summary>
		[Export]
		public float Sensitivity
		{
			get => _sensitivity;
			set => _sensitivity = Mathf.Max(0.001f, value);
		}

		private float _maxCursorRadius = 120f;
		/// <summary>
		/// Maximum radius of the software steering cursor in pixels.
		/// Cursor is clamped to this radius each input event (T-03-03 mitigation).
		/// </summary>
		[Export]
		public float MaxCursorRadius
		{
			get => _maxCursorRadius;
			set => _maxCursorRadius = Mathf.Max(1f, value);
		}

		private float _deadzoneFraction = 0.1f;
		/// <summary>
		/// Fraction of MaxCursorRadius that is the deadzone.
		/// Cursor within deadzone → zero rotation (D-02 hold-attitude).
		/// </summary>
		[Export]
		public float DeadzoneFraction
		{
			get => _deadzoneFraction;
			set => _deadzoneFraction = Mathf.Clamp(value, 0f, 0.99f);
		}

		private float _turnRate = 1.2f;
		/// <summary>Max turn rate in radians per second at full cursor deflection.</summary>
		[Export]
		public float TurnRate
		{
			get => _turnRate;
			set => _turnRate = Mathf.Max(0f, value);
		}

		private float _rollRate = 1.0f;
		/// <summary>Roll rate in radians per second at full key press.</summary>
		[Export]
		public float RollRate
		{
			get => _rollRate;
			set => _rollRate = Mathf.Max(0f, value);
		}

		private float _throttleStep = 0.05f;
		/// <summary>Throttle increment per key press (fraction of [-1,1] range).</summary>
		[Export]
		public float ThrottleStep
		{
			get => _throttleStep;
			set => _throttleStep = Mathf.Clamp(value, 0f, 1f);
		}

		private double _speedPerMeter = 0.5;
		/// <summary>
		/// Context-max speed multiplier per metre of nearest-surface distance (D-06).
		/// contextMax = clamp(nearest * SpeedPerMeter, MinSpeed, MaxSpeed).
		/// </summary>
		[Export]
		public double SpeedPerMeter
		{
			get => _speedPerMeter;
			set => _speedPerMeter = System.Math.Max(0.0, value);
		}

		private double _minSpeed = 10.0;
		/// <summary>Minimum context-max speed in m/s (never stops the throttle from doing something).</summary>
		[Export]
		public double MinSpeed
		{
			get => _minSpeed;
			set => _minSpeed = System.Math.Max(0.0, value);
		}

		/// <summary>
		/// Default intergalactic MaxSpeed: ~2e20 m/s gives a ~2-minute full-throttle crossing
		/// at the ~2.4e22 m home→destination galaxy separation (D-35).
		/// Play-test tuning knob: raise for faster crossing, lower (e.g. 4e19) for ~10 min.
		/// </summary>
		private double _maxSpeed = 2e20;
		/// <summary>
		/// Maximum context-max speed in m/s. No SpeedOfLight cap — the distance→speed curve
		/// (D-06/07/08) decelerates naturally near bodies via RadiusMeters (D-36).
		/// System.Math.Max(0.0, value) blocks negative and NaN inputs (T-03-04 mitigation).
		/// </summary>
		[Export]
		public double MaxSpeed
		{
			get => _maxSpeed;
			set => _maxSpeed = System.Math.Max(0.0, value);  // no SpeedOfLight cap (Plan 03-02)
		}

		private double _speedEasing = 1.0;
		/// <summary>
		/// Easing rate for the contextMax lerp (D-07). Higher = faster transition.
		/// Absorbs SOI-boundary discontinuities so there is no visible speed snap (Pitfall 9).
		/// </summary>
		[Export]
		public double SpeedEasing
		{
			get => _speedEasing;
			set => _speedEasing = System.Math.Max(0.0, value);
		}

		private double _tierSpeedFactor = 1e-5;
		/// <summary>
		/// Per-tier speed ceiling factor (D-40): tierCeiling = parent.SOIMeters × k.
		/// Default 1e-5 → StarSOI 1.5e15 m × 1e-5 = 1.5e10 m/s (≈50 AU/s in-system).
		/// Play-test tuning knob — raise for faster in-system flight, lower for more precision.
		/// System.Math.Max(0.0, value) blocks negative and NaN inspector inputs (T-04-02 mitigation).
		/// </summary>
		[Export]
		public double TierSpeedFactor
		{
			get => _tierSpeedFactor;
			set => _tierSpeedFactor = System.Math.Max(0.0, value);
		}

		private double _speedPerTarget = 0.1;
		/// <summary>
		/// Target ease-out factor (D-43): targetEaseMax = distToTarget × k'.
		/// Default 0.1 → at 1 AU distance (1.5e11 m) → 1.5e10 m/s max. Tune by feel.
		/// System.Math.Max(0.0, value) blocks negative and NaN inspector inputs (T-04-02 mitigation).
		/// </summary>
		[Export]
		public double SpeedPerTarget
		{
			get => _speedPerTarget;
			set => _speedPerTarget = System.Math.Max(0.0, value);
		}

		private double _manualMaxSpeed = 1e6;
		/// <summary>
		/// Manual flight speed cap in m/s (D-09). All non-warp throttle clamped to this value.
		/// Default 1e6 m/s = 1,000 km/s — slow enough for precise navigation.
		/// System.Math.Max(0.0, value) blocks negative and NaN inputs (T-07-05 mitigation).
		/// </summary>
		[Export]
		public double ManualMaxSpeed
		{
			get => _manualMaxSpeed;
			set => _manualMaxSpeed = System.Math.Max(0.0, value);
		}

		private double _warpMaxSpeed = 2e20;
		/// <summary>
		/// Safety cap on computed warp speed in m/s (D-07). Technical knob only — prevents
		/// absurdly high speed when target is very close and travel time is short.
		/// Default 2e20 m/s matches the old intergalactic MaxSpeed magnitude.
		/// System.Math.Max(0.0, value) blocks negative and NaN inputs (T-07-05 mitigation).
		/// </summary>
		[Export]
		public double WarpMaxSpeed
		{
			get => _warpMaxSpeed;
			set => _warpMaxSpeed = System.Math.Max(0.0, value);
		}

		private double _warpOrientRate = 1.5;
		/// <summary>
		/// Slerp weight rate for warp auto-orient (D-03). Higher = faster ship turns toward target.
		/// Used as: slerpWeight = Clamp(WarpOrientRate × delta, 0, 1).
		/// System.Math.Max(0.0, value) blocks negative and NaN inputs (T-07-05 mitigation).
		/// </summary>
		[Export]
		public double WarpOrientRate
		{
			get => _warpOrientRate;
			set => _warpOrientRate = System.Math.Max(0.0, value);
		}

		// ── Warp state machine ──────────────────────────────────────────────────

		/// <summary>Three-state warp state machine (D-18/Plan 01 Task 2).</summary>
		private enum WarpState { Manual, Confirming, Warping }

		/// <summary>Current warp state. Starts in Manual; transitions on EngageWarp/DisengageWarp.</summary>
		private WarpState _warpState = WarpState.Manual;

		// ── Private flight state ────────────────────────────────────────────────

		/// <summary>Accumulated software steering cursor in pixels (D-01).</summary>
		private Vector2 _cursor = Vector2.Zero;

		/// <summary>
		/// Raw mouse delta from the last _Input event, reset each frame.
		/// Used by look-around to get raw mouse motion without going through the
		/// _cursor accumulator (which is suppressed when look_around is held).
		/// </summary>
		private Vector2 _rawMouseDelta = Vector2.Zero;

		/// <summary>Persistent ship attitude basis (D-02 hold-attitude).</summary>
		private Basis _shipBasis = Basis.Identity;

		/// <summary>
		/// Camera offset basis for look-around (D-12/D-13).
		/// Accumulates mouse delta while look_around held; lerps back to Identity on release.
		/// Combined with _shipBasis each frame: _camera.Basis = (_shipBasis * _cameraOffset).Orthonormalized()
		/// </summary>
		private Basis _cameraOffset = Basis.Identity;

		/// <summary>
		/// Ease-back rate for _cameraOffset toward Identity (D-13).
		/// 1f / 0.3f → camera returns to heading over ~0.3 s after Alt release.
		/// </summary>
		private float _lookEaseRate = 1f / 0.3f;

		/// <summary>Persistent throttle in [-1,1] (D-03). Negative = reverse thrust.</summary>
		private double _throttle01 = 0.0;

		/// <summary>
		/// Eased context-max speed in m/s (D-07).
		/// Starts at MinSpeed so first frame is not a jump from zero.
		/// </summary>
		private double _contextMax;

		/// <summary>
		/// Frame-to-frame eased actual speed in m/s (Bug 4 fix).
		/// Lerps toward _throttle01 * _contextMax each frame so throttle
		/// changes (including full-stop) smooth out rather than snap instantly.
		/// </summary>
		private double _easedSpeed;

		/// <summary>
		/// Travel time in seconds selected by the player in the warp confirmation screen (D-17).
		/// Stored per-session; never decremented.
		/// </summary>
		private double _selectedTravelTimeSec = 120.0;

		/// <summary>
		/// Calibrated internal time constant computed in EngageWarp (Bug 2 fix).
		/// Corrects the exponential approach dist(t) = d0·exp(-t/T_int) so the ship
		/// arrives at the target SOI in exactly _selectedTravelTimeSec.
		/// Derivation: T_int = T_sel / ln(d0 / SOI).
		/// </summary>
		private double _warpInternalTimeSec = 120.0;

		// ── Private references ───────────────────────────────────────────────────

		private TestSetup _world;
		private Camera3D _camera;
		private Hud.Hud _hud;

		// Reticle Control nodes (optional — set if nodes exist in the scene).
		private Control _steeringReticle;
		private Vector2 _viewportCenter;

		// ── Read-only accessors for HUD ─────────────────────────────────────────

		/// <summary>Current actual speed in m/s (throttle01 × contextMax).</summary>
		public double CurrentSpeed { get; private set; }

		/// <summary>Current throttle fraction [-1,1]. Negative = reverse thrust.</summary>
		public double Throttle01 => _throttle01;

		/// <summary>Current steering cursor offset in pixels from screen center.</summary>
		public Vector2 SteerCursor => _cursor;

		/// <summary>Current ship attitude basis.</summary>
		public Basis ShipBasis => _shipBasis;

		/// <summary>
		/// Set to true by TargetSelectorPanel when it opens, false when it closes.
		/// When true, ALL flight input is suppressed so WASD drives the menu, not the ship.
		/// This is the clean gate that prevents WASD from both navigating the menu and
		/// driving the ship simultaneously (06-02 play-test rework requirement).
		/// </summary>
		public bool IsPanelOpen { get; set; } = false;

		/// <summary>
		/// True while the ship is in the Warping state (flying on autopilot rails to target).
		/// Read-only consumer for the HUD/WarpConfirmationScreen cosmetic "WARP" display (D-14).
		/// </summary>
		public bool IsWarping => _warpState == WarpState.Warping;

		// ── Godot callbacks ──────────────────────────────────────────────────────

		public override void _Ready()
		{
			// Resolve world reference
			if (WorldPath != null && !WorldPath.IsEmpty)
				_world = GetNode<TestSetup>(WorldPath);
			else
				_world = GetTree().Root.FindChild("Main", true, false) as TestSetup;

			// Resolve camera
			if (CameraPath != null && !CameraPath.IsEmpty)
				_camera = GetNode<Camera3D>(CameraPath);
			else
				_camera = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;

			// Resolve optional reticle node (added in Task 3)
			_steeringReticle = GetTree().Root.FindChild("SteeringReticle", true, false) as Control;

			// Resolve Hud reference for reading active target index (D-43 target ease-out).
			// No [Export] NodePath — mirrors _steeringReticle no-export style.
			// Callers use ?. so a missing Hud node is safe (target ease-out simply disabled).
			_hud = GetTree().Root.FindChild("Hud", true, false) as Hud.Hud;

			// Compute viewport center for reticle positioning
			var viewport = GetViewport();
			if (viewport != null)
			{
				var size = viewport.GetVisibleRect().Size;
				_viewportCenter = size / 2f;
			}
			else
			{
				_viewportCenter = new Vector2(384f, 216f); // 768×432 / 2
			}

			// Initialise contextMax and easedSpeed so the first frame doesn't lerp from zero.
			_contextMax = _minSpeed;
			_easedSpeed = 0.0;

			// Start in Captured mode: mouse is invisible and relative motion drives steering.
			// The player can toggle to Visible mode (free cursor) with the T key.
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}

		public override void _Input(InputEvent @event)
		{
			// Mouse motion accumulation MUST run in _Input, not _UnhandledInput.
			// Control nodes with mouse_filter=Stop (the default) consume InputEventMouseMotion
			// before _UnhandledInput is called, so steering never reaches _UnhandledInput.
			// _Input runs before GUI event dispatch and is never blocked by Control nodes.
			//
			// Suppress ALL steering when the target-selector panel is open (WASD drives the menu).
			if (IsPanelOpen) return;
			//
			// Only accumulate steering when captured (mouse locked to window).
			if (Input.MouseMode != Input.MouseModeEnum.Captured) return;

			if (@event is InputEventMouseMotion motion)
			{
				// Always capture raw delta for look-around (D-12).
				// UpdateLookAround reads _rawMouseDelta directly so it gets motion even when
				// _cursor accumulation is suppressed below.
				_rawMouseDelta += motion.Relative;

				// Look-around active: mouse delta drives _cameraOffset only (D-12).
				// Do NOT accumulate into _cursor when look_around is held — that would steer
				// the ship the moment Alt is released (stale cursor bug).
				if (Input.IsActionPressed("look_around")) return;

				// Accumulate relative delta into software cursor (D-01, Pitfall 8).
				// In Captured mode Godot always reports correct non-zero Relative deltas.
				_cursor += motion.Relative * _sensitivity;

				// Clamp to MaxCursorRadius — prevents overflow accumulation (T-03-03).
				_cursor = _cursor.LimitLength(_maxCursorRadius);
			}
		}

		public override void _UnhandledInput(InputEvent @event)
		{
			// Suppress T-key mouse-mode toggle while the target-selector panel is open.
			// The panel owns the cursor while open; the T-key toggle resumes naturally
			// when the panel closes and IsPanelOpen is reset to false.
			if (IsPanelOpen) return;

			// Toggle between Captured (steering active) and Visible (free cursor) with T key.
			// When Captured: OS cursor is hidden/locked, relative mouse motion drives steering.
			// When Visible:  OS cursor is free; no steering input accumulates.
			if (@event is InputEventKey key && key.PhysicalKeycode == Key.T && key.Pressed && !key.Echo)
			{
				if (Input.MouseMode == Input.MouseModeEnum.Captured)
				{
					Input.MouseMode = Input.MouseModeEnum.Visible;
					// Zero the software cursor so ship holds attitude on release
					_cursor = Vector2.Zero;
				}
				else
				{
					Input.MouseMode = Input.MouseModeEnum.Captured;
				}
			}
		}

		public override void _Process(double delta)
		{
			if (_world == null) return;
			if (delta <= 0.0) return;

			// Panel gate: only suppress flight when manual AND panel is open (Pitfall 3).
			// During warp the panel is closed before EngageWarp is called, so this guard
			// never blocks _WarpProcess. During Confirming, IsPanelOpen=true and the
			// switch falls to the empty Confirming case — the panel handles all input.
			if (IsPanelOpen && _warpState == WarpState.Manual) return;

			switch (_warpState)
			{
				case WarpState.Manual:
					HandleThrottleInput();
					UpdateAttitude(delta);
					UpdateSpeedEnvelope(delta);
					ApplyMotion(delta);
					UpdateReticlePosition();
					break;

				case WarpState.Confirming:
					// IsPanelOpen=true; WarpConfirmationScreen handles all input.
					// Flight is frozen while the player configures travel time.
					break;

				case WarpState.Warping:
					// Look-around always active during warp (D-14).
					// UpdateLookAround handles both accumulate (Alt held) and ease-back (Alt released).
					UpdateLookAround(delta);
					_WarpProcess(delta);
					ApplyMotion(delta);
					// Camera write: combined ship heading + camera offset (same as manual path).
					if (_camera != null)
						_camera.Basis = (_shipBasis * _cameraOffset).Orthonormalized();
					break;
			}
		}

		// ── Input handling ───────────────────────────────────────────────────────

		/// <summary>
		/// Reads throttle_up / throttle_down / full_stop actions and updates _throttle01.
		/// Throttle persists hands-off (D-03); full_stop zeroes it.
		/// Throttle range is [-1, 1]: positive = forward, negative = reverse thrust (D-03 refinement).
		/// Clamped to [-1,1] (T-03-04 mitigation extended for reverse).
		/// </summary>
		private void HandleThrottleInput()
		{
			if (Input.IsActionJustPressed("throttle_up"))
				_throttle01 = Mathf.Clamp(_throttle01 + _throttleStep, -1.0, 1.0);

			if (Input.IsActionJustPressed("throttle_down"))
				_throttle01 = Mathf.Clamp(_throttle01 - _throttleStep, -1.0, 1.0);

			if (Input.IsActionJustPressed("full_stop"))
				_throttle01 = 0.0;
		}

		// ── Attitude update ──────────────────────────────────────────────────────

		/// <summary>
		/// Updates _shipBasis from mouse cursor (pitch/yaw) and Q/E keys (roll).
		/// Uses Basis multiply for drift-free composition (D-02, Pattern 3).
		/// Calls Orthonormalized() every frame to prevent skew accumulation (T-03-01).
		///
		/// Look-around (D-12): when look_around (Left Alt) is held, mouse delta accumulates
		/// into _cameraOffset instead of _shipBasis — ship holds heading, view decouples.
		/// On release, UpdateLookAround eases _cameraOffset back to Identity (D-13).
		/// </summary>
		private void UpdateAttitude(double delta)
		{
			float dt = (float)delta;

			bool isLookAround = Input.IsActionPressed("look_around");

			if (isLookAround)
			{
				// D-12: ship holds heading while look_around held.
				// Zero _cursor so no stale steering delta carries over when Alt is released.
				// Mouse motion is NOT accumulated into _cursor while look_around is held (_Input guard).
				_cursor = Vector2.Zero;
				// Mouse delta accumulates into _cameraOffset via UpdateLookAround.
				// Roll (Q/E) is suspended (Pitfall 6: must not mutate _shipBasis during look-around).
				UpdateLookAround(delta);
				// Orthonormalize _shipBasis even while holding heading (T-03-01).
				_shipBasis = _shipBasis.Orthonormalized();
			}
			else
			{
				// Normal manual steering: mouse delta → _shipBasis, Q/E → roll.
				Vector2 steer = _cursor / _maxCursorRadius;
				// Deadzone: inside deadzone → zero rotation → hold-attitude (D-02)
				if (steer.Length() < _deadzoneFraction)
					steer = Vector2.Zero;

				float yaw   = -steer.X * _turnRate * dt;
				float pitch = -steer.Y * _turnRate * dt;
				float roll  = (Input.GetActionStrength("roll_left") - Input.GetActionStrength("roll_right"))
				              * _rollRate * dt;

				// Compose LOCAL rotation onto the persistent _shipBasis (D-02 — no auto-level).
				// Order: pitch around local right, then yaw around local up, then roll around local forward.
				if (yaw != 0f || pitch != 0f || roll != 0f)
				{
					var pitchBasis = new Basis(Vector3.Right,    pitch);
					var yawBasis   = new Basis(Vector3.Up,       yaw);
					var rollBasis  = new Basis(Vector3.Back,     roll);  // +Z = back; roll around local forward = -Z
					_shipBasis = _shipBasis * pitchBasis * yawBasis * rollBasis;
				}

				// Orthonormalize every frame to prevent skew accumulation regardless of rotation (T-03-01).
				_shipBasis = _shipBasis.Orthonormalized();

				// Ease _cameraOffset back toward Identity (D-13): camera returns to ship heading.
				// UpdateLookAround handles the else-branch (look_around not held = ease-back).
				UpdateLookAround(delta);
			}

			// Align the camera to ship heading × look-around offset (T-07-04 mitigation).
			// When _cameraOffset is Identity (look-around inactive), this equals _shipBasis.
			if (_camera != null)
				_camera.Basis = (_shipBasis * _cameraOffset).Orthonormalized();
		}

		// ── Speed envelope ───────────────────────────────────────────────────────

		/// <summary>
		/// Computes distance-scaled contextMax from nearest-surface distance and eases
		/// it frame-to-frame to hide SOI-boundary snaps (D-06/D-07/Pitfall 9).
		/// actualSpeed = throttle01 × contextMax (D-08), itself eased so throttle
		/// changes do not snap speed instantly (Bug 4 fix).
		///
		/// Surface-distance scan (Bug 3 fix):
		///   Always include the PARENT body — when in Planet space the parent IS the
		///   planet and the siblings list is empty, so without this check the fallback
		///   would give huge speed near the planet surface (inverted envelope).
		///   Ship's distance from its parent body = ship.LocalPos.Magnitude() because
		///   the parent is the origin of the ship's coordinate frame.
		/// </summary>
		private void UpdateSpeedEnvelope(double delta)
		{
			var gameObjects = _world?.GameObjects;
			if (gameObjects == null) return;

			int shipIndex = _world.ShipIndex;
			var ship = (uint)shipIndex < (uint)gameObjects.Count ? gameObjects[shipIndex] : null;
			if (ship == null) return;

			int parentIdx = ship.ParentIndex;
			var parent = (uint)parentIdx < (uint)gameObjects.Count ? gameObjects[parentIdx] : null;
			if (parent == null)
			{
				// At root — no bodies; skip envelope update, keep previous _easedSpeed
				CurrentSpeed = _easedSpeed;
				return;
			}

			// ── Tier ceiling (D-40) ────────────────────────────────────────────
			// Derived from the ship's current SOI radius × tuning factor k.
			// Guard double.MaxValue (Root SOI = double.MaxValue sentinel in TestSetup)
			// to prevent Infinity from the multiply (T-04-01 mitigation, Pitfall 1).
			// Existing double.IsFinite guard in ApplyMotion is the last-resort catch.
			double tierCeiling = (parent.SOIMeters < double.MaxValue / 2.0)
				? Mathf.Clamp(parent.SOIMeters * _tierSpeedFactor, _minSpeed, _maxSpeed)
				: _maxSpeed;   // Root/open-universe: use authored MaxSpeed

			double nearest = double.MaxValue;

			// ── ALWAYS include the parent body itself (Bug 3 fix) ──────────────
			// The parent is the origin of the ship's coordinate frame, so the ship's
			// distance from it is just the magnitude of ship.LocalPos (in meters).
			// Using UniVec3.Distance(ship, parent) would mix coordinate frames.
			//
			// EXCEPTION (04-02 play-test fix): a Galaxy parent is a diffuse sky body
			// whose RadiusMeters equals its full SOIMeters (5e20), NOT a solid surface.
			// Including it makes the proximity damp treat the entire galaxy SOI as a
			// body surface, so `nearest` collapses to ~0 at the SOI-exit boundary and
			// crushes targetMax to MinSpeed (the galaxy-SOI-exit dead zone) — and ties
			// with the home star at the frame origin (HUD nearest flicker). Galaxies are
			// never rendered as meshes (D-28/T-03-06); they should not damp speed either.
			// In-galaxy proximity damp comes from sibling stars below; receding past them
			// lets `nearest` grow so speed ramps to tierCeiling (the intended D-42 behavior).
			if (parent.RadiusMeters > 0.0 && parent.ObjectType != UniObject.Type.Galaxy)
			{
				double distToParentCentre = ship.LocalPos.Magnitude();
				double distToParentSurface = System.Math.Max(0.0, distToParentCentre - parent.RadiusMeters);
				nearest = System.Math.Min(nearest, distToParentSurface);
			}

			// ── Scan same-space siblings ────────────────────────────────────────
			// Snapshot ChildIndices to avoid mutation issues during scan.
			// Uses UniVec3.Distance — ship and all siblings share the same parent
			// frame (same-frame path; CLAUDE.md §Position Math). NOT UniMath.Distance
			// here because that is the cross-frame LCA path (reserved for the target
			// ease-out below where the target may be in a different frame).
			int[] siblings = [.. parent.ChildIndices];

			foreach (int idx in siblings)
			{
				if (idx == shipIndex) continue;

				var body = (uint)idx < (uint)gameObjects.Count ? gameObjects[idx] : null;
				if (body == null || body.RadiusMeters <= 0.0) continue;

				// Same-frame sibling: LocalPos values share the parent coordinate space.
				double centreDist = UniVec3.Distance(ship.LocalPos, body.LocalPos);
				double surfaceDist = System.Math.Max(0.0, centreDist - body.RadiusMeters);
				nearest = System.Math.Min(nearest, surfaceDist);
			}

			// If still no bodies found, open space: use tierCeiling as the speed target.
			// (Previously _maxSpeed; updated to tierCeiling for consistency — D-42.)
			if (nearest == double.MaxValue)
				nearest = tierCeiling / System.Math.Max(_speedPerMeter, 1.0);

			// ── Proximity damp + tier ceiling (D-42) ──────────────────────────
			// Symmetric damp: speed is bounded by nearest-surface distance × SpeedPerMeter,
			// clamped to tierCeiling (NOT _maxSpeed). This is the core fix vs 260617-j6b:
			// when the ship recedes from a body, targetMax returns to tierCeiling — a
			// contextually appropriate value, never the global intergalactic _maxSpeed.
			double targetMax = Mathf.Clamp(nearest * _speedPerMeter, _minSpeed, tierCeiling);

			// ── Target-aware ease-out (D-43, only when a target is set) ────────
			// When active, further clamps targetMax to distToTarget × k' so the ship
			// decelerates naturally onto the target.
			// MUST use UniMath.Distance here (LCA path, CLAUDE.md §Position Math) —
			// the target may be in a different coordinate frame from the ship (e.g. after
			// an SOI transition). Do NOT use UniVec3.Distance or raw ToDouble3() for this.
			// tierCeiling still caps the ease-out (a target never makes you faster than
			// the current tier allows — D-44).
			int tgtIdx = _hud?.ActiveTargetIndex ?? -1;
			if (tgtIdx >= 0 && (uint)tgtIdx < (uint)gameObjects.Count && gameObjects[tgtIdx] != null)
			{
				// Cross-frame distance: UniMath.Distance uses the LCA path (CLAUDE.md).
				double distToTarget = UniMath.Distance(ship, gameObjects[tgtIdx], gameObjects);
				double targetEaseMax = Mathf.Clamp(distToTarget * _speedPerTarget, _minSpeed, tierCeiling);
				targetMax = System.Math.Min(targetMax, targetEaseMax);
			}

			// ── ManualMaxSpeed cap (D-09/D-10) ─────────────────────────────────
			// Apply only on the manual path. Tier ceiling (D-40) and proximity damp (D-42)
			// still computed above; manual flight simply ignores them beyond ManualMaxSpeed.
			// Warp path bypasses this method entirely (_WarpProcess sets _easedSpeed directly).
			if (_warpState == WarpState.Manual)
				targetMax = System.Math.Min(targetMax, _manualMaxSpeed);

			// ── Easing lerps — ALWAYS run on EVERY path (D-07 / D-41 / Bug 4 fix) ─
			// No early return may be inserted between here and CurrentSpeed = _easedSpeed.
			// Both lerps must execute regardless of whether a target is set (Pitfall 2).

			// Ease contextMax toward target to hide SOI-boundary discontinuities (D-07, Pitfall 9).
			_contextMax = Mathf.Lerp(_contextMax, targetMax, Mathf.Clamp(_speedEasing * delta, 0.0, 1.0));

			// Target speed = throttle fraction × context max (D-08).
			double targetSpeed = _throttle01 * _contextMax;

			// ── Ease actual speed frame-to-frame (Bug 4 fix) ──────────────────
			// Without this, changing throttle (including full_stop → 0) snaps speed
			// instantly. We lerp _easedSpeed toward targetSpeed so all transitions —
			// including throttle reversal and full-stop — smooth out visibly.
			_easedSpeed = Mathf.Lerp(_easedSpeed, targetSpeed, Mathf.Clamp(_speedEasing * delta, 0.0, 1.0));

			CurrentSpeed = _easedSpeed;
		}

		// ── Motion application ───────────────────────────────────────────────────

		/// <summary>
		/// Computes the motion Double3 delta from attitude + speed and calls TranslatePos.
		/// forward = -_shipBasis.Z (Godot −Z is forward, RESEARCH Pattern 3).
		/// Negative _easedSpeed (reverse throttle) → positive +Z motion → ship flies backward.
		/// Uses _easedSpeed (= CurrentSpeed) so motion smoothly follows throttle changes.
		/// </summary>
		private void ApplyMotion(double delta)
		{
			// Security mitigation T-03-04 / RESEARCH §V5: guard against non-finite CurrentSpeed
			// (NaN or Infinity) that could corrupt TranslatePos with invalid position deltas.
			// With MaxSpeed uncapped, a pathological editor export value could produce Infinity;
			// this guard ensures such values never reach the position system.
			if (!double.IsFinite(CurrentSpeed)) return;

			// Skip trivially tiny speeds to avoid unnecessary TranslatePos calls,
			// but use epsilon guard rather than exact-zero so easing can fully settle.
			if (System.Math.Abs(CurrentSpeed) < 1e-3) return;

			// Forward in ship-local space: -Z axis of the basis (Godot convention).
			// W (throttle_up) increases _throttle01 → positive CurrentSpeed → motion in -Z → forward.
			// S (throttle_down) below 0 → negative CurrentSpeed → motion in +Z → reverse.
			Vector3 forward = -_shipBasis.Z;

			// Build Double3 delta (meters). Speed is already clamped via MaxSpeed/MinSpeed.
			var motionDelta = new Double3(
				forward.X * CurrentSpeed * delta,
				forward.Y * CurrentSpeed * delta,
				forward.Z * CurrentSpeed * delta);

			_world.TranslatePos(_world.ShipIndex, motionDelta);
		}

		// ── Reticle positioning ───────────────────────────────────────────────────

		/// <summary>
		/// Updates the moving steering reticle's screen position based on _cursor.
		/// The reticle tracks cursor offset from viewport center (D-05).
		/// Control.Position sets the top-left corner, so we subtract half the reticle
		/// size (8px on each axis for a 16×16 control) to keep it centered on the cursor.
		/// Hides the reticle while look-around is active (visual feedback: crosshair gone
		/// while Alt is held tells the player they are in look-around mode, not steering).
		/// </summary>
		private void UpdateReticlePosition()
		{
			if (_steeringReticle == null) return;

			// Hide reticle during look-around — its position is meaningless then and hiding it
			// gives the player a clear visual signal that Alt-hold decoupled steering.
			if (Input.IsActionPressed("look_around"))
			{
				_steeringReticle.Visible = false;
				return;
			}
			_steeringReticle.Visible = true;

			// Reticle size is 16×16 px (arms span -8..+8 each axis).
			// Position top-left at (viewportCenter + cursor - halfSize) to center the control.
			var halfSize = _steeringReticle.Size / 2f;
			_steeringReticle.Position = _viewportCenter + _cursor - halfSize;
		}

		// ── Look-around camera ───────────────────────────────────────────────────

		/// <summary>
		/// Handles the _cameraOffset accumulate/ease-back cycle (D-12/D-13).
		///
		/// When look_around (Left Alt) is held: mouse cursor drives _cameraOffset accumulation.
		/// The same pitch/yaw composition used by UpdateAttitude is applied, but to _cameraOffset
		/// instead of _shipBasis — ship heading does not change.
		/// Roll is not applied to _cameraOffset (Pitfall 6: suspend roll during look-around).
		///
		/// When look_around is not held: ease _cameraOffset back toward Basis.Identity via
		/// Quaternion Slerp over ~0.3 s (D-13). Camera returns to ship heading / warp direction.
		///
		/// Called from:
		///   - UpdateAttitude (manual path) else branch (look_around not held).
		///   - _Process Warping case (both held and released paths, every warp frame).
		/// </summary>
		private void UpdateLookAround(double delta)
		{
			if (Input.IsActionPressed("look_around"))
			{
				// Accumulate raw mouse delta into _cameraOffset only (D-12).
				// _rawMouseDelta is the per-frame raw relative motion from _Input, captured even
				// when _cursor suppression is active. Scale by _sensitivity so sensitivity knob
				// still applies uniformly to both steering and look-around.
				Vector2 scaledDelta = _rawMouseDelta * _sensitivity;
				Vector2 steer = scaledDelta / _maxCursorRadius;
				float yaw   = -steer.X * _turnRate;
				float pitch = -steer.Y * _turnRate;
				if (yaw != 0f || pitch != 0f)
				{
					var pitchBasis = new Basis(Vector3.Right, pitch);
					var yawBasis   = new Basis(Vector3.Up,    yaw);
					_cameraOffset = (_cameraOffset * pitchBasis * yawBasis).Orthonormalized();
				}
				// Consume the raw delta so it doesn't double-apply if UpdateLookAround is called again.
				_rawMouseDelta = Vector2.Zero;
			}
			else
			{
				// Ease _cameraOffset back to Identity via Quaternion Slerp (D-13).
				// t clamped to [0,1] so a long delta does not overshoot identity.
				float t = Mathf.Clamp(_lookEaseRate * (float)delta, 0f, 1f);
				var offsetQuat = new Quaternion(_cameraOffset).Normalized();
				_cameraOffset = new Basis(offsetQuat.Slerp(Quaternion.Identity, t)).Orthonormalized();
				// Always reset raw delta on non-look-around frames too.
				_rawMouseDelta = Vector2.Zero;
			}
		}

		// ── Warp public API ──────────────────────────────────────────────────────

		/// <summary>
		/// Engages warp drive toward the current ActiveTargetIndex destination (D-06/D-08).
		/// Called by WarpConfirmationScreen on Enter after the player confirms travel time.
		///
		/// T-07-01 mitigation: travelTimeSec clamped to a minimum of 1.0 to prevent
		/// division by zero in _WarpProcess (dist / _selectedTravelTimeSec).
		/// </summary>
		public void EngageWarp(double travelTimeSec)
		{
			_selectedTravelTimeSec = System.Math.Max(1.0, travelTimeSec);

			// Compute calibrated T_int so arrival time = T_sel (Bug 2 fix).
			// dist(t) = d0·exp(-t/T_int) → arrives at SOI when t = T_int·ln(d0/SOI).
			// Setting T_int = T_sel / ln(d0/SOI) makes that exactly T_sel.
			var gameObjects = _world?.GameObjects;
			int shipIdx = _world?.ShipIndex ?? -1;
			int tgtIdx  = _hud?.ActiveTargetIndex ?? -1;
			bool hasRefs = gameObjects != null
				&& (uint)shipIdx < (uint)gameObjects.Count
				&& tgtIdx >= 0 && (uint)tgtIdx < (uint)gameObjects.Count;
			if (hasRefs)
			{
				var shipObj = gameObjects[shipIdx];
				var tgtObj  = gameObjects[tgtIdx];
				if (shipObj != null && tgtObj != null)
				{
					double d0  = UniMath.Distance(shipObj, tgtObj, gameObjects);
					double soi = System.Math.Max(tgtObj.SOIMeters, 1.0);
					double lnRatio = d0 > soi ? System.Math.Log(d0 / soi) : 0.0;
					_warpInternalTimeSec = lnRatio > 1e-9
						? _selectedTravelTimeSec / lnRatio
						: _selectedTravelTimeSec;
				}
				else
					_warpInternalTimeSec = _selectedTravelTimeSec;
			}
			else
				_warpInternalTimeSec = _selectedTravelTimeSec;

			// Zero steering cursor: stale delta would cause unexpected rotation on disengage.
			_cursor = Vector2.Zero;
			_warpState = WarpState.Warping;
		}

		/// <summary>
		/// Disengages warp drive and returns to manual flight (D-19).
		/// Does NOT zero CurrentSpeed — leaves _easedSpeed as-is so UpdateSpeedEnvelope's
		/// existing lerp eases it down toward ManualMaxSpeed on subsequent manual frames.
		/// This prevents the jarring hard-stop visible on the HUD (D-19 invariant).
		/// </summary>
		public void DisengageWarp()
		{
			_warpState = WarpState.Manual;
		}

		// ── Warp per-frame process ───────────────────────────────────────────────

		/// <summary>
		/// Per-frame warp execution: computes distance-based speed, auto-orients ship,
		/// and checks SOI arrival for auto-disengage. Called only when Warping.
		///
		/// Speed formula (D-06): warpSpeed = dist / _warpInternalTimeSec.
		/// _warpInternalTimeSec is calibrated at EngageWarp so arrival time = _selectedTravelTimeSec
		/// despite the exponential deceleration curve (Bug 2 fix). Never decremented.
		///
		/// Auto-orient (D-03): Slerp _shipBasis toward target direction each frame.
		/// SOI disengage (D-08): auto-disengages when dist &lt; target.SOIMeters.
		/// </summary>
		private void _WarpProcess(double delta)
		{
			var gameObjects = _world?.GameObjects;
			if (gameObjects == null) return;

			// Bounds-safe ship lookup (pattern: (uint) cast checks both >= 0 and < Count).
			int shipIdx = _world.ShipIndex;
			var ship = (uint)shipIdx < (uint)gameObjects.Count ? gameObjects[shipIdx] : null;

			// Bounds-safe target lookup.
			int tgtIdx = _hud?.ActiveTargetIndex ?? -1;
			var target = (tgtIdx >= 0 && (uint)tgtIdx < (uint)gameObjects.Count) ? gameObjects[tgtIdx] : null;

			// If either object is missing, disengage safely rather than continuing on rails.
			if (ship == null || target == null) { DisengageWarp(); return; }

			// Cross-frame distance — the ONLY safe way (CLAUDE.md §Position Math).
			// Never UniVec3.Distance or raw ToDouble3() across different coordinate frames.
			double dist = UniMath.Distance(ship, target, gameObjects);

			// D-08: auto-disengage when ship enters target SOI.
			if (dist < target.SOIMeters) { DisengageWarp(); return; }

			// D-06: warp speed = remaining distance / calibrated time constant (Bug 2 fix).
			// Capped by WarpMaxSpeed (D-07) to prevent pathological values on close targets.
			double warpSpeed = System.Math.Min(dist / _warpInternalTimeSec, _warpMaxSpeed);

			// T-07-02 mitigation: guard NaN/Infinity before writing to _easedSpeed.
			if (!double.IsFinite(warpSpeed)) { DisengageWarp(); return; }

			// D-03: smooth auto-orient toward target direction each frame.
			// NormalizedDirection normalizes in Double3 (double) space before casting to float —
			// avoids ~3e15 m per-axis precision loss when casting 2.4e22 m components directly.
			Double3 dir = UniMath.NormalizedDirection(ship, target, gameObjects);
			if (dir.X != 0.0 || dir.Y != 0.0 || dir.Z != 0.0)
			{
				// Build a target Basis with -Z aligned to the direction from ship to target.
				Vector3 forward = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z).Normalized();
				Vector3 currentUp = _shipBasis.Y;

				// Build orthogonal right vector using forward × up (right-handed convention).
				// up × forward gives the LEFT vector — wrong chirality → bad Quaternion → jitter.
				// Guard degenerate case when forward ≈ currentUp (ship pointing straight up/down at target).
				Vector3 right = forward.Cross(currentUp);
				if (right.LengthSquared() < 1e-6f)
					right = _shipBasis.X;   // degenerate guard: use existing local right
				else
					right = right.Normalized();

				Vector3 up = right.Cross(forward).Normalized();

				// Desired basis: -Z = forward direction (Godot -Z is forward convention).
				// Columns: X=right, Y=up, Z=-forward.
				var desiredBasis = new Basis(right, up, -forward).Orthonormalized();
				var currentQuat  = new Quaternion(_shipBasis).Normalized();
				var desiredQuat  = new Quaternion(desiredBasis).Normalized();

				float slerpWeight = Mathf.Clamp((float)(_warpOrientRate * delta), 0f, 1f);
				var lerpedQuat    = currentQuat.Slerp(desiredQuat, slerpWeight);
				_shipBasis        = new Basis(lerpedQuat).Orthonormalized();
			}

			// Write warp speed directly into _easedSpeed + CurrentSpeed.
			// ApplyMotion (called by the Warping case of _Process) uses CurrentSpeed.
			_easedSpeed  = warpSpeed;
			CurrentSpeed = _easedSpeed;
		}
	}
}
