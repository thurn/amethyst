using System;
using System.Numerics;

namespace Amethyst.Action
{
    /// <summary>What a pushed enemy struck (an Impact event's).</summary>
    public enum HazardKind
    {
        Wall,
        Pillar,
        Pylon,
        Enemy,
    }

    /// <summary>The testbed's hazard layouts; arena modules build their own.</summary>
    public enum HazardSet
    {
        /// <summary>The room's walls only.</summary>
        None,

        /// <summary>A ring of pillars, and pylons.</summary>
        Pillars,

        /// <summary>A ring of Spike Vents erupting in turn.</summary>
        Vents,
    }

    /// <summary>What blocks a body: it moves a body of <c>radius</c> at <c>p</c> out of whatever
    /// it overlaps.</summary>
    public interface IObstacles
    {
        /// <summary>Moves a body of <paramref name="radius"/> at <paramref name="p"/> out of what
        /// blocks it (<paramref name="flies"/>: over pillars and pylons); true if anything did.</summary>
        bool Collide(ref Vector2 p, float radius, bool flies);
    }

    /// <summary>A wall: an axis-aligned box on the ground.</summary>
    public struct WallBox
    {
        public Vector2 Center,
            Half;
    }

    /// <summary>A pillar or a pylon: a round post.</summary>
    public struct Post
    {
        public Vector2 Center;
        public float Radius;
    }

    /// <summary>A Spike Vent: where it erupts and how far into its cycle it starts, s.</summary>
    public struct Vent
    {
        public Vector2 Center;
        public float Radius,
            Offset;
    }

    public enum VentPhase
    {
        Idle,

        /// <summary>The red marker filling toward the eruption.</summary>
        Marker,
        Erupting,
    }

    /// <summary>
    /// A room's hazards as data (docs/game-design.md, Arenas and hazards): its floor's half
    /// extents, its walls, pillars, pylons and Spike Vents. Arena modules place them.
    /// </summary>
    public sealed class HazardLayout
    {
        const float WallThickness = 1f;

        /// <summary>The floor's centre, m: the origin for a room of its own, elsewhere for an arena
        /// module placed in the chain's world (<see cref="Moved"/>).</summary>
        public Vector2 Center;

        /// <summary>The floor inside the outer walls, from <see cref="Center"/> each way, m.</summary>
        public Vector2 Half;
        public WallBox[] Walls = Array.Empty<WallBox>();
        public Post[] Pillars = Array.Empty<Post>();
        public Post[] Pylons = Array.Empty<Post>();
        public Vent[] Vents = Array.Empty<Vent>();

        /// <summary>A room <paramref name="half"/> m from its centre each way, walled in.</summary>
        public static HazardLayout Open(Vector2 half)
        {
            var t = WallThickness / 2f;
            return new HazardLayout
            {
                Half = half,
                Walls = new[]
                {
                    Box(new Vector2(0f, half.Y + t), new Vector2(half.X + 2f * t, t)),
                    Box(new Vector2(0f, -half.Y - t), new Vector2(half.X + 2f * t, t)),
                    Box(new Vector2(half.X + t, 0f), new Vector2(t, half.Y)),
                    Box(new Vector2(-half.X - t, 0f), new Vector2(t, half.Y)),
                },
            };
        }

        /// <summary>The testbed's layout of <paramref name="set"/> in a square room
        /// <paramref name="half"/> m each way.</summary>
        public static HazardLayout Of(HazardSet set, float half)
        {
            var layout = Open(new Vector2(half));
            switch (set)
            {
                case HazardSet.Pillars:
                    // A ring of pillars around the hero's start, one on each diagonal where the
                    // packs from the corners stream in, splits the horde into lanes; a pylon by
                    // the start, more out along the axes.
                    layout.Pillars = Ring(8, half * 0.45f, 0f, 0.4f + half * 0.025f);
                    var pylons = Ring(4, half * 0.7f, 0f, 0.55f);
                    layout.Pylons = new Post[pylons.Length + 1];
                    layout.Pylons[0] = new Post { Center = new Vector2(2.5f, 0f), Radius = 0.55f };
                    pylons.CopyTo(layout.Pylons, 1);
                    break;
                case HazardSet.Vents:
                    var vents = Ring(6, half * 0.5f, 0f, 1.8f);
                    layout.Vents = new Vent[vents.Length];
                    for (int i = 0; i < vents.Length; i++)
                        layout.Vents[i] = new Vent
                        {
                            Center = vents[i].Center,
                            Radius = vents[i].Radius,
                            Offset = i * Hazards.VentCycle / vents.Length,
                        };
                    break;
            }
            return layout;
        }

        /// <summary>This layout moved <paramref name="by"/> m, everything in it with it.</summary>
        public HazardLayout Moved(Vector2 by)
        {
            var walls = (WallBox[])Walls.Clone();
            for (int i = 0; i < walls.Length; i++)
                walls[i].Center += by;
            return new HazardLayout
            {
                Center = Center + by,
                Half = Half,
                Walls = walls,
                Pillars = Shift(Pillars, by),
                Pylons = Shift(Pylons, by),
                Vents = Array.ConvertAll(
                    Vents,
                    v => new Vent
                    {
                        Center = v.Center + by,
                        Radius = v.Radius,
                        Offset = v.Offset,
                    }
                ),
            };
        }

        static Post[] Shift(Post[] posts, Vector2 by) =>
            Array.ConvertAll(posts, p => new Post { Center = p.Center + by, Radius = p.Radius });

        internal static WallBox Box(Vector2 center, Vector2 half) =>
            new WallBox { Center = center, Half = half };

        static Post[] Ring(int count, float radius, float start, float size)
        {
            var posts = new Post[count];
            for (int i = 0; i < count; i++)
            {
                var a = start + i * 2f * MathF.PI / count;
                posts[i] = new Post
                {
                    Center = new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius,
                    Radius = size,
                };
            }
            return posts;
        }
    }

    /// <summary>
    /// A room's hazards in play: what blocks bodies (walls block everyone; pillars and standing
    /// pylons block walkers, not fliers), walkers' steering around posts, which pylons are gone and
    /// where each vent is in its cycle.
    /// </summary>
    public sealed class Hazards : IObstacles
    {
        /// <summary>A vent's marker, eruption and rest, s: one cycle is their sum.</summary>
        public const float VentMarker = 0.8f,
            VentErupt = 0.4f,
            VentIdle = 3f,
            VentCycle = VentMarker + VentErupt + VentIdle;

        /// <summary>A pylon's burst: its radius, m, damage (a Cleave finisher's) and outward push, m/s.</summary>
        public const float BurstRadius = 3f,
            BurstKnockback = 6f;
        public static readonly int BurstDamage = Strike.Of(Move.CleaveC).Damage;

        /// <summary>A pushed enemy's least speed for an Impact, and for an Impact to burst a pylon, m/s.</summary>
        public const float ImpactSpeed = 1f,
            PylonSpeed = 4f;

        const float LookAhead = 3f;
        const float Clearance = 0.3f;

        readonly bool[] broken;

        public Hazards(HazardLayout layout)
        {
            Layout = layout;
            broken = new bool[layout.Pylons.Length];
        }

        public HazardLayout Layout { get; }

        public bool Broken(int pylon) => broken[pylon];

        /// <summary>Breaks a standing pylon; false if it was already gone.</summary>
        public bool Break(int pylon)
        {
            if (broken[pylon])
                return false;
            broken[pylon] = true;
            return true;
        }

        /// <summary>Moves a body of <paramref name="radius"/> at <paramref name="p"/> out of what
        /// blocks it; true if anything did.</summary>
        public bool Collide(ref Vector2 p, float radius, bool flies) =>
            Collide(ref p, radius, flies, out _, out _, out _);

        /// <summary>As <see cref="Collide(ref Vector2, float, bool)"/> for a walker, without the
        /// floor's bounds: its walls (with their doorways) and standing posts only, for a hero who
        /// walks on between rooms.</summary>
        public bool Block(ref Vector2 p, float radius) =>
            Collide(ref p, radius, false, false, out _, out _, out _);

        /// <summary>As <see cref="Collide(ref Vector2, float, bool)"/>, with the deepest thing it
        /// struck, its index in its layout array (-1 for the floor's bounds) and the way out of it.</summary>
        public bool Collide(
            ref Vector2 p,
            float radius,
            bool flies,
            out HazardKind hit,
            out int index,
            out Vector2 normal
        ) => Collide(ref p, radius, flies, true, out hit, out index, out normal);

        bool Collide(
            ref Vector2 p,
            float radius,
            bool flies,
            bool bounded,
            out HazardKind hit,
            out int index,
            out Vector2 normal
        )
        {
            var kind = HazardKind.Wall;
            var at = -1;
            var way = Vector2.Zero;
            var deepest = 0f;
            // The floor's bounds first, so nothing tunnels through an outer wall in a long step.
            var bound = Layout.Half - new Vector2(radius);
            var inside = bounded
                ? Vector2.Clamp(p, Layout.Center - bound, Layout.Center + bound)
                : p;
            if (inside != p)
            {
                var back = inside - p;
                p = inside;
                Note((back.Length(), Vector2.Normalize(back)), HazardKind.Wall, -1);
            }
            var walls = Layout.Walls;
            for (int i = 0; i < walls.Length; i++)
                Note(OutOfBox(ref p, radius, walls[i]), HazardKind.Wall, i);
            if (!flies)
            {
                var pillars = Layout.Pillars;
                for (int i = 0; i < pillars.Length; i++)
                    Note(OutOfPost(ref p, radius, pillars[i]), HazardKind.Pillar, i);
                var pylons = Layout.Pylons;
                for (int i = 0; i < pylons.Length; i++)
                    if (!broken[i])
                        Note(OutOfPost(ref p, radius, pylons[i]), HazardKind.Pylon, i);
            }
            hit = kind;
            index = at;
            normal = way;
            return deepest > 0f;

            void Note((float Depth, Vector2 Normal) push, HazardKind what, int i)
            {
                if (push.Depth <= deepest)
                    return;
                deepest = push.Depth;
                kind = what;
                at = i;
                way = push.Normal;
            }
        }

        /// <summary>
        /// A walker's way <paramref name="toward"/> its goal <paramref name="reach"/> m off, bent
        /// around the posts just ahead: each post in its path pushes it to the side it is already
        /// on, more the nearer and more central it is.
        /// </summary>
        public Vector2 Steer(Vector2 p, Vector2 toward, float radius, float reach)
        {
            var way = toward;
            Bend(Layout.Pillars, null);
            Bend(Layout.Pylons, broken);
            var length = way.Length();
            return length > 1e-4f ? way / length : toward;

            void Bend(Post[] posts, bool[] gone)
            {
                for (int i = 0; i < posts.Length; i++)
                {
                    if (gone != null && gone[i])
                        continue;
                    var to = posts[i].Center - p;
                    var along = Vector2.Dot(to, toward);
                    if (along <= 0f || along > reach || along > LookAhead + posts[i].Radius)
                        continue;
                    var across = toward.X * to.Y - toward.Y * to.X;
                    var clear = posts[i].Radius + radius + Clearance;
                    if (MathF.Abs(across) >= clear)
                        continue;
                    // The post is to the left of the way when across > 0: bend right.
                    var side =
                        across > 0f
                            ? new Vector2(toward.Y, -toward.X)
                            : new Vector2(-toward.Y, toward.X);
                    var near = 1f - Math.Clamp((along - posts[i].Radius) / LookAhead, 0f, 1f);
                    way += side * (2f * (1f - MathF.Abs(across) / clear) * (0.5f + near));
                }
            }
        }

        /// <summary>Where vent <paramref name="vent"/> is in its cycle at room time
        /// <paramref name="time"/>, and how far through that phase (0-1).</summary>
        public VentPhase Phase(int vent, float time, out float fill)
        {
            var t = Mod(time + Layout.Vents[vent].Offset, VentCycle);
            if (t < VentMarker)
            {
                fill = t / VentMarker;
                return VentPhase.Marker;
            }
            if (t < VentMarker + VentErupt)
            {
                fill = (t - VentMarker) / VentErupt;
                return VentPhase.Erupting;
            }
            fill = (t - VentMarker - VentErupt) / VentIdle;
            return VentPhase.Idle;
        }

        /// <summary>The count of vent <paramref name="vent"/>'s eruptions begun by room time
        /// <paramref name="time"/>, less one: it rises by one as each marker fills.</summary>
        public int Eruption(int vent, float time) =>
            (int)MathF.Floor((time + Layout.Vents[vent].Offset - VentMarker) / VentCycle);

        static float Mod(float a, float b) => a - b * MathF.Floor(a / b);

        static (float, Vector2) OutOfPost(ref Vector2 p, float radius, Post post)
        {
            var off = p - post.Center;
            var want = post.Radius + radius;
            var d2 = off.LengthSquared();
            if (d2 >= want * want)
                return (0f, Vector2.Zero);
            var d = MathF.Sqrt(d2);
            var n = d > 1e-4f ? off / d : Vector2.UnitY;
            p = post.Center + n * want;
            return (want - d, n);
        }

        static (float, Vector2) OutOfBox(ref Vector2 p, float radius, WallBox box)
        {
            var near = Vector2.Clamp(p, box.Center - box.Half, box.Center + box.Half);
            var off = p - near;
            var d2 = off.LengthSquared();
            if (d2 >= radius * radius)
                return (0f, Vector2.Zero);
            if (d2 > 1e-8f)
            {
                var d = MathF.Sqrt(d2);
                var n = off / d;
                p = near + n * radius;
                return (radius - d, n);
            }
            // The centre is inside the box: out the nearest face.
            var rel = p - box.Center;
            var dx = box.Half.X - MathF.Abs(rel.X);
            var dy = box.Half.Y - MathF.Abs(rel.Y);
            if (dx < dy)
            {
                var sx = rel.X >= 0f ? 1f : -1f;
                p.X = box.Center.X + sx * (box.Half.X + radius);
                return (dx + radius, new Vector2(sx, 0f));
            }
            var sy = rel.Y >= 0f ? 1f : -1f;
            p.Y = box.Center.Y + sy * (box.Half.Y + radius);
            return (dy + radius, new Vector2(0f, sy));
        }
    }
}
