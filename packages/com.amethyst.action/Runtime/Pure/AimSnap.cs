using System;
using System.Collections.Generic;
using System.Numerics;

namespace Amethyst.Action
{
    /// <summary>Mild aim assist (feel §2.5): turn an aim toward the nearest target near it.</summary>
    public static class AimSnap
    {
        /// <summary>
        /// The unit direction from <paramref name="origin"/> to the nearest target within
        /// <paramref name="degrees"/> of <paramref name="aim"/> and <paramref name="range"/> m,
        /// or <paramref name="aim"/> normalized when there is none.
        /// </summary>
        public static Vector2 Snap(
            Vector2 origin,
            Vector2 aim,
            IReadOnlyList<Vector2> targets,
            float degrees,
            float range
        )
        {
            if (aim.LengthSquared() < 1e-6f)
                return aim;
            aim = Vector2.Normalize(aim);
            var cos = MathF.Cos(degrees * MathF.PI / 180f);
            var best = aim;
            var bestDistance = range;
            foreach (var target in targets)
            {
                var to = target - origin;
                var distance = to.Length();
                if (distance < 1e-3f || distance > bestDistance)
                    continue;
                var direction = to / distance;
                if (Vector2.Dot(direction, aim) < cos)
                    continue;
                best = direction;
                bestDistance = distance;
            }
            return best;
        }
    }
}
