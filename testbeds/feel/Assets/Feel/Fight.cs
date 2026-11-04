using System.Collections.Generic;
using Amethyst.Action;
using Amethyst.Look;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Num = System.Numerics;

namespace Amethyst.Feel
{
    /// <summary>
    /// One run of the feel room in its combat mode, picked once (blind picks per run): rooms of
    /// skeletons one after another (<see cref="CombatRoom"/>), every hit reported through each
    /// feedback channel (feel §3: per-target hitstop, flash, knockback, the kill shatter, trauma
    /// shake for heavy hits and the hero's damage, global hitstop with a slow-motion tail for a
    /// room's clear and Empowered kills, sounds through the voice budget) and feel §11.2's metrics
    /// per room in the run folder. The hero's death shatters it and restarts the arena.
    /// </summary>
    sealed class Fight : MonoBehaviour
    {
        const float NextRoomAfter = 2.5f;
        const float RestartAfter = 1.6f;
        const int SlowTail = 24;
        const float SlowStart = 0.25f;

        static int runs;

        Hero hero;
        Camera cam;
        FollowCamera follow;
        GridFloor floor;
        HeroKit kit;
        ModeTuning tuning;
        CombatMode mode;
        CombatRoom room;
        int seed;
        SkeletonView view;
        Shapes shapes;
        FightSounds sounds;
        FeelMetrics metrics;
        Color hue;
        readonly Trauma trauma = new Trauma();
        readonly List<Vector3> targets = new List<Vector3>();
        float clearedAt = -1f,
            diedAt = -1f;
        int freezeFrames,
            slowFrames;
        bool ended;
        int frames;
        float frameClock,
            logAt = 2f;

        /// <summary>Builds the room for a new run: picks its mode and makes its world.</summary>
        public static void Begin()
        {
            var picked = CombatModes.Pick(
                FeelFlags.Mode.Value,
                (ulong)System.DateTime.UtcNow.Ticks + (ulong)runs++
            );
            var tuning = ModeTuning.For(picked).Copy();
            if (FeelFlags.Alive.Value > 0)
                tuning.Alive = FeelFlags.Alive.Value;
            if (FeelFlags.RoomSeconds.Value > 0f)
                tuning.RoomSeconds = FeelFlags.RoomSeconds.Value;

            var palette = Palette.Default;
            var hero = Arena.Build(1, greybox: false, halfSize: tuning.ArenaHalfSize);
            var cam = Camera.main;
            cam.backgroundColor = EdgeLook.Linear(palette.Void).gamma;
            cam.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing =
                true;
            var knight = hero.transform.Find("Knight");
            if (knight != null)
                HeroLook.Dress(knight.gameObject, palette, LookFigure.Load("hero"));

            var fight = new GameObject("Fight").AddComponent<Fight>();
            fight.Init(hero, cam, picked, tuning, palette);
        }

        void Init(Hero h, Camera c, CombatMode m, ModeTuning t, Palette palette)
        {
            hero = h;
            cam = c;
            mode = m;
            tuning = t;
            follow = cam.GetComponent<FollowCamera>();
            follow.Distance = tuning.CameraDistance;
            floor = GridFloor.Create(palette.Cobalt);
            kit = HeroKit.Load();
            seed = (int)(System.DateTime.UtcNow.Ticks & 0x7fffffff);
            if (Run.Args.FixedTime)
                seed = 1;
            room = NewRoom();
            view = SkeletonView.Create(Mathf.Max(64, tuning.Alive + 32), palette);
            hue = EdgeLook.Linear(palette.ForType("skeleton")) * 1.2f;
            shapes = new Shapes();
            sounds = new FightSounds(cam.gameObject, kit);
            metrics = new FeelMetrics(mode);
            Walls(palette);
            hero.Struck += OnStruck;
            hero.Died += OnDied;
            Debug.Log(
                $"feel: {mode} mode (combat.mode is {FeelFlags.Mode.Value}), {tuning.Alive} alive, metrics to {metrics.Path}"
            );
        }

        CombatRoom NewRoom()
        {
            var r = new CombatRoom(tuning, seed++)
            {
                HitstopScale = Mathf.Max(0f, FeelFlags.Hitstop.Value),
                KnockbackScale = FeelFlags.Knockback.Value ? 1f : 0f,
                FlashFrames = 2,
            };
            return r;
        }

        // The room's edge, in the walls' grey.
        void Walls(Palette palette)
        {
            var grey = EdgeLook.Linear(palette.Wall);
            grey.a = 1f;
            var material = Flat.Make(grey, Flat.Effects - 10);
            var h = tuning.ArenaHalfSize;
            for (int i = 0; i < 4; i++)
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(wall.GetComponent<Collider>());
                wall.name = "Wall";
                wall.transform.SetParent(transform, false);
                var x = i < 2;
                var side = i % 2 == 0 ? 1f : -1f;
                wall.transform.position = x
                    ? new Vector3(0f, 0.02f, side * h)
                    : new Vector3(side * h, 0.02f, 0f);
                wall.transform.localScale = x
                    ? new Vector3(2f * h, 0.04f, 0.1f)
                    : new Vector3(0.1f, 0.04f, 2f * h);
                wall.GetComponent<Renderer>().sharedMaterial = material;
            }
        }

        void OnStruck(Strike strike)
        {
            if (hero.Dead || room.Phase == RoomPhase.Cleared)
                return;
            var (hits, _) = room.Hit(strike);
            hero.Freeze(Hitstop.Hero(tuning, hits, FeelFlags.Hitstop.Value));
            if (hits > 0 && (strike.Move == Move.CleaveC || strike.Move == Move.LeapSlam))
                Shake(0.3f);
        }

        void OnDied()
        {
            diedAt = Time.time;
            HeroShards.Burst(hero);
            sounds.Budget.Add(CombatSound.Break);
            Shake(0.8f);
            metrics.End("died", hero.Presses);
        }

        void LateUpdate()
        {
            Pace();
            var now = Time.time;
            var dt = Time.deltaTime;
            if (diedAt >= 0f && now - diedAt >= RestartAfter)
            {
                Restart();
                return;
            }
            if (
                room.Phase == RoomPhase.Cleared
                && clearedAt >= 0f
                && now - clearedAt >= NextRoomAfter
            )
            {
                room = NewRoom();
                clearedAt = -1f;
            }
            var at = new Num.Vector2(hero.transform.position.x, hero.transform.position.z);
            if (dt > 0f)
                room.Step(dt, at, hero.Vulnerable);
            Drain();
            if (dt > 0f && !hero.Dead && room.Phase != RoomPhase.Cleared)
                metrics.Frame(dt, hero.Locked);

            targets.Clear();
            foreach (var s in room.Skeletons)
                if (s.Fighting)
                    targets.Add(new Vector3(s.Position.X, 0f, s.Position.Y));
            hero.Targets = targets;

            view?.Present(room, now, FeelFlags.Flash.Value, FeelFlags.Shatter.Value);
            shapes.Begin();
            SkeletonView.Shapes(shapes, room, cam, hue, view?.Height ?? 1.8f);
            shapes.End();
            sounds.Flush(now, FeelFlags.Sound.Value);

            trauma.Step(Time.unscaledDeltaTime);
            var shake = FeelFlags.Shake.Value ? trauma.Shake : 0f;
            follow.Shake =
                shake > 0f
                    ? new Vector3(
                        Mathf.PerlinNoise(now * 23f, 0.3f) - 0.5f,
                        0f,
                        Mathf.PerlinNoise(0.7f, now * 23f) - 0.5f
                    ) * (shake * 0.9f)
                    : Vector3.zero;
            floor.Follow(cam);
            Log();
        }

        void Drain()
        {
            foreach (var e in room.Events)
            {
                view?.Note(e);
                var at = new Vector3(e.Position.X, 0f, e.Position.Y);
                switch (e.Kind)
                {
                    case RoomEventKind.Windup:
                        sounds.Budget.Add(CombatSound.Telegraph);
                        break;
                    case RoomEventKind.Hit:
                        sounds.Budget.Add(e.Killed ? CombatSound.Kill : CombatSound.Hit);
                        if (!e.Killed)
                            break;
                        metrics.Kills(1);
                        if (e.Skeleton.Empowered)
                        {
                            Freeze(tuning.FreezeElite);
                            Shake(0.5f);
                        }
                        break;
                    case RoomEventKind.ArmorBreak:
                        // A big moment: a white flash and a short global hitstop.
                        SwordEffects.Ring(at + Vector3.up * 0.05f, 1.6f * e.Skeleton.Scale, 0.25f);
                        sounds.Budget.Add(CombatSound.Break);
                        Freeze(tuning.FreezeElite / 2);
                        break;
                    case RoomEventKind.HeroHit:
                        if (hero.Hurt(e.Amount, at, e.Telegraphed))
                        {
                            metrics.HeroHit(e.Amount, e.Telegraphed);
                            Shake(e.Telegraphed ? 0.5f : 0.3f);
                        }
                        break;
                    case RoomEventKind.Buzzer:
                        SwordEffects.Ring(
                            hero.transform.position + Vector3.up * 0.05f,
                            tuning.ArenaHalfSize * 1.5f,
                            0.6f
                        );
                        sounds.Budget.Add(CombatSound.Break, 8);
                        Shake(0.5f);
                        break;
                    case RoomEventKind.Clear:
                        clearedAt = Time.time;
                        Freeze(tuning.FreezeClear);
                        if (!hero.Dead)
                            metrics.End("cleared", hero.Presses);
                        break;
                }
            }
            room.Events.Clear();
        }

        void Shake(float trauma) => this.trauma.Add(trauma);

        // Global hitstop: the game holds for `frames`, then eases back from slow motion.
        void Freeze(int frames)
        {
            if (!FeelFlags.Freeze.Value || frames <= 0)
                return;
            freezeFrames = Mathf.Max(freezeFrames, frames);
            slowFrames = 0;
            Time.timeScale = 0f;
        }

        void Pace()
        {
            if (freezeFrames > 0)
            {
                if (--freezeFrames == 0)
                    slowFrames = SlowTail;
                return;
            }
            if (slowFrames > 0)
            {
                slowFrames--;
                Time.timeScale = Mathf.Lerp(1f, SlowStart, (float)slowFrames / SlowTail);
                return;
            }
            Time.timeScale = 1f;
        }

        void Restart()
        {
            Time.timeScale = 1f;
            ended = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void Log()
        {
            frames++;
            var real = Time.realtimeSinceStartup;
            if (frames == 1)
                frameClock = real;
            if (Time.time < logAt)
                return;
            var ms = 1000f * (real - frameClock) / Mathf.Max(1, frames - 1);
            Debug.Log(
                $"feel: frame {ms:F2} ms over {frames - 1} frames; {mode}, room {room.Wave} wave, "
                    + $"{room.Alive} skeletons, {view?.OnScreen ?? 0} on screen, {view?.Shards ?? 0} shattering, "
                    + $"hero {hero.Health} HP"
            );
            frames = 0;
            logAt = Time.time + 2f;
        }

        void OnGUI()
        {
            if (Application.isBatchMode)
                return;
            var timer = float.IsInfinity(room.TimeLeft)
                ? $"wave {room.Wave}/{tuning.Waves}"
                : $"{room.TimeLeft:F0} s";
            var text =
                $"{mode}   HP {hero.Health}/{Hero.MaxHealth}   {timer}   {room.Alive} skeletons";
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.UpperRight,
            };
            GUI.Label(new Rect(Screen.width - 616, 16, 600, 30), text, style);
        }

        void OnApplicationQuit()
        {
            if (!ended && !hero.Dead && room.Phase != RoomPhase.Cleared)
                metrics.End("quit", hero.Presses);
            ended = true;
            Time.timeScale = 1f;
        }

        void OnDestroy()
        {
            view?.Dispose();
            shapes?.Dispose();
            if (hero != null)
            {
                hero.Struck -= OnStruck;
                hero.Died -= OnDied;
            }
        }
    }
}
