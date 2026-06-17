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

		// ── Private flight state ────────────────────────────────────────────────

		/// <summary>Accumulated software steering cursor in pixels (D-01).</summary>
		private Vector2 _cursor = Vector2.Zero;

		/// <summary>Persistent ship attitude basis (D-02 hold-attitude).</summary>
		private Basis _shipBasis = Basis.Identity;

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

		// ── Private references ───────────────────────────────────────────────────

		private TestSetup _world;
		private Camera3D _camera;

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
			// Only accumulate steering when captured (mouse locked to window).
			if (Input.MouseMode != Input.MouseModeEnum.Captured) return;

			if (@event is InputEventMouseMotion motion)
			{
				// Accumulate relative delta into software cursor (D-01, Pitfall 8).
				// In Captured mode Godot always reports correct non-zero Relative deltas.
				_cursor += motion.Relative * _sensitivity;

				// Clamp to MaxCursorRadius — prevents overflow accumulation (T-03-03).
				_cursor = _cursor.LimitLength(_maxCursorRadius);
			}
		}

		public override void _UnhandledInput(InputEvent @event)
		{
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

			HandleThrottleInput();
			UpdateAttitude(delta);
			UpdateSpeedEnvelope(delta);
			ApplyMotion(delta);
			UpdateReticlePosition();
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
		/// </summary>
		private void UpdateAttitude(double delta)
		{
			// Compute normalised steer from cursor: -1..1 each axis
			Vector2 steer = _cursor / _maxCursorRadius;

			// Deadzone: inside deadzone → zero rotation → hold-attitude (D-02)
			if (steer.Length() < _deadzoneFraction)
				steer = Vector2.Zero;

			float dt = (float)delta;

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

			// Align the camera to the ship attitude (player sees from the ship's viewpoint).
			if (_camera != null)
				_camera.Basis = _shipBasis;
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
		///
		/// Direction-aware proximity clamp (tech-debt fix: thrust-zero-at-galaxy-soi-exit):
		///   During the scan we track the SINGLE nearest body and the unit direction from
		///   the ship TOWARD that body's centre (towardNearestUnit). After the scan we
		///   compute the radial closing velocity: closing = Dot(motionDir, towardNearestUnit).
		///   • closing > 0  → APPROACHING  → apply the proximity clamp as before.
		///   • closing ≤ 0  → RECEDING     → exempt the clamp (targetMax = _maxSpeed) so
		///                                    the existing _contextMax lerp ramps speed back
		///                                    up smoothly after passing a galaxy SOI edge.
		///   Throttle ≈ 0 or degenerate case (no toward-direction recorded) → treat as
		///   APPROACHING so the clamp is safe and the zero-throttle guard stays consistent.
		///   Both the _contextMax and _easedSpeed lerps run on every path (D-07 / Bug 4).
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

			double nearest = double.MaxValue;

			// towardNearestUnit: unit direction from ship TOWARD the nearest body's centre,
			// expressed in the parent meters frame. Valid only when nearest < double.MaxValue.
			Double3 towardNearestUnit = Double3.Zero;
			bool hasTowardDir = false;

			// ── ALWAYS include the parent body itself (Bug 3 fix) ──────────────
			// The parent is the origin of the ship's coordinate frame, so the ship's
			// distance from it is just the magnitude of ship.LocalPos (in meters).
			// Using UniVec3.Distance(ship, parent) would mix coordinate frames.
			if (parent.RadiusMeters > 0.0)
			{
				double distToParentCentre = ship.LocalPos.Magnitude();
				double distToParentSurface = System.Math.Max(0.0, distToParentCentre - parent.RadiusMeters);
				if (distToParentSurface < nearest)
				{
					nearest = distToParentSurface;
					// Toward-parent direction = opposite of the ship's position from parent origin.
					// ship.LocalPos.ToDouble3() is the meters vector from parent centre to ship;
					// negate it to point from ship toward parent centre, then normalize.
					Double3 shipPosMet = ship.LocalPos.ToDouble3();
					double mag = shipPosMet.Magnitude();
					if (mag > 1e-9)
					{
						towardNearestUnit = -shipPosMet * (1.0 / mag);
						hasTowardDir = true;
					}
				}
			}

			// ── Scan same-space siblings ────────────────────────────────────────
			// Snapshot ChildIndices to avoid mutation issues during scan.
			int[] siblings = [.. parent.ChildIndices];

			foreach (int idx in siblings)
			{
				if (idx == shipIndex) continue;

				var body = (uint)idx < (uint)gameObjects.Count ? gameObjects[idx] : null;
				if (body == null || body.RadiusMeters <= 0.0) continue;

				// Both ship and sibling share the same parent frame — LocalPos values
				// are in the same coordinate space, so Distance is safe here.
				double centreDist = UniVec3.Distance(ship.LocalPos, body.LocalPos);
				double surfaceDist = System.Math.Max(0.0, centreDist - body.RadiusMeters);
				if (surfaceDist < nearest)
				{
					nearest = surfaceDist;
					// Toward-sibling direction: delta from ship to body centre in meters.
					// Form the delta UniVec3 then collapse to meters with a single ToDouble3().
					Double3 deltaMeters = (body.LocalPos - ship.LocalPos).ToDouble3();
					double mag = deltaMeters.Magnitude();
					if (mag > 1e-9)
					{
						towardNearestUnit = deltaMeters * (1.0 / mag);
						hasTowardDir = true;
					}
					else
					{
						hasTowardDir = false;
					}
				}
			}

			// If still no bodies found, open space: allow max speed.
			if (nearest == double.MaxValue)
				nearest = _maxSpeed / System.Math.Max(_speedPerMeter, 1.0);

			// ── Direction-aware proximity clamp ────────────────────────────────
			// Derive the ship's motion direction in the parent meters frame.
			// forward = -_shipBasis.Z (Godot −Z forward convention, same as ApplyMotion).
			// Sign by throttle01 so reverse thrust produces a +Z motion direction.
			// Throttle ~0 → treat as approaching (clamp stays; speed is ~0 anyway).
			double targetMax;
			if (!hasTowardDir || System.Math.Abs(_throttle01) < 1e-6)
			{
				// Degenerate/open-space or zero throttle → keep proximity clamp (safe default).
				targetMax = Mathf.Clamp(nearest * _speedPerMeter, _minSpeed, _maxSpeed);
			}
			else
			{
				Vector3 fwd = -_shipBasis.Z;
				double tSign = System.Math.Sign(_throttle01);  // +1 forward, -1 reverse
				var motionDir = new Double3(fwd.X * tSign, fwd.Y * tSign, fwd.Z * tSign);

				double closing = Double3.Dot(motionDir, towardNearestUnit);

				if (closing > 0.0)
				{
					// Approaching — apply proximity clamp (ease-in preserved, D-06).
					targetMax = Mathf.Clamp(nearest * _speedPerMeter, _minSpeed, _maxSpeed);
				}
				else
				{
					// Receding — exempt proximity clamp so throttle ramps back to max speed.
					targetMax = _maxSpeed;
				}
			}

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
		/// </summary>
		private void UpdateReticlePosition()
		{
			if (_steeringReticle == null) return;

			// Reticle size is 16×16 px (arms span -8..+8 each axis).
			// Position top-left at (viewportCenter + cursor - halfSize) to center the control.
			var halfSize = _steeringReticle.Size / 2f;
			_steeringReticle.Position = _viewportCenter + _cursor - halfSize;
		}
	}
}
