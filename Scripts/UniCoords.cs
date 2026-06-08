using Godot;
using System;
using System.Collections.Generic;

namespace Universe
{
	public struct UniCoords
    {
        public List<UniVec3> Positions;

        public UniCoords(double x, double y, double z)
        {
            var posMeters = CoordsMath.FromMeters(x, y, z);
            Positions = [
                posMeters,
                posMeters.Convert(CoordsMath.SCALE_KILOMETER),
                posMeters.Convert(CoordsMath.SCALE_LIGHT_YEAR),
                posMeters.Convert(CoordsMath.SCALE_AU),
            ];
        }
    }
}
