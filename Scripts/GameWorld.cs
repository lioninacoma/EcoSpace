using Godot;
using System.Collections.Generic;

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

		/// <summary>
		/// Checks whether <paramref name="obj"/> has crossed any SOI boundary and
		/// moves it to the correct space. Runs recursively until stable.
		///
		/// Two directions are checked each call:
		///   1. Exit  – obj is outside its current parent's SOI → move up one level.
		///   2. Entry – obj is inside a sibling's SOI           → move down one level.
		/// </summary>
		private void TrySpaceTransition(UniObject obj, int excludeIndex = -1)
		{
			if (TryExitParentSOI(obj, out int exitedIndex)) { TrySpaceTransition(obj, exitedIndex); return; }
			if (TryEnterChildSOI(obj, excludeIndex)) { TrySpaceTransition(obj); }
		}

		// --- Exit: obj leaves its parent's SOI → move up one level --------

		private bool TryExitParentSOI(UniObject obj, out int exitedIndex)
		{
			exitedIndex = -1;
			if (obj.ParentIndex < 0) return false;  // already at root

			var parent = GameObjects[obj.ParentIndex];

			double distMeters = obj.LocalPos.Magnitude();
			if (distMeters < parent.SOIMeters) return false;

			exitedIndex = obj.ParentIndex;

			// Reparent: remove from current parent, add to grandparent
			parent.ChildIndices.Remove(obj.Index);
			if (parent.ParentIndex >= 0)
				GameObjects[parent.ParentIndex].ChildIndices.Add(obj.Index);

			obj.LocalPos = ChildPosToParentSpace(obj.LocalPos, parent);
			obj.ParentIndex = parent.ParentIndex;
			obj.CurrentSpace = parent.CurrentSpace;

			GD.Print($"[Transition ↑] Exited SOI of {parent.CurrentSpace}, " +
					 $"now in {obj.CurrentSpace}");
			return true;
		}

		// --- Entry: obj enters a sibling's SOI → move down one level ------

		/// <summary>
		/// Iterates only over the siblings of <paramref name="obj"/> by reading
		/// the parent's <see cref="UniObject.ChildIndices"/> directly.
		/// O(siblings) instead of O(all GameObjects).
		/// <paramref name="excludeIndex"/> skips the object just exited to prevent
		/// immediately re-entering it in the same transition step.
		/// </summary>
		private bool TryEnterChildSOI(UniObject obj, int excludeIndex = -1)
		{
			if (obj.ParentIndex < 0) return false;  // root-level objects have no siblings

			var parent = GameObjects[obj.ParentIndex];

			foreach (int siblingIndex in parent.ChildIndices)
			{
				if (siblingIndex == obj.Index) continue;
				if (siblingIndex == excludeIndex) continue;

				var candidate = GameObjects[siblingIndex];

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

			var obj = GameObjects[index];
			var localPos = obj.LocalPos;
			int currentParent = obj.ParentIndex;
			GD.Print(localPos);

			while (currentParent >= 0)
			{
				var parent = GameObjects[currentParent];
				localPos = ChildPosToParentSpace(localPos, parent);
				GD.Print(localPos);
				currentParent = parent.ParentIndex;
			}
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