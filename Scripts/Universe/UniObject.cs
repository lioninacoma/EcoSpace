using System;
using System.Collections.Generic;
using System.Linq;
using Universe.Math;

namespace Universe
{
	public class UniObject
	{
		public enum Type
		{
			Orb, Asteroid, Ship, None
		}

		public enum Space
		{
			Root, Universe, Galaxy, Star, Planet
		}

		public static double Scale(Space space)
		{
			return space switch
			{
				Space.Universe => 1e16,
				Space.Galaxy   => 10000,
				Space.Star     => 1,
				Space.Planet   => 0.0001,
				_              => -1
			};
		}

		public static Space ChildSpace(Space parentSpace)
		{
			return parentSpace switch
			{
				Space.Root     => Space.Universe,
				Space.Universe => Space.Galaxy,
				Space.Galaxy   => Space.Star,
				Space.Star     => Space.Planet,
				_              => Space.Planet
			};
		}

		public static Space ParentSpace(Space childSpace)
		{
			return childSpace switch
			{
				Space.Planet   => Space.Star,
				Space.Star     => Space.Galaxy,
				Space.Galaxy   => Space.Universe,
				Space.Universe => Space.Root,
				_              => Space.Root
			};
		}

		public static Space IndexToSpace(int index)
		{
			var spaces = Enum.GetValues(typeof(Space)).Cast<Space>().ToArray();
			return spaces[System.Math.Min(System.Math.Max(index, 0), spaces.Length - 1)];
		}

		public static int SpaceToIndex(Space space)
		{
			var spaces = Enum.GetValues(typeof(Space)).Cast<Space>().ToArray();
			return Array.FindIndex(spaces, s => space.Equals(s));
		}

		public int             Index;
		public Space           CurrentSpace;
		public int             ParentIndex;
		public double          SOIMeters;
		public UniVec3         LocalPos;

		/// <summary>
		/// Indices into GameWorld.GameObjects of all direct children of this object.
		/// Maintained by GameWorld.AddGameObject / RemoveGameObject.
		/// </summary>
		public List<int> ChildIndices = [];
	}
}