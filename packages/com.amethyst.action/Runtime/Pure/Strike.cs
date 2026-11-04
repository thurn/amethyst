using System;
using System.Numerics;

namespace Amethyst.Action
{
    public enum StrikeShape
    {
        Cone,
        Line,
        Circle,
    }

    /// <summary>
    /// One hit of a sword move on the ground plane, as its contact frame lands: who it reaches,
    /// for how much, and which way it pushes. Sizes are the hitbox's, which the visuals draw
    /// smaller (feel §4.2).
    /// </summary>
    public struct Strike
    {
        public Move Move;
        public StrikeShape Shape;
        public Vector2 Origin;

        /// <summary>Unit direction: the cone's axis or the line's.</summary>
        public Vector2 Direction;

        /// <summary>The cone's radius, the line's length or the circle's radius, m.</summary>
        public float Reach;

        /// <summary>The cone's full angle in degrees, or the line's width in m.</summary>
        public float Width;

        public int Damage;

        /// <summary>Knockback speed, m/s: along <see cref="Direction"/>, or outward for a circle.</summary>
        public float Knockback;

        /// <summary>The starting values (docs/game-design.md, The hero's kit; Starting values).</summary>
        public static Strike Of(Move move)
        {
            switch (move)
            {
                case Move.CleaveA:
                case Move.CleaveB:
                    return Make(move, StrikeShape.Cone, 3f, 120f, 10, 2f);
                case Move.CleaveC:
                    return Make(move, StrikeShape.Cone, 3.3f, 160f, 18, 6f);
                case Move.Lunge:
                    return Make(move, StrikeShape.Line, 4.5f, 1.2f, 22, 3f);
                case Move.LeapSlam:
                    return Make(move, StrikeShape.Circle, 3.5f, 0f, 25, 7f);
                default:
                    throw new ArgumentOutOfRangeException(nameof(move), move, null);
            }
        }

        static Strike Make(
            Move move,
            StrikeShape shape,
            float reach,
            float width,
            int damage,
            float knockback
        ) =>
            new Strike
            {
                Move = move,
                Shape = shape,
                Reach = reach,
                Width = width,
                Damage = damage,
                Knockback = knockback,
                Direction = Vector2.UnitY,
            };

        /// <summary>Whether a body of <paramref name="radius"/> m at <paramref name="point"/> is
        /// inside: any overlap counts.</summary>
        public bool Hits(Vector2 point, float radius)
        {
            var to = point - Origin;
            var distance = to.Length();
            switch (Shape)
            {
                case StrikeShape.Circle:
                    return distance <= Reach + radius;
                case StrikeShape.Line:
                    var along = Vector2.Dot(to, Direction);
                    var across = MathF.Abs(Direction.X * to.Y - Direction.Y * to.X);
                    return along >= -radius
                        && along <= Reach + radius
                        && across <= Width / 2f + radius;
                default:
                    if (distance > Reach + radius)
                        return false;
                    if (distance <= radius)
                        return true;
                    var off = MathF.Acos(
                        Math.Clamp(Vector2.Dot(to / distance, Direction), -1f, 1f)
                    );
                    return off <= Width * MathF.PI / 360f + MathF.Asin(radius / distance);
            }
        }

        /// <summary>The knockback velocity a body at <paramref name="point"/> takes, m/s.</summary>
        public Vector2 Push(Vector2 point)
        {
            if (Shape != StrikeShape.Circle)
                return Direction * Knockback;
            var away = point - Origin;
            return away.LengthSquared() > 1e-6f
                ? Vector2.Normalize(away) * Knockback
                : Vector2.Zero;
        }
    }
}
