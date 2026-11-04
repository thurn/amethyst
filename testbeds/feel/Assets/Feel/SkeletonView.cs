using System;
using System.Collections.Generic;
using Amethyst.Action;
using Amethyst.Look;
using UnityEngine;

namespace Amethyst.Feel
{
    /// <summary>
    /// Draws a <see cref="CombatRoom"/>'s skeletons through the look's crowd in the key family's hue:
    /// each state's clip (walk or idle, Attack 1 up to its impact over the telegraph and on through
    /// its recovery, Got Hit for a stagger) crossfaded, a telegraph's flare, the hit flash, a
    /// hitstop's shiver along the blow and a kick spring; and on its break the kill shatter.
    /// Ground shapes (spawn markers, telegraph wedges, the Empowered's ring and armor bar) are
    /// <see cref="Shapes"/>'.
    /// </summary>
    sealed class SkeletonView : IDisposable
    {
        const float Fade = 0.1f;
        const float Shiver = 0.05f;

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

        readonly int capacity;
        readonly MonsterCrowd crowd;
        readonly MonsterShatter shatter;
        readonly int type;
        readonly MonsterType skeleton;
        readonly int idle,
            walk,
            attack,
            hit;
        readonly float impact,
            stride;
        readonly Dictionary<int, Pose> poses = new Dictionary<int, Pose>();
        readonly List<Skeleton> breaking = new List<Skeleton>();
        readonly List<Skeleton> drawn = new List<Skeleton>();
        int serial;

        SkeletonView(MonsterLibrary lib, int type, int capacity, Palette palette)
        {
            this.capacity = capacity;
            this.type = type;
            skeleton = lib.Types[type];
            crowd = new MonsterCrowd(lib, capacity, palette);
            shatter = new MonsterShatter(crowd, palette);
            idle = Mathf.Max(0, skeleton.Clip("idle"));
            walk = Mathf.Max(0, skeleton.Clip("walk"));
            attack = Mathf.Max(0, skeleton.Clip("attack"));
            hit = Mathf.Max(0, skeleton.Clip("hit"));
            var a = skeleton.Clips[attack];
            impact = a.Impact > 0f ? a.Impact : a.Length * 0.5f;
            stride = skeleton.Clips[walk].StrideM > 0f ? skeleton.Clips[walk].StrideM : 1.2f;
        }

        /// <summary>The skeleton's view, or null when it isn't baked (its vendor files are missing).</summary>
        public static SkeletonView Create(int capacity, Palette palette)
        {
            var lib = MonsterLibrary.Load();
            var type = lib?.IndexOf("skeleton") ?? -1;
            if (type < 0)
            {
                Debug.LogWarning("feel: no baked skeleton (its vendor files are missing)");
                return null;
            }
            return new SkeletonView(lib, type, capacity, palette);
        }

        public float Height => skeleton.HeightM;

        public int OnScreen => crowd.OnScreen;

        public int Shards => shatter.LiveDeaths;

        /// <summary>Notes what this frame's events mean for the poses: hits kick, shatters break.</summary>
        public void Note(in RoomEvent e)
        {
            var s = e.Skeleton;
            if (s == null)
                return;
            switch (e.Kind)
            {
                case RoomEventKind.Hit:
                    var p = PoseOf(s);
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

        /// <summary>Writes this frame's figures and breaks: every skeleton in <paramref name="room"/>
        /// and those that shattered this frame.</summary>
        public void Present(CombatRoom room, float time, bool flash, bool shards)
        {
            drawn.Clear();
            foreach (var s in room.Skeletons)
                if (s.State != SkeletonState.Arriving)
                    drawn.Add(s);
            int live = drawn.Count;
            drawn.AddRange(breaking);
            var count = Mathf.Min(drawn.Count, capacity);
            if (count == 0)
            {
                crowd.Present(time);
                shatter.Advance(time);
                return;
            }
            var instances = crowd.Instances(type, count);
            var motion = crowd.Motion(type, count);
            for (int i = 0; i < count; i++)
            {
                var s = drawn[i];
                var broken = i >= live;
                var body = Instance(s, flash);
                if (broken)
                {
                    body.Flags = (uint)CrowdFlags.Dying;
                    if (shards)
                        shatter.Break(
                            type,
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
                motion[i] = Motion(s);
            }
            breaking.Clear();
            crowd.Present(time);
            shatter.Advance(time);
        }

        MonsterInstance Instance(Skeleton s, bool flash)
        {
            var p = PoseOf(s);
            var moving = s.Walked > p.LastWalked + 1e-4f;
            p.LastWalked = s.Walked;
            int clip;
            float time;
            // An Empowered skeleton's lines burn brighter: with its ring, its doubled outline.
            float flare = s.Empowered ? 0.35f : 0f;
            switch (s.State)
            {
                case SkeletonState.WindingUp:
                    clip = attack;
                    var f = Mathf.Clamp01(s.Age / Mathf.Max(s.Windup, 1e-3f));
                    time = f * impact;
                    flare = Mathf.Max(flare, 0.3f + 0.7f * f);
                    break;
                case SkeletonState.Recovering:
                    clip = attack;
                    time = impact + s.Age;
                    break;
                case SkeletonState.Staggered:
                    clip = hit;
                    time = s.Age;
                    break;
                default:
                    clip = moving ? walk : idle;
                    time = clip == walk ? s.Walked / stride * skeleton.Clips[walk].Length : s.Clock;
                    time += (s.Seed & 1023) / 1023f * skeleton.Clips[clip].Length;
                    break;
            }
            if (clip != p.Clip && s.State != SkeletonState.Dying)
            {
                p.PrevClip = p.Clip;
                p.PrevTime = s.Clock - p.From;
                p.Since = s.Clock;
                p.Clip = clip;
                p.From = s.Clock;
            }
            poses[s.Id] = p;
            var blend = Mathf.Clamp01((s.Clock - p.Since) / Fade);
            var position = new Vector3(s.Position.X, 0f, s.Position.Y);
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

        MonsterMotion Motion(Skeleton s)
        {
            var m = MonsterMotion.Still(s.Seed);
            var p = PoseOf(s);
            if (p.Kick != Vector2.zero)
            {
                m.KickAge = s.Clock - p.KickAt;
                m.Kick = p.Kick;
            }
            return m;
        }

        Pose PoseOf(Skeleton s)
        {
            if (!poses.TryGetValue(s.Id, out var p))
                p = new Pose
                {
                    Clip = walk,
                    PrevClip = walk,
                    From = s.Clock,
                    Since = -1f,
                    LastWalked = s.Walked,
                };
            return p;
        }

        /// <summary>
        /// The skeletons' ground shapes: each spawn marker filling over its time, each telegraph a
        /// red wedge filling toward its impact (above the hero's effects), and an Empowered one's
        /// ring in its hue and armor bar.
        /// </summary>
        public static void Shapes(
            Shapes shapes,
            CombatRoom room,
            Camera camera,
            Color hue,
            float height
        )
        {
            var marker = new Color(hue.r, hue.g, hue.b, 0.9f);
            var markerFill = new Color(hue.r, hue.g, hue.b, 0.3f);
            var telegraph = Danger(0.25f);
            var telegraphFill = Danger(0.55f);
            foreach (var s in room.Skeletons)
            {
                var at = new Vector3(s.Position.X, 0.02f, s.Position.Y);
                switch (s.State)
                {
                    case SkeletonState.Arriving:
                        var f = 1f - Mathf.Clamp01((s.ArriveAt - room.Time) / s.MarkerSeconds);
                        shapes.Ring(at, 0.55f * s.Scale, marker, Flat.Markers);
                        shapes.Disc(at, 0.55f * s.Scale * f, markerFill, Flat.Markers);
                        continue;
                    case SkeletonState.WindingUp:
                        var toward = new Vector3(s.AttackDirection.X, 0f, s.AttackDirection.Y);
                        var fill = Mathf.Clamp01(s.Age / Mathf.Max(s.Windup, 1e-3f));
                        var reach = 2.2f * s.Scale;
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
                }
                if (s.Empowered && s.State != SkeletonState.Dying)
                {
                    shapes.Ring(at, 0.75f * s.Scale, marker, Flat.Markers);
                    if (s.MaxArmor > 0)
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

        // Hazards and enemy attacks: filled red and orange shapes (docs/game-design.md, Look).
        static Color Danger(float alpha)
        {
            var c = EdgeLook.Linear(Palette.Default.EnemyAttack) * 1.6f;
            c.a = alpha;
            return c;
        }

        public void Dispose()
        {
            shatter.Dispose();
            crowd.Dispose();
        }
    }
}
