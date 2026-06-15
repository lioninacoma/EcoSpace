using Godot;

namespace Render
{
	/// <summary>
	/// The single source of truth for how a star's PHYSICAL per-instance properties
	/// (<see cref="UniObject.Luminosity"/>, <see cref="UniObject.RadiusMeters"/>,
	/// <see cref="UniObject.BaseColor"/>) map to its rendered appearance — used IDENTICALLY by
	/// the in-system mesh path (<see cref="WorldRenderer"/>) and the skybox light-point path
	/// (<see cref="SkyboxRenderer"/>), so a star looks the same whichever way it is drawn and a
	/// tier-crossing handoff cannot pop.
	///
	/// A star is configured ONLY on its own instance (Luminosity, RadiusMeters, BaseColor in
	/// TestSetup). The ONE global knob is <see cref="Exposure"/> — editor-tunable via
	/// WorldRenderer.StarBrightness — which brightens or dims EVERY star (mesh and sky) together.
	///
	/// Why a magnitude (logarithmic) brightness curve, not linear:
	/// The Sun seen from ~1 AU and a sibling 8 ly away differ in received flux by ~10¹⁰
	/// (≈25 astronomical magnitudes). No single LINEAR scale can render the close Sun as a clean
	/// coloured disc AND a distant star as a visible coloured point — one always saturates to
	/// white. Taking the logarithm of the inverse-square flux compresses that vast range into a
	/// small [0,1] display band, so brightness stays where hue survives and ONE exposure shifts
	/// all stars coherently. This is the standard apparent-magnitude model.
	/// </summary>
	public static class StarRendering
	{
		/// <summary>
		/// The single global star-brightness knob (editor handle: WorldRenderer.StarBrightness),
		/// in magnitude/exposure stops. 0 = calibrated default. Positive brightens every star
		/// (mesh and sky) together; negative dims them. This is the ONLY global star option.
		/// </summary>
		public static float Exposure = 0f;

		// ----- Fixed calibration (not tuning knobs; internal units) -----------

		/// <summary>log10 of the inverse-square flux (Luminosity / metres²) that maps to display
		/// brightness 0. Set so the dimmest authored sibling (Barnard's, L=0.0035 @ 5.96 ly) sits
		/// just above black at Exposure 0.</summary>
		private const double LogFluxFloor = -40.0;

		/// <summary>Display brightness gained per decade (log10) of flux. Small because the
		/// flux range across the scene spans ~18 decades; this is what compresses Sun↔siblings
		/// into one visible band.</summary>
		private const float Contrast = 0.048f;

		// ----- Shared appearance rules ----------------------------------------

		/// <summary>
		/// Apparent display brightness in [0,1] for a star of the given physical luminosity seen
		/// at the given distance (metres). Inverse-square flux → log → linear ramp, shifted by the
		/// shared <see cref="Exposure"/>. Used by BOTH the mesh (emissive energy) and the sky point
		/// (additive alpha), so the two representations match. Kept ≤ 1 so the star's BaseColor
		/// hue is preserved instead of saturating to white.
		/// </summary>
		public static float ApparentBrightness(double luminosity, double distMeters)
		{
			if (luminosity <= 0.0 || distMeters <= 1e-30) return 0f;
			double flux  = luminosity / (distMeters * distMeters);
			double bright = (System.Math.Log10(flux) - LogFluxFloor + Exposure) * Contrast;
			return (float)System.Math.Clamp(bright, 0.0, 1.0);
		}

		/// <summary>Apparent angular radius (radians) of a star from its physical radius and
		/// distance. The SAME size rule for mesh and sky so the handoff cannot pop.</summary>
		public static double AngularRadius(double radiusMeters, double distMeters) =>
			distMeters > 1e-30 ? radiusMeters / distMeters : 0.0;
	}
}
