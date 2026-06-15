namespace Render
{
	/// <summary>
	/// The single source of truth for how a star's PHYSICAL per-instance properties
	/// (<see cref="UniObject.Luminosity"/>, <see cref="UniObject.RadiusMeters"/>,
	/// <see cref="UniObject.BaseColor"/>) map to its rendered appearance — used by BOTH the
	/// in-system mesh path (<see cref="WorldRenderer"/>) and the skybox light-point path
	/// (<see cref="SkyboxRenderer"/>).
	///
	/// Design intent (per user direction): a star is configured ONLY on its own instance
	/// (Luminosity, RadiusMeters, BaseColor in TestSetup). There are no per-renderer tuning
	/// knobs. The two display constants below live here, in ONE place, so the mesh and the
	/// skybox always derive size and emitted light from the same rule and a tier-crossing
	/// handoff stays coherent.
	///
	/// Physics:
	/// - Apparent angular radius θ = RadiusMeters / distance (small-angle). This is identical
	///   for a mesh sphere and a sky disc at the same distance, so SIZE is determined purely by
	///   the star's physical radius — no artificial size enhancement.
	/// - A star is an unresolved point source at interstellar range, so its apparent brightness
	///   follows the inverse-square law: flux ∝ Luminosity / distance². The home star, rendered
	///   as a resolved mesh, instead shows its (distance-independent) surface emission. Both are
	///   derived here from the same per-star Luminosity.
	/// </summary>
	public static class StarRendering
	{
		/// <summary>
		/// Maps physical flux (Luminosity / distance², in solar-lum / m²) to a sky-point HDR
		/// brightness. The ONE exposure constant for skybox light points. Values &gt;1 feed the
		/// WorldEnvironment glow (bloom). Calibrated so a bright sibling (Sirius, L=25.4 @ 8.6 ly)
		/// lands a little above 1 (slight bloom) and dimmer/closer stars rank below it by true
		/// inverse-square falloff.
		/// </summary>
		public const double SkyExposure = 3e32;

		/// <summary>
		/// Maps per-star Luminosity to the resolved mesh's emissive energy (surface brightness,
		/// distance-independent). The ONE constant for star meshes. Default 3 reproduces the
		/// solar mesh's prior look (home star L=1 → energy 3).
		/// </summary>
		public const float MeshEmission = 3.0f;

		/// <summary>Apparent angular radius (radians) of a star from its physical radius and
		/// distance. Shared size rule — identical for mesh and sky so the handoff cannot pop.</summary>
		public static double AngularRadius(double radiusMeters, double distMeters) =>
			distMeters > 1e-30 ? radiusMeters / distMeters : 0.0;

		/// <summary>Inverse-square apparent brightness of a star as a sky light point.
		/// flux = Luminosity / distance², scaled to HDR by <see cref="SkyExposure"/>.</summary>
		public static float SkyBrightness(double luminosity, double distMeters) =>
			luminosity > 0.0 && distMeters > 1e-30
				? (float)(luminosity * SkyExposure / (distMeters * distMeters))
				: 0f;

		/// <summary>Emissive energy of a star rendered as a resolved mesh, from its Luminosity.</summary>
		public static float MeshEmissionEnergy(double luminosity) =>
			(float)(luminosity * MeshEmission);
	}
}
