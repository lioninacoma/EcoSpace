using Godot;
using System.Collections.Generic;
using Universe.Math;

namespace Universe
{
	public partial class GameWorld : Node3D
	{
		public List<UniObject> GameObjects;

		public override void _Ready()
		{
			GameObjects = [];
		}

		// ----- Public translate API ----------------------------------------

		public void TranslatePos(int index, UniVec3 delta)
		{
			if ((uint)index < (uint)GameObjects.Count)
				TranslatePos(GameObjects[index], delta);
		}

		public void TranslatePos(int index, Double3 delta)
		{
			if ((uint)index < (uint)GameObjects.Count)
				TranslatePos(GameObjects[index], delta);
		}

		private void TranslatePos(UniObject obj, UniVec3 delta)
		{
			obj.LocalPos += delta.Convert(obj.LocalPos.Scale);
			TrySpaceTransition(obj);
		}

		private void TranslatePos(UniObject obj, Double3 delta)
		{
			obj.LocalPos += delta;
			TrySpaceTransition(obj);
		}

		// ----- Space-transition logic --------------------------------------

		// Maximum iterations for the transition loop — prevents infinite loops
		// from math oscillations (converts stack overflow / hang into a logged anomaly).
		private const int MaxIterations = 32;

		/// <summary>
		/// Null-safe accessor; returns null for out-of-range or null slots.
		/// Route every GameObjects[i] lookup in the transition family through here.
		/// </summary>
		private UniObject Get(int index)
			=> (uint)index < (uint)GameObjects.Count ? GameObjects[index] : null;

		/// <summary>
		/// Checks whether <paramref name="obj"/> has crossed any SOI boundary and
		/// moves it to the correct space. Iterative, bounded, non-oscillating.
		/// </summary>
		private void TrySpaceTransition(UniObject obj)
		{
			int lastExitedIndex = -1;
			int iterations = 0;

			while (iterations < MaxIterations)
			{
				iterations++;

				if (TryExitParentSOI(obj, out int exitedIndex))
				{
					lastExitedIndex = exitedIndex;
					continue;
				}

				if (TryEnterChildSOI(obj, excludeIndex: lastExitedIndex))
				{
					lastExitedIndex = -1;
					continue;
				}

				// Stable — no more transitions needed
				break;
			}

			if (iterations >= MaxIterations)
			{
				GD.PrintErr($"[Transition] MaxIterations reached for object index={obj.Index} " +
							$"space={obj.CurrentSpace} — possible math oscillation. Halting transition.");
			}
		}

		private bool TryExitParentSOI(UniObject obj, out int exitedIndex)
		{
			exitedIndex = -1;
			if (obj.ParentIndex < 0) return false;  // already at root

			var parent = Get(obj.ParentIndex);
			if (parent == null) return false;

			double distMeters = obj.LocalPos.Magnitude();
			if (distMeters < parent.SOIMeters) return false;

			exitedIndex = obj.ParentIndex;

			// Reparent: remove from current parent, add to grandparent
			parent.ChildIndices.Remove(obj.Index);

			var grandparent = Get(parent.ParentIndex);
			if (grandparent != null)
				grandparent.ChildIndices.Add(obj.Index);

			obj.LocalPos = ChildPosToParentSpace(obj.LocalPos, parent);
			obj.ParentIndex = parent.ParentIndex;
			obj.CurrentSpace = parent.CurrentSpace;

			GD.Print($"[Transition ↑] Exited SOI of {parent.CurrentSpace}, " +
					 $"now in {obj.CurrentSpace}");
			return true;
		}

		// --- Entry: obj enters a sibling's SOI → move down one level ------

		private bool TryEnterChildSOI(UniObject obj, int excludeIndex = -1)
		{
			if (obj.ParentIndex < 0) return false;  // root-level objects have no siblings

			var parent = Get(obj.ParentIndex);
			if (parent == null) return false;

			// Snapshot ChildIndices before iterating to avoid foreach-while-mutating
			// (Pitfall 3: never mutate the collection while enumerating it)
			int[] siblings = [.. parent.ChildIndices];

			foreach (int siblingIndex in siblings)
			{
				if (siblingIndex == obj.Index) continue;
				if (siblingIndex == excludeIndex) continue;

				var candidate = Get(siblingIndex);
				if (candidate == null) continue;

				UniVec3 relPos = obj.LocalPos - candidate.LocalPos;
				double distMeters = relPos.Magnitude();

				if (distMeters >= candidate.SOIMeters) continue;

				// Reparent: move obj from current parent's children to candidate's children
				parent.ChildIndices.Remove(obj.Index);
				candidate.ChildIndices.Add(obj.Index);

				UniObject.Space childSpace = UniObject.ChildSpace(candidate.CurrentSpace);
				double childScale = UniObject.Scale(childSpace);

				obj.LocalPos = relPos.Convert(childScale);
				obj.ParentIndex = siblingIndex;
				obj.CurrentSpace = childSpace;

				GD.Print($"[Transition ↓] Entered SOI of {candidate.CurrentSpace} " +
						 $"(index {siblingIndex}), now in {obj.CurrentSpace}");
				return true;
			}

			return false;
		}

		// ----- Coordinate helpers -----------------------------------------

		/// <summary>
		/// Converts a position expressed in the child space of <paramref name="parent"/>
		/// into grandparent space.
		/// Formula: grandparentPos = parent.LocalPos + childPos.Convert(parentScale)
		/// </summary>
		protected static UniVec3 ChildPosToParentSpace(UniVec3 childSpacePos, UniObject parent)
		{
			double parentScale = UniObject.Scale(parent.CurrentSpace);
			return childSpacePos.Convert(parentScale) + parent.LocalPos;
		}

		// ----- Debug helpers ----------------------------------------------

		private void PrintPositions(int index)
		{
			if ((uint)index >= (uint)GameObjects.Count) return;

			var obj = Get(index);
			if (obj == null) return;

			var localPos = obj.LocalPos;
			int currentParent = obj.ParentIndex;
			GD.Print(localPos);

			while (currentParent >= 0)
			{
				var parent = Get(currentParent);
				if (parent == null) break;
				localPos = ChildPosToParentSpace(localPos, parent);
				GD.Print(localPos);
				currentParent = parent.ParentIndex;
			}
		}

		/// <summary>
		/// STAB-01 smoke check: places a fresh ship just inside Planet A's SOI,
		/// applies a delta large enough to exit Planet A and enter Planet B's SOI,
		/// then prints a summary line. Call from a headless or editor run to verify
		/// iterative transition correctness.
		/// </summary>
		public void RunTransitionSmokeCheck()
		{
			const double PlanetA_Z    = 1.496e11;
			const double PlanetB_Z    = 2.279e11;
			const double StarSOI      = 1.5e15;
			const double PlanetSOI    = 1.0e9;
			const double ShipOrbit    = 7e6;

			// Build a minimal hierarchy
			var saved = GameObjects;
			GameObjects = [];

			int root    = AddGameObject(-1,     new Double3(0, 0, 0),         double.MaxValue);
			int galaxy  = AddGameObject(root,   new Double3(0, 0, 0),         5e3);
			int star    = AddGameObject(galaxy, new Double3(0, 0, 0),         StarSOI);
			int planetA = AddGameObject(star,   new Double3(0, 0, PlanetA_Z), PlanetSOI);
			int planetB = AddGameObject(star,   new Double3(0, 0, PlanetB_Z), PlanetSOI);
			int ship    = AddGameObject(planetA, new Double3(0, 0, ShipOrbit), 0);

			bool exceptionThrown = false;
			try
			{
				// Large delta: crosses Planet A SOI and travels far enough to enter Planet B SOI
				// In planet-space units (scale=1e-4 m/unit), PlanetSOI = 1e9 m = 1e13 units.
				// We need to overshoot Planet A's SOI (~1e9 m) and reach Planet B's neighborhood.
				// Delta in meters: cross PlanetA SOI (exit) + travel toward PlanetB in star space.
				// Use a very large delta to ensure crossing both boundaries in one call.
				double bigDelta = PlanetB_Z - PlanetA_Z + PlanetSOI * 0.5; // ~8.3e10 m toward PlanetB
				TranslatePos(ship, new Double3(0, 0, bigDelta));
			}
			catch (System.Exception ex)
			{
				exceptionThrown = true;
				GD.PrintErr($"[SmokeCheck] Exception: {ex.Message}");
			}

			var shipObj = Get(ship);
			int finalParent = shipObj?.ParentIndex ?? -999;
			GD.Print($"SMOKE: parent={finalParent} exception={exceptionThrown}");
			GD.Print($"SMOKE: ship.CurrentSpace={shipObj?.CurrentSpace} " +
					 $"expectedParent={planetB}");

			// Restore original GameObjects
			GameObjects = saved;
		}

		// ----- Object management ------------------------------------------

		/// <returns>Index of the new object, or -1 on failure.</returns>
		protected int AddGameObject(int parentIndex, Double3 localPos, double soiMeters)
		{
			UniObject.Space space;

			if (parentIndex == -1)
			{
				space = UniObject.Space.Root;
			}
			else if ((uint)parentIndex < (uint)GameObjects.Count)
			{
				space = UniObject.ChildSpace(GameObjects[parentIndex].CurrentSpace);
			}
			else return -1;

			int index = GameObjects.Count;
			var obj = new UniObject
			{
				Index = index,
				CurrentSpace = space,
				ParentIndex = parentIndex,
				LocalPos = new UniVec3(localPos, UniObject.Scale(space)),
				SOIMeters = soiMeters,
			};

			GameObjects.Add(obj);

			if (parentIndex >= 0)
				GameObjects[parentIndex].ChildIndices.Add(index);

			return index;
		}

		/// <summary>
		/// Removes an object from the world and detaches it from its parent's
		/// <see cref="UniObject.ChildIndices"/>.
		/// Note: does not re-index; leaves a null slot. Adjust if you need compaction.
		/// </summary>
		protected void RemoveGameObject(int index)
		{
			if ((uint)index >= (uint)GameObjects.Count) return;

			var obj = GameObjects[index];
			if (obj.ParentIndex >= 0)
				GameObjects[obj.ParentIndex].ChildIndices.Remove(obj.Index);

			GameObjects[index] = null;
		}

		public override void _Process(double delta) { }
	}
}
