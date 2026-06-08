using Godot;
using System;
using System.Collections.Generic;

namespace Universe
{
	public partial class GameWorld : Node3D
	{
		public List<UniObject> GameObjects;

		// Called when the node enters the scene tree for the first time.
		public override void _Ready()
		{
			GameObjects = [];
			AddGameObject(-1, new Double3(0, 0, 0), 1000);    // root (scale = -1)
			AddGameObject(0, new Double3(0, 0, 0), 1000);     // galaxy (universe scale = 1e16)
			AddGameObject(1, new Double3(0, 0, 1e14), 1000);  // star (galaxy scale = 1e4)
			AddGameObject(2, new Double3(0, 0, 10000), 1000); // planet (star scale = 1)
			PrintPositions(3);
		}

		private void TranslatePos(int index, UniVec3 v)
		{
			if (index < GameObjects.Count && index >= 0)
			{
				var obj = GameObjects[index];
				TranslatePos(obj, v);
			}
		}

		private void TranslatePos(int index, Double3 v)
		{
			if (index < GameObjects.Count && index >= 0)
			{
				var obj = GameObjects[index];
				TranslatePos(obj, v);
			}
		}

		private void TranslatePos(UniObject obj, UniVec3 v)
		{
			obj.LocalPos += v;
			TrySpaceTransition(obj);
		}

		private void TranslatePos(UniObject obj, Double3 v)
		{
			obj.LocalPos += v;
			TrySpaceTransition(obj);
		}

		private void TrySpaceTransition(UniObject obj)
		{
			if (obj.ParentIndex >= 0)
			{
				var parent = GameObjects[obj.ParentIndex];
				if (obj.LocalPos.Units.MagnitudeSq() >= parent.SOIUnits * parent.SOIUnits)
				{
					obj.LocalPos = FromChildToSameSpace(obj.LocalPos, parent);
					obj.CurrentSpace = parent.CurrentSpace;
					obj.ParentIndex = parent.ParentIndex;
				}
			}
		}

		private static bool IsChildSpacePosition(UniVec3 position, UniObject.Space space)
		{
			return Math.Abs(position.Scale - UniObject.Scale(UniObject.ChildSpace(space))) <= UniVec3.EPSILON;
		}

		private UniVec3 FromChildToSameSpace(UniVec3 childSpacePos, UniObject o)
		{
			if (!IsChildSpacePosition(childSpacePos, o.CurrentSpace))
			{
				throw new ArgumentException("'childSpacePos' is not in child space of 'o'!");
			}
			return childSpacePos.Convert(UniObject.Scale(o.CurrentSpace)) + o.LocalPos;
		}

		private void PrintPositions(int index)
		{
			if (index < GameObjects.Count && index >= 0)
			{
				var obj = GameObjects[index];
				var localPos = obj.LocalPos;
				int currentParentIndex = obj.ParentIndex;
				GD.Print(localPos);

				while (currentParentIndex > 0) // root := 0
				{
					var parent = GameObjects[currentParentIndex];
					localPos = FromChildToSameSpace(localPos, parent);
					GD.Print(localPos);
					currentParentIndex = parent.ParentIndex;
				}
			}
		}

		private int AddGameObject(int parentIndex, Double3 localPos, double soiUnits)
		{
			UniObject.Space space;

			if (parentIndex < GameObjects.Count && parentIndex >= 0)
			{
				var parent = GameObjects[parentIndex];
				space = UniObject.ChildSpace(parent.CurrentSpace);
			}
			else if (parentIndex == -1)
			{
				space = UniObject.Space.Root;
			}
			else return -1;

			int index = GameObjects.Count;
			GameObjects.Add(new UniObject
			{
				CurrentSpace = space,
				ParentIndex = parentIndex,
				LocalPos = new UniVec3(localPos, UniObject.Scale(space)),
				SOIUnits = soiUnits
			});

			return index;
		}

		// Called every frame. 'delta' is the elapsed time since the previous frame.
		public override void _Process(double delta)
		{
		}
	}
}