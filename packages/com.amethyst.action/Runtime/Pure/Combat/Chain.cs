using System;
using System.Collections.Generic;
using System.Numerics;

namespace Amethyst.Action
{
    /// <summary>A wall's side of a room, in <see cref="SpawnSchedule.Edges"/>' bit order.</summary>
    public enum Side
    {
        /// <summary>+y.</summary>
        North,

        /// <summary>-y.</summary>
        South,

        /// <summary>+x.</summary>
        East,

        /// <summary>-x.</summary>
        West,
    }

    /// <summary>A doorway socket: the side of the room it is in and its centre along that wall
    /// from the room's centre, m (x on north and south walls, y on east and west ones).</summary>
    public struct Socket
    {
        public Side Side;
        public float Along;

        public Socket(Side side, float along)
        {
            Side = side;
            Along = along;
        }

        /// <summary>The way out of the room through it.</summary>
        public Vector2 Out =>
            Side switch
            {
                Side.North => Vector2.UnitY,
                Side.South => -Vector2.UnitY,
                Side.East => Vector2.UnitX,
                _ => -Vector2.UnitX,
            };

        /// <summary>Where it meets the floor, from the room's centre, for a room <paramref name="half"/>
        /// m each way.</summary>
        public Vector2 At(Vector2 half)
        {
            var o = Out;
            var across = new Vector2(MathF.Abs(o.Y), MathF.Abs(o.X));
            return o * (o.X != 0f ? half.X : half.Y) + across * Along;
        }
    }

    /// <summary>
    /// A hand-built combat arena (docs/game-design.md, Arenas; Variety between runs): about 40x30 m,
    /// its own shape, pillars and one or two hazard types besides its walls, with an entry socket on
    /// its south or west wall and an exit on its north or east one, so any module can follow any
    /// other and a chain of them only ever heads north and east.
    /// </summary>
    public sealed class ArenaModule
    {
        /// <summary>A doorway's half width, m: a corridor's too.</summary>
        public const float DoorHalf = 2f;

        /// <summary>Walls' thickness, m.</summary>
        public const float Wall = 1f;

        public string Name;

        /// <summary>Its hazards about its own centre, its outer walls open at its two doorways.</summary>
        public HazardLayout Layout;
        public Socket Entry,
            Exit;

        /// <summary>Points a walk from the entry to the exit passes through, clear of everything
        /// in the way; none when the straight line is clear.</summary>
        public Vector2[] Via;

        public Vector2 Half => Layout.Half;

        ArenaModule(
            string name,
            Vector2 half,
            Socket entry,
            Socket exit,
            HazardLayout inside,
            params Vector2[] via
        )
        {
            Name = name;
            Entry = entry;
            Exit = exit;
            Via = via;
            inside.Half = half;
            var walls = new List<WallBox>(Walls(half, entry, exit));
            walls.AddRange(inside.Walls);
            inside.Walls = walls.ToArray();
            Layout = inside;
        }

        /// <summary>The early tier's modules (rooms 1-4).</summary>
        public static readonly ArenaModule[] Early =
        {
            // Two rows of pillars split the horde into three lanes along the room; pylons wait in
            // the outer lanes.
            new ArenaModule(
                "lanes",
                new Vector2(20f, 15f),
                new Socket(Side.West, 0f),
                new Socket(Side.East, 0f),
                new HazardLayout
                {
                    Pillars = Grid(0.9f, new[] { -12f, -6f, 0f, 6f, 12f }, new[] { -5f, 5f }),
                    Pylons = Posts(0.55f, (-9f, 10f), (9f, -10f), (-3f, -10f), (3f, 10f)),
                }
            ),
            // A square room ringed by six Spike Vents erupting in turn, a pillar in each corner.
            new ArenaModule(
                "vent ring",
                new Vector2(17f, 17f),
                new Socket(Side.South, -8f),
                new Socket(Side.North, 8f),
                new HazardLayout
                {
                    Pillars = Posts(1.2f, (-11f, 11f), (11f, 11f), (-11f, -11f), (11f, -11f)),
                    Vents = Ring(6, 9f, MathF.PI / 6f, 1.8f),
                }
            ),
            // A long gallery whose walls jut in from either side, a Spike Vent in each bay.
            new ArenaModule(
                "gallery",
                new Vector2(22f, 13f),
                new Socket(Side.West, 0f),
                new Socket(Side.North, 16f),
                new HazardLayout
                {
                    Walls = new[]
                    {
                        HazardLayout.Box(new Vector2(-11f, 9.5f), new Vector2(0.5f, 3.5f)),
                        HazardLayout.Box(new Vector2(0f, -8.5f), new Vector2(0.5f, 4.5f)),
                        HazardLayout.Box(new Vector2(11f, 9.5f), new Vector2(0.5f, 3.5f)),
                    },
                    Vents = Vents(1.8f, (-16f, -7f), (-5.5f, 8f), (5.5f, -7f), (16.5f, -7f)),
                },
                new Vector2(14f, 0f)
            ),
            // A court around a cluster of four pillars, pylons ringing it to burst into the crowd.
            new ArenaModule(
                "court",
                new Vector2(19f, 15f),
                new Socket(Side.South, 6f),
                new Socket(Side.East, -6f),
                new HazardLayout
                {
                    Pillars = Posts(1f, (-3f, 3f), (3f, 3f), (-3f, -3f), (3f, -3f)),
                    Pylons = Posts(0.55f, (-10f, 0f), (0f, 9f), (-8f, -8f), (-10f, 9f), (11f, 7f)),
                }
            ),
        };

        // The outer walls of a room `half` m each way, each side split around its doorway.
        static IEnumerable<WallBox> Walls(Vector2 half, Socket entry, Socket exit)
        {
            var t = Wall / 2f;
            foreach (Side side in Enum.GetValues(typeof(Side)))
            {
                var s = new Socket(side, 0f);
                var o = s.Out;
                var across = new Vector2(MathF.Abs(o.Y), MathF.Abs(o.X));
                // The wall runs along `across` from -reach to reach, past the corners on the
                // north and south walls so they close.
                var reach = Vector2.Dot(across, half) + (o.Y != 0f ? Wall : 0f);
                var line = s.At(half) + o * t;
                var door =
                    entry.Side == side ? entry.Along
                    : exit.Side == side ? exit.Along
                    : float.NaN;
                if (float.IsNaN(door))
                {
                    yield return Strip(line, across, -reach, reach);
                    continue;
                }
                yield return Strip(line, across, -reach, door - DoorHalf);
                yield return Strip(line, across, door + DoorHalf, reach);
            }

            WallBox Strip(Vector2 line, Vector2 across, float from, float to) =>
                HazardLayout.Box(
                    line + across * ((from + to) / 2f),
                    across * ((to - from) / 2f) + new Vector2(across.Y, across.X) * t
                );
        }

        static Post[] Posts(float radius, params (float X, float Y)[] at) =>
            Array.ConvertAll(at, p => new Post { Center = new Vector2(p.X, p.Y), Radius = radius });

        static Post[] Grid(float radius, float[] xs, float[] ys)
        {
            var posts = new List<Post>();
            foreach (var y in ys)
            foreach (var x in xs)
                posts.Add(new Post { Center = new Vector2(x, y), Radius = radius });
            return posts.ToArray();
        }

        static Vent[] Vents(float radius, params (float X, float Y)[] at)
        {
            var vents = new Vent[at.Length];
            for (int i = 0; i < at.Length; i++)
                vents[i] = new Vent
                {
                    Center = new Vector2(at[i].X, at[i].Y),
                    Radius = radius,
                    Offset = i * Hazards.VentCycle / at.Length,
                };
            return vents;
        }

        static Vent[] Ring(int count, float radius, float start, float size)
        {
            var at = new (float, float)[count];
            for (int i = 0; i < count; i++)
            {
                var a = start + i * 2f * MathF.PI / count;
                at[i] = (MathF.Cos(a) * radius, MathF.Sin(a) * radius);
            }
            return Vents(size, at);
        }
    }

    /// <summary>An axis-aligned rectangle on the ground.</summary>
    public struct Area
    {
        public Vector2 Min,
            Max;

        public Area(Vector2 a, Vector2 b)
        {
            Min = Vector2.Min(a, b);
            Max = Vector2.Max(a, b);
        }

        public Area Grown(float by) => new Area(Min - new Vector2(by), Max + new Vector2(by));

        public bool Contains(Vector2 p) =>
            p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;

        /// <summary>Whether the two overlap by more than <paramref name="slack"/> m both ways.</summary>
        public bool Overlaps(Area o, float slack = 0.01f) =>
            MathF.Min(Max.X, o.Max.X) - MathF.Max(Min.X, o.Min.X) > slack
            && MathF.Min(Max.Y, o.Max.Y) - MathF.Max(Min.Y, o.Min.Y) > slack;
    }

    /// <summary>
    /// A chain's world (docs/game-design.md, Arenas): arena modules in order, each placed so its
    /// entry meets the corridor from the one before, joined by corridors (straight, or with one
    /// turn) a 3-5 s walk long, with a short lead-in before the first and a stub after the last, no
    /// two of them overlapping. Everything is in world metres, x east and y north.
    /// </summary>
    public sealed class ChainLayout
    {
        /// <summary>A corridor's length between two modules' outer walls, m: about 3.4 s at a run.</summary>
        public const float CorridorLength = 24f;

        /// <summary>The lead-in before the first module and the stub after the last, m.</summary>
        public const float LeadIn = 8f,
            Stub = 10f;

        // How far a passage's floor reaches into the rooms it joins, m: through the wall and a
        // little beyond, so it overlaps their floors.
        const float Reach = ArenaModule.Wall + 1.5f;

        public ArenaModule[] Modules;

        /// <summary>Each module's hazards, moved into the world.</summary>
        public HazardLayout[] Rooms;

        /// <summary>The walkable floor of each passage: passage k leads into module k (0: the
        /// lead-in), passage <c>Modules.Length</c> out of the last one (the stub).</summary>
        public Area[][] Passages;

        /// <summary>Each corridor leg's footprint, walls included, for overlap checks.</summary>
        public List<Area> Legs = new List<Area>();

        /// <summary>The corridors' walls.</summary>
        public List<WallBox> CorridorWalls = new List<WallBox>();

        /// <summary>Each module's entry and exit gate, across its doorway in the wall's middle:
        /// their two ends.</summary>
        public (Vector2 A, Vector2 B)[] Entries,
            Exits;

        /// <summary>The demo's walk: from the lead-in through every module, ending in the stub.
        /// <see cref="Inside"/> and <see cref="Leave"/> mark each module's first and last points
        /// inside it.</summary>
        public List<Vector2> Path = new List<Vector2>();
        public int[] Inside,
            Leave;

        /// <summary>Where the hero starts, in the lead-in.</summary>
        public Vector2 Start => Path[0];

        /// <summary>The modules of a run shuffled by <paramref name="seed"/> and laid out.</summary>
        public static ChainLayout Of(ArenaModule[] modules, int seed)
        {
            var order = (ArenaModule[])modules.Clone();
            var rng = new Random(seed);
            for (int i = order.Length - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            return In(order);
        }

        /// <summary><paramref name="order"/>'s modules laid out in that order.</summary>
        public static ChainLayout In(ArenaModule[] order)
        {
            var n = order.Length;
            var c = new ChainLayout
            {
                Modules = order,
                Rooms = new HazardLayout[n],
                Passages = new Area[n + 1][],
                Entries = new (Vector2, Vector2)[n],
                Exits = new (Vector2, Vector2)[n],
                Inside = new int[n],
                Leave = new int[n],
            };
            const float Side = ArenaModule.DoorHalf + ArenaModule.Wall;

            // The lead-in: straight into the first module's entry, closed at its far end.
            var first = order[0];
            var door = first.Entry.At(first.Half) + first.Entry.Out * ArenaModule.Wall;
            var leadEnd = door + first.Entry.Out * LeadIn;
            c.Path.Add(leadEnd - first.Entry.Out * 2f);
            c.Passages[0] = new[] { c.Leg(leadEnd, door, End.Closed, End.Room) };
            c.Legs.Add(Band(leadEnd, door, Side, ArenaModule.Wall, 0f));

            var origin = Vector2.Zero;
            var placed = new List<Area>();
            for (int k = 0; k < n; k++)
            {
                var m = order[k];
                c.Place(k, origin);
                placed.Add(Footprint(m, origin));
                var exitDoor = origin + m.Exit.At(m.Half) + m.Exit.Out * ArenaModule.Wall;
                c.Leave[k] = c.Path.Count;
                c.Path.Add(exitDoor - m.Exit.Out * (ArenaModule.Wall + 3f));
                if (k + 1 == n)
                {
                    var end = exitDoor + m.Exit.Out * Stub;
                    c.Passages[n] = new[] { c.Leg(exitDoor, end, End.Room, End.Closed) };
                    c.Legs.Add(Band(exitDoor, end, Side, 0f, ArenaModule.Wall));
                    c.Path.Add(end - m.Exit.Out * 2f);
                    break;
                }

                // The next module: through a corridor leaving along the exit's way and arriving
                // along the next entry's, with a turn between when they differ, lengthened until
                // it and the module clear everything laid so far.
                var next = order[k + 1];
                var d1 = m.Exit.Out;
                var d2 = -next.Entry.Out;
                var turns = d1 != d2;
                for (var extra = 0f; ; extra += 2f)
                {
                    var a = (turns ? CorridorLength / 2f : CorridorLength) + extra;
                    var b = turns ? CorridorLength / 2f + extra : 0f;
                    var corner = exitDoor + d1 * a;
                    var arrive = corner + d2 * b;
                    var at = arrive - next.Entry.At(next.Half) - next.Entry.Out * ArenaModule.Wall;
                    var legs = turns
                        ? new[]
                        {
                            Band(exitDoor, corner, Side, 0f, Side),
                            Band(corner, arrive, Side, Side, 0f),
                        }
                        : new[] { Band(exitDoor, arrive, Side, 0f, 0f) };
                    var box = Footprint(next, at);
                    if (!Clear(box, legs, placed, c.Legs))
                        continue;
                    c.Passages[k + 1] = turns
                        ? new[]
                        {
                            c.Leg(exitDoor, corner, End.Room, End.Turn, d2),
                            c.Leg(corner, arrive, End.Turn, End.Room, -d1),
                        }
                        : new[] { c.Leg(exitDoor, arrive, End.Room, End.Room) };
                    c.Legs.AddRange(legs);
                    if (turns)
                        c.Path.Add(corner);
                    origin = at;
                    break;
                }
            }
            return c;
        }

        // Places module k with its centre at `origin`: its hazards, gates and the walk's points
        // inside it up to its exit.
        void Place(int k, Vector2 origin)
        {
            var m = Modules[k];
            Rooms[k] = m.Layout.Moved(origin);
            Entries[k] = Gate(origin, m, m.Entry);
            Exits[k] = Gate(origin, m, m.Exit);
            Inside[k] = Path.Count;
            Path.Add(origin + m.Entry.At(m.Half) - m.Entry.Out * 3f);
            foreach (var v in m.Via)
                Path.Add(origin + v);
        }

        static (Vector2, Vector2) Gate(Vector2 origin, ArenaModule m, Socket s)
        {
            var o = s.Out;
            var mid = origin + s.At(m.Half) + o * (ArenaModule.Wall / 2f);
            var across = new Vector2(MathF.Abs(o.Y), MathF.Abs(o.X)) * ArenaModule.DoorHalf;
            return (mid - across, mid + across);
        }

        enum End
        {
            /// <summary>Joins a room through its doorway.</summary>
            Room,

            /// <summary>A dead end, walled across.</summary>
            Closed,

            /// <summary>Turns into the corridor's other leg.</summary>
            Turn,
        }

        // A corridor leg from `from` to `to`: its walls, and its floor, which reaches on into a
        // room it joins and over a turn's corner square. At a turn, `inward` is the way to the
        // turn's inner side: the outer wall runs on round the corner and the inner one stops
        // short of the other leg.
        Area Leg(Vector2 from, Vector2 to, End atFrom, End atTo, Vector2 inward = default)
        {
            var d = Vector2.Normalize(to - from);
            var w = ArenaModule.DoorHalf;
            foreach (var s in new[] { new Vector2(-d.Y, d.X), new Vector2(d.Y, -d.X) })
            {
                var inner = Vector2.Dot(s, inward) > 0f;
                var past = inner ? -w : w + ArenaModule.Wall;
                var a = from - d * (atFrom == End.Turn ? past : 0f);
                var b = to + d * (atTo == End.Turn ? past : 0f);
                CorridorWalls.Add(
                    HazardLayout.Box(
                        (a + b) / 2f + s * (w + ArenaModule.Wall / 2f),
                        Abs(b - a) / 2f + Abs(s) * (ArenaModule.Wall / 2f)
                    )
                );
            }
            if (atFrom == End.Closed)
                Cap(from, -d);
            if (atTo == End.Closed)
                Cap(to, d);
            return Band(from, to, w, Floor(atFrom), Floor(atTo));

            static float Floor(End e) =>
                e switch
                {
                    End.Room => Reach,
                    End.Turn => ArenaModule.DoorHalf,
                    _ => 0f,
                };
        }

        // A wall across a corridor's closed end at `at`, beyond it along `way`.
        void Cap(Vector2 at, Vector2 way)
        {
            var across = Abs(new Vector2(way.Y, way.X));
            CorridorWalls.Add(
                HazardLayout.Box(
                    at + way * (ArenaModule.Wall / 2f),
                    across * (ArenaModule.DoorHalf + ArenaModule.Wall)
                        + Abs(way) * (ArenaModule.Wall / 2f)
                )
            );
        }

        /// <summary>A module's footprint, walls included, centred at <paramref name="origin"/>.</summary>
        public static Area Footprint(ArenaModule m, Vector2 origin) =>
            new Area(origin - m.Half, origin + m.Half).Grown(ArenaModule.Wall);

        // The band `across` m to each side of the line from `from` to `to`, lengthened past each
        // end by `before` and `after` m.
        static Area Band(Vector2 from, Vector2 to, float across, float before, float after)
        {
            var d = Vector2.Normalize(to - from);
            var s = new Vector2(-d.Y, d.X) * across;
            return new Area(from - d * before - s, to + d * after + s);
        }

        static bool Clear(Area box, Area[] legs, List<Area> modules, List<Area> corridors)
        {
            foreach (var r in modules)
            {
                if (box.Overlaps(r))
                    return false;
                foreach (var l in legs)
                    if (l.Overlaps(r))
                        return false;
            }
            foreach (var r in corridors)
            {
                if (box.Overlaps(r))
                    return false;
                foreach (var l in legs)
                    if (l.Overlaps(r))
                        return false;
            }
            return true;
        }

        static Vector2 Abs(Vector2 v) => new Vector2(MathF.Abs(v.X), MathF.Abs(v.Y));
    }

    /// <summary>
    /// A run through a <see cref="ChainLayout"/> (docs/game-design.md, Arenas): its gates and the
    /// floor they let the hero walk. Stepping through a module's entry seals the gate behind the
    /// hero (<see cref="Step"/>); the room's clear dissolves its exit gate (<see cref="Clear"/>),
    /// opening the corridor to the next. What blocks the hero is the open floor, every module's walls
    /// and standing posts. While no room holds the hero, <see cref="Goal"/> is where a scripted walk
    /// heads next.
    /// </summary>
    public sealed class Chain : IObstacles
    {
        /// <summary>How far past its doorway the hero must be, m, for a module's entry to seal.</summary>
        public const float SealDepth = 2.5f;

        // How far ahead along the path a scripted walk aims, m.
        const float Lookahead = 3f;

        readonly bool[] entered,
            cleared;
        readonly float[] along; // each path point's distance along the path
        float progress; // how far along the path the hero has come
        Vector2? goal;

        public Chain(ChainLayout layout)
        {
            Layout = layout;
            var n = layout.Modules.Length;
            entered = new bool[n];
            cleared = new bool[n];
            Hazards = Array.ConvertAll(layout.Rooms, r => new Hazards(r));
            var path = layout.Path;
            along = new float[path.Count];
            for (int i = 1; i < path.Count; i++)
                along[i] = along[i - 1] + Vector2.Distance(path[i - 1], path[i]);
            goal = path[1];
        }

        public ChainLayout Layout { get; }

        /// <summary>Each module's hazards in play; its room plays in them.</summary>
        public Hazards[] Hazards { get; }

        public int Count => entered.Length;

        /// <summary>The module the hero is in, or heading for; <see cref="Count"/> past the last.</summary>
        public int Room { get; private set; }

        public bool Entered(int k) => entered[k];

        public bool Cleared(int k) => cleared[k];

        /// <summary>Whether the hero is sealed in a room still being fought.</summary>
        public bool Fighting => Room < Count && entered[Room];

        /// <summary>Where a scripted walk heads next: on along the path through the open gates;
        /// none while the hero is sealed in a fight.</summary>
        public Vector2? Goal => Fighting ? null : goal;

        /// <summary>Whether the hero has walked to the end of the stub past the last module.</summary>
        public bool Done => Room >= Count && progress >= along[along.Length - 1] - 0.5f;

        /// <summary>Notes the hero at <paramref name="hero"/>; true when that step took it far enough
        /// into <see cref="Room"/> to seal its entry behind it.</summary>
        public bool Step(Vector2 hero)
        {
            Walk(hero);
            if (Room >= Count || entered[Room] || !Within(Room, hero, SealDepth))
                return false;
            entered[Room] = true;
            return true;
        }

        /// <summary>The hero's room is clear: its exit gate dissolves and the next module is ahead.</summary>
        public void Clear()
        {
            if (!Fighting)
                return;
            cleared[Room] = true;
            Room++;
        }

        bool Within(int k, Vector2 p, float depth)
        {
            var floor = Layout.Rooms[k];
            var inner = floor.Half - new Vector2(depth);
            return Math.Abs(p.X - floor.Center.X) <= inner.X
                && Math.Abs(p.Y - floor.Center.Y) <= inner.Y;
        }

        // Follows the path: the hero's progress is its nearest point on it, never back, and the goal
        // a little ahead, up to the next module's first point inside, and only as far as the
        // doorway while the hero is still in the room it cleared, so it never aims through a wall.
        void Walk(Vector2 hero)
        {
            var path = Layout.Path;
            var limit = Room < Count ? along[Layout.Inside[Room]] : along[along.Length - 1];
            if (Room > 0 && Within(Room - 1, hero, 0f))
                limit = Math.Min(limit, along[Layout.Leave[Room - 1]] + 3f + ArenaModule.Wall);
            if (Fighting)
                limit = progress;
            var best = progress;
            var nearest = float.MaxValue;
            for (int i = 0; i + 1 < path.Count && along[i] <= limit; i++)
            {
                if (along[i + 1] < progress)
                    continue;
                var a = path[i];
                var d = path[i + 1] - a;
                var t = Math.Clamp(Vector2.Dot(hero - a, d) / d.LengthSquared(), 0f, 1f);
                var at = Math.Clamp(along[i] + t * d.Length(), progress, limit);
                var dist = Vector2.DistanceSquared(hero, PointAt(at));
                if (dist < nearest)
                {
                    nearest = dist;
                    best = at;
                }
            }
            progress = Math.Max(progress, best);
            goal = PointAt(Math.Min(progress + Lookahead, limit));
        }

        Vector2 PointAt(float distance)
        {
            var path = Layout.Path;
            for (int i = 0; i + 1 < path.Count; i++)
                if (distance <= along[i + 1])
                {
                    var span = along[i + 1] - along[i];
                    var t = span > 0f ? (distance - along[i]) / span : 0f;
                    return Vector2.Lerp(path[i], path[i + 1], Math.Clamp(t, 0f, 1f));
                }
            return path[path.Count - 1];
        }

        /// <summary>Whether passage <paramref name="k"/> (into module k; past the last at
        /// <see cref="Count"/>) is walkable: the gates at both its ends open.</summary>
        public bool Open(int k) => (k == 0 || cleared[k - 1]) && (k == Count || !entered[k]);

        public bool Collide(ref Vector2 p, float radius, bool flies)
        {
            var start = p;
            var at = p;
            // Onto the open floor: the modules' floors and the open passages' ...
            var best = p;
            var nearest = float.MaxValue;
            for (int k = 0; k < Count && nearest > 0f; k++)
            {
                var f = Layout.Rooms[k];
                Near(new Area(f.Center - f.Half, f.Center + f.Half));
            }
            for (int k = 0; k <= Count && nearest > 0f; k++)
                if (Open(k))
                    foreach (var r in Layout.Passages[k])
                        Near(r);
            p = best;
            // ... then out of every module's walls and standing posts.
            foreach (var h in Hazards)
                h.Block(ref p, radius);
            return p != start;

            void Near(Area r)
            {
                var inside = Vector2.Clamp(
                    at,
                    r.Min + new Vector2(radius),
                    r.Max - new Vector2(radius)
                );
                var d = Vector2.DistanceSquared(inside, at);
                if (d < nearest)
                {
                    nearest = d;
                    best = inside;
                }
            }
        }
    }
}
