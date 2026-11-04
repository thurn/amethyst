using System;
using System.Collections.Generic;
using Amethyst.Action;
using Amethyst.Look;
using UnityEngine;

namespace Amethyst.Combat
{
    /// <summary>
    /// Draws a <see cref="CombatRoom"/>'s enemies through the look's crowd, each kind as its baked
    /// crowd type in its palette family's hue: each state's clip (walk or idle, the attack up to its
    /// impact over the telegraph and on through its recovery, the hit clip for a stagger)
    /// crossfaded, a telegraph's flare, the hit flash, a hitstop's shiver along the blow and a kick
    /// spring; and on its break the kill shatter. Ground shapes (spawn markers, telegraphs, the
    /// Empowered's ring and armor bar) are <see cref="Shapes"/>'.
    /// </summary>
    sealed class EnemyView : IDisposable
    {
        const float Fade = 0.1f;
        const float Shiver = 0.05f;
        const float DefaultHeight = 1.8f;

        struct Pose
        {
            public int Clip;
            public float From; // its clock when the clip started
            public int PrevClip;
            public float PrevTime;
            public float Since; // its clock when the crossfade began
            public float LastWalked;
            public float KickAt;
            public Vector2 Kick;
            public Vector2 Blow;
            public float FreezeFrom;
        }

        // A crowd type's clips, and the figures it draws this frame: the live ones, then the breaks.
        sealed class Kind
        {
            public int Type;
            public MonsterType Monster;
            public int Idle,
                Walk,
                Attack,
                Hit;
            public float Impact,
                Stride;
            public readonly List<Enemy> Drawn = new List<Enemy>();
            public int Live;
        }

        readonly int capacity;
        readonly MonsterLibrary lib;
        readonly MonsterCrowd crowd;
        readonly MonsterShatter shatter;
        readonly Dictionary<EnemyKind, Kind> kinds = new Dictionary<EnemyKind, Kind>();
        readonly Dictionary<int, Pose> poses = new Dictionary<int, Pose>();
        readonly List<Enemy> breaking = new List<Enemy>();
        int serial;

        EnemyView(MonsterLibrary lib, int capacity, Palette palette)
        {
            this.lib = lib;
            this.capacity = capacity;
            crowd = new MonsterCrowd(lib, capacity, palette);
            shatter = new MonsterShatter(crowd, palette);
        }

        /// <summary>The enemies' view, with room for <paramref name="capacity"/> of each kind, or
        /// null when no crowd type is baked (the project's vendor files are missing).</summary>
        public static EnemyView Create(int capacity, Palette palette)
        {
            var lib = MonsterLibrary.Load();
            if (lib == null)
            {
                Debug.LogWarning("fight: no baked crowd types (the vendor files are missing)");
                return null;
            }
            return new EnemyView(lib, capacity, palette);
        }

        public int OnScreen => crowd.OnScreen;

        public int Shards => shatter.LiveDeaths;

        /// <summary>A kind's figure's top above the ground, m: its height over its hover.</summary>
        public float HeightOf(EnemyKind kind) =>
            Of(kind) is Kind k ? k.Monster.HeightM + k.Monster.HoverM : DefaultHeight;

        // A kind's crowd type, or null (warned once) when it isn't baked.
        Kind Of(EnemyKind enemy)
        {
            if (kinds.TryGetValue(enemy, out var k))
                return k;
            var type = lib.IndexOf(enemy.Id);
            if (type < 0)
            {
                Debug.LogWarning(
                    $"fight: no baked crowd type '{enemy.Id}' (its vendor files are missing)"
                );
                kinds[enemy] = null;
                return null;
            }
            var m = lib.Types[type];
            k = new Kind
            {
                Type = type,
                Monster = m,
                Idle = Mathf.Max(0, m.Clip("idle")),
                Walk = Mathf.Max(0, m.Clip("walk")),
                Attack = Mathf.Max(0, m.Clip("attack")),
                Hit = Mathf.Max(0, m.Clip("hit")),
            };
            var a = m.Clips[k.Attack];
            k.Impact = a.Impact > 0f ? a.Impact : a.Length * 0.5f;
            k.Stride = m.Clips[k.Walk].StrideM > 0f ? m.Clips[k.Walk].StrideM : 1.2f;
            kinds[enemy] = k;
            return k;
        }

        /// <summary>Notes what this frame's events mean for the poses: hits kick, shatters break.</summary>
        public void Note(in RoomEvent e)
        {
            var s = e.Enemy;
            if (s == null)
                return;
            switch (e.Kind)
            {
                case RoomEventKind.Hit:
                    var p = PoseOf(s, Of(s.Kind));
                    p.KickAt = s.Clock;
                    p.Kick = new Vector2(e.Direction.X, e.Direction.Y) * (e.Killed ? 1f : 0.7f);
                    p.Blow = p.Kick;
                    p.FreezeFrom = Mathf.Max(s.Frozen, 1e-3f);
                    poses[s.Id] = p;
                    break;
                case RoomEventKind.Shatter:
                    breaking.Add(s);
                    break;
            }
        }

        /// <summary>Writes this frame's figures and breaks: every enemy in <paramref name="room"/>
        /// and those that shattered this frame, by kind.</summary>
        public void Present(CombatRoom room, float time, bool flash, bool shards)
        {
            foreach (var k in kinds.Values)
                if (k != null)
                    k.Drawn.Clear();
            foreach (var s in room.Enemies)
                if (s.State != EnemyState.Arriving)
                    Of(s.Kind)?.Drawn.Add(s);
            foreach (var k in kinds.Values)
                if (k != null)
                    k.Live = k.Drawn.Count;
            foreach (var s in breaking)
                Of(s.Kind)?.Drawn.Add(s);
            breaking.Clear();
            foreach (var k in kinds.Values)
                if (k != null && k.Drawn.Count > 0)
                    Draw(k, time, flash, shards);
            crowd.Present(time);
            shatter.Advance(time);
        }

        void Draw(Kind k, float time, bool flash, bool shards)
        {
            var count = Mathf.Min(k.Drawn.Count, capacity);
            var instances = crowd.Instances(k.Type, count);
            var motion = crowd.Motion(k.Type, count);
            for (int i = 0; i < count; i++)
            {
                var s = k.Drawn[i];
                var body = Instance(s, k, flash);
                if (i >= k.Live)
                {
                    body.Flags = (uint)CrowdFlags.Dying;
                    if (shards)
                        shatter.Break(
                            k.Type,
                            i,
                            body,
                            serial++,
                            time,
                            new Vector2(s.Blow.X, s.Blow.Y),
                            s.Force > 0f ? s.Force * 0.4f : 1.85f
                        );
                    poses.Remove(s.Id);
                }
                instances[i] = body;
                motion[i] = Motion(s, k);
            }
        }

        MonsterInstance Instance(Enemy s, Kind k, bool flash)
        {
            var p = PoseOf(s, k);
            var moving = s.Walked > p.LastWalked + 1e-4f;
            p.LastWalked = s.Walked;
            int clip;
            float time;
            // An Empowered enemy's lines burn brighter: with its ring, its doubled outline.
            float flare = s.Empowered ? 0.35f : 0f;
            switch (s.State)
            {
                case EnemyState.WindingUp:
                    clip = k.Attack;
                    var f = Mathf.Clamp01(s.Age / Mathf.Max(s.Windup, 1e-3f));
                    time = f * k.Impact;
                    flare = Mathf.Max(flare, 0.3f + 0.7f * f);
                    break;
                case EnemyState.Lunging:
                    // The strike's pose held through the dash, burning.
                    clip = k.Attack;
                    time = k.Impact;
                    flare = Mathf.Max(flare, 1f);
                    break;
                case EnemyState.Recovering:
                    clip = k.Attack;
                    time = k.Impact + s.Age;
                    break;
                case EnemyState.Staggered:
                    clip = k.Hit;
                    time = s.Age;
                    break;
                default:
                    clip = moving ? k.Walk : k.Idle;
                    var clips = k.Monster.Clips;
                    time = clip == k.Walk ? s.Walked / k.Stride * clips[k.Walk].Length : s.Clock;
                    time += (s.Seed & 1023) / 1023f * clips[clip].Length;
                    break;
            }
            if (clip != p.Clip && s.State != EnemyState.Dying)
            {
                p.PrevClip = p.Clip;
                p.PrevTime = s.Clock - p.From;
                p.Since = s.Clock;
                p.Clip = clip;
                p.From = s.Clock;
            }
            poses[s.Id] = p;
            var blend = Mathf.Clamp01((s.Clock - p.Since) / Fade);
            var position = new Vector3(s.Position.X, k.Monster.HoverM * s.Scale, s.Position.Y);
            if (s.Frozen > 0f)
            {
                // Feel §3.1: the struck body shivers along the blow, decaying through its freeze.
                var left = Mathf.Clamp01(s.Frozen / Mathf.Max(p.FreezeFrom, 1e-3f));
                var sign = (Time.frameCount & 1) == 0 ? 1f : -1f;
                position += new Vector3(p.Blow.x, 0f, p.Blow.y) * (Shiver * left * sign);
            }
            return new MonsterInstance
            {
                Position = position,
                Yaw = s.Yaw,
                ClipA = (uint)clip,
                TimeA = time,
                ClipB = (uint)p.PrevClip,
                TimeB = p.PrevTime,
                Blend = blend,
                Scale = s.Scale,
                HitFlash = flash && s.Flash > 0f ? 1f : 0f,
                Flare = flare,
                Flags = (uint)CrowdFlags.Alive,
            };
        }

        MonsterMotion Motion(Enemy s, Kind k)
        {
            var m = MonsterMotion.Still(s.Seed);
            var p = PoseOf(s, k);
            if (p.Kick != Vector2.zero)
            {
                m.KickAge = s.Clock - p.KickAt;
                m.Kick = p.Kick;
            }
            return m;
        }

        Pose PoseOf(Enemy s, Kind k)
        {
            if (!poses.TryGetValue(s.Id, out var p))
            {
                var walk = k?.Walk ?? 0;
                p = new Pose
                {
                    Clip = walk,
                    PrevClip = walk,
                    From = s.Clock,
                    Since = -1f,
                    LastWalked = s.Walked,
                };
            }
            return p;
        }

        /// <summary>
        /// The enemies' ground shapes, each in its kind's hue: each spawn marker filling over its
        /// time, each telegraph a red shape filling toward its impact (above the hero's effects; a
        /// cone's a wedge, a lunge's its lane, a volley's the circle it lands in, filling on through
        /// the spores' flight), the spores in the air, an Empowered one's ring and an armored one's
        /// armor bar. <paramref name="view"/> sets the bars' heights; without one they float at a
        /// skeleton's.
        /// </summary>
        public static void Shapes(
            Shapes shapes,
            CombatRoom room,
            Camera camera,
            Palette palette,
            EnemyView view
        )
        {
            var telegraph = Danger(palette, 0.25f);
            var telegraphFill = Danger(palette, 0.55f);
            foreach (var v in room.Volleys)
            {
                var flight = Mathf.Clamp01((room.Time - v.Launched) / (v.Lands - v.Launched));
                Landing(shapes, v.Target, v.Radius, 0.5f + 0.5f * flight, palette);
                for (int i = 0; i < v.Landings.Length; i++)
                {
                    var (spore, height) = v.Spore(i, room.Time);
                    var p = new Vector3(spore.X, height + 0.3f, spore.Y);
                    shapes.Disc(p, 0.22f, Danger(palette, 1f), Flat.Telegraphs + 2);
                }
            }
            foreach (var s in room.Enemies)
            {
                var at = new Vector3(s.Position.X, 0.02f, s.Position.Y);
                switch (s.State)
                {
                    case EnemyState.Arriving:
                        var hue = Hue(palette, s.Kind);
                        var f = 1f - Mathf.Clamp01((s.ArriveAt - room.Time) / s.MarkerSeconds);
                        shapes.Ring(at, 0.55f * s.Scale, Alpha(hue, 0.9f), Flat.Markers);
                        shapes.Disc(at, 0.55f * s.Scale * f, Alpha(hue, 0.3f), Flat.Markers);
                        continue;
                    case EnemyState.WindingUp:
                        var fill = Mathf.Clamp01(s.Age / Mathf.Max(s.Windup, 1e-3f));
                        switch (s.Kind.Attack)
                        {
                            case AttackKind.Cone:
                                var toward = new Vector3(
                                    s.AttackDirection.X,
                                    0f,
                                    s.AttackDirection.Y
                                );
                                var reach = (s.Kind.Reach + 0.2f) * s.Scale;
                                shapes.Wedge(at, toward, reach, 130, telegraph, Flat.Telegraphs);
                                shapes.Wedge(
                                    at,
                                    toward,
                                    reach * fill,
                                    130,
                                    telegraphFill,
                                    Flat.Telegraphs + 1
                                );
                                break;
                            case AttackKind.Lunge:
                                Lane(shapes, s, fill, telegraph, telegraphFill);
                                break;
                            case AttackKind.Volley:
                                Landing(
                                    shapes,
                                    s.Aim,
                                    CombatRoom.VolleyRadius * s.Scale,
                                    0.5f * fill,
                                    palette
                                );
                                break;
                        }
                        break;
                    case EnemyState.Lunging:
                        Lane(shapes, s, 1f, telegraph, telegraphFill);
                        break;
                }
                if (s.State != EnemyState.Dying && (s.Empowered || s.MaxArmor > 0))
                {
                    var hue = Hue(palette, s.Kind);
                    if (s.Empowered)
                        shapes.Ring(at, 0.75f * s.Scale, Alpha(hue, 0.9f), Flat.Markers);
                    if (s.MaxArmor > 0)
                    {
                        var height = view?.HeightOf(s.Kind) ?? DefaultHeight;
                        shapes.Bar(
                            at + Vector3.up * (height * s.Scale + 0.4f),
                            camera,
                            1.2f,
                            (float)s.Armor / s.MaxArmor,
                            new Color(0.08f, 0.08f, 0.1f, 0.8f),
                            new Color(hue.r * 1.4f, hue.g * 1.4f, hue.b * 1.4f, 1f)
                        );
                    }
                }
            }
        }

        // A lunge's lane from where it crouched, filled to `fill` of its length.
        static void Lane(Shapes shapes, Enemy s, float fill, Color back, Color front)
        {
            var at = new Vector3(s.Aim.X, 0.02f, s.Aim.Y);
            var toward = new Vector3(s.AttackDirection.X, 0f, s.AttackDirection.Y);
            var length = s.Kind.Reach * s.Scale;
            var width = 2f * CombatRoom.LaneHalfWidth * s.Scale;
            shapes.Lane(at, toward, length, width, back, Flat.Telegraphs);
            shapes.Lane(at, toward, length * fill, width, front, Flat.Telegraphs + 1);
        }

        // A volley's landing circle, its outline and its fill to `fill` of its radius.
        static void Landing(
            Shapes shapes,
            System.Numerics.Vector2 at,
            float radius,
            float fill,
            Palette palette
        )
        {
            var p = new Vector3(at.X, 0.02f, at.Y);
            shapes.Ring(p, radius, Danger(palette, 0.9f), Flat.Telegraphs);
            shapes.Disc(p, radius, Danger(palette, 0.2f), Flat.Telegraphs);
            shapes.Disc(p, radius * fill, Danger(palette, 0.55f), Flat.Telegraphs + 1);
        }

        static Color Hue(Palette palette, EnemyKind kind) =>
            EdgeLook.Linear(palette.ForType(kind.Id)) * 1.2f;

        static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // Hazards and enemy attacks: filled red and orange shapes (docs/game-design.md, Look).
        static Color Danger(Palette palette, float alpha) =>
            Alpha(EdgeLook.Linear(palette.EnemyAttack) * 1.6f, alpha);

        public void Dispose()
        {
            shatter.Dispose();
            crowd.Dispose();
        }
    }
}
