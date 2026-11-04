using System.Collections.Generic;
using Amethyst.Action;
using Amethyst.Look;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Num = System.Numerics;

namespace Amethyst.Combat
{
    /// <summary>
    /// One run of a fight in its combat mode, picked once (blind picks per run): rooms of its
    /// <see cref="SpawnList"/>'s enemies one after another (<see cref="CombatRoom"/>), every hit
    /// reported through each feedback channel (feel §3: per-target hitstop, flash, knockback, the
    /// kill shatter, trauma shake for heavy hits and the hero's damage, global hitstop with a
    /// slow-motion tail for a room's clear and Empowered kills, sounds through the voice budget)
    /// and feel §11.2's metrics per room in the run folder; each room in its hazard layout
    /// (<see cref="CombatFlags.Hazards"/>), drawn by <see cref="HazardView"/>. The hero's death
    /// shatters it and restarts the arena (the scene reloads; its entry begins the fight again).
    /// </summary>
    public sealed class Fight : MonoBehaviour
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
        SpawnList spawns;
        HazardLayout layout;
        HazardView hazards;
        CombatRoom room;
        int seed;
        Palette palette;
        EnemyView view;
        Shapes shapes;
        FightSounds sounds;
        FeelMetrics metrics;
        readonly Trauma trauma = new Trauma();
        readonly List<Vector3> targets = new List<Vector3>();
        float clearedAt = -1f,
            diedAt = -1f;

        /// <summary>The room being fought.</summary>
        public CombatRoom Room => room;

        int freezeFrames,
            slowFrames;
        bool ended;
        int frames;
        float frameClock,
            logAt = 2f;

        /// <summary>Builds the room for a new run of <paramref name="spawns"/> (M1's skeletons by
        /// default): picks its mode and makes its world.</summary>
        public static void Begin(SpawnList spawns = null)
        {
            var picked = CombatModes.Pick(
                CombatFlags.Mode.Value,
                (ulong)System.DateTime.UtcNow.Ticks + (ulong)runs++
            );
            var tuning = ModeTuning.For(picked).Copy();
            if (CombatFlags.Alive.Value > 0)
                tuning.Alive = CombatFlags.Alive.Value;
            if (CombatFlags.RoomSeconds.Value > 0f)
                tuning.RoomSeconds = CombatFlags.RoomSeconds.Value;
            if (CombatFlags.Elites.Value >= 0)
            {
                tuning.RoomElites = CombatFlags.Elites.Value;
                tuning.ElitesAlive = CombatFlags.Elites.Value;
            }
            if (CombatFlags.EliteEvery.Value > 0f)
                tuning.EliteEvery = CombatFlags.EliteEvery.Value;

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
            fight.Init(hero, cam, picked, tuning, palette, spawns ?? SpawnList.Skeletons);
        }

        void Init(Hero h, Camera c, CombatMode m, ModeTuning t, Palette p, SpawnList list)
        {
            palette = p;
            spawns = list;
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
            layout = HazardLayout.Of(CombatFlags.Hazards.Value, tuning.ArenaHalfSize);
            hazards = new HazardView(layout, palette, tuning.CameraDistance * 0.0015f);
            room = NewRoom();
            view = EnemyView.Create(Mathf.Max(64, tuning.Alive + 32), palette);
            shapes = new Shapes();
            sounds = new FightSounds(cam.gameObject, kit);
            metrics = new FeelMetrics(mode);
            hero.Struck += OnStruck;
            hero.Died += OnDied;
            Debug.Log(
                $"fight: {mode} mode (combat.mode is {CombatFlags.Mode.Value}), {tuning.Alive} alive, metrics to {metrics.Path}"
            );
        }

        CombatRoom NewRoom()
        {
            var r = new CombatRoom(tuning, seed++, spawns, layout)
            {
                HitstopScale = Mathf.Max(0f, CombatFlags.Hitstop.Value),
                KnockbackScale = CombatFlags.Knockback.Value ? 1f : 0f,
                FlashFrames = 2,
            };
            Arena.Hazards = r.Hazards;
            return r;
        }

        void OnStruck(Strike strike)
        {
            if (hero.Dead || room.Phase == RoomPhase.Cleared)
                return;
            var (hits, _) = room.Hit(strike);
            hero.Freeze(Hitstop.Hero(tuning, hits, CombatFlags.Hitstop.Value));
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
            foreach (var s in room.Enemies)
                if (s.Fighting)
                    targets.Add(new Vector3(s.Position.X, 0f, s.Position.Y));
            hero.Targets = targets;

            view?.Present(room, now, CombatFlags.Flash.Value, CombatFlags.Shatter.Value);
            shapes.Begin();
            hazards.Present(shapes, room);
            EnemyView.Shapes(shapes, room, cam, palette, view);
            shapes.End();
            sounds.Flush(now, CombatFlags.Sound.Value);

            trauma.Step(Time.unscaledDeltaTime);
            var shake = CombatFlags.Shake.Value ? trauma.Shake : 0f;
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
                hazards.Note(e, room.Time);
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
                        if (e.Enemy.Empowered)
                        {
                            Freeze(tuning.FreezeElite);
                            Shake(0.5f);
                        }
                        break;
                    case RoomEventKind.ArmorBreak:
                        // The big moment: the body's held white flash, its outer outline
                        // shattering (the view's), a white ring, a break and a global hitstop.
                        SwordEffects.Ring(at + Vector3.up * 0.05f, 1.6f * e.Enemy.Scale, 0.25f);
                        sounds.Budget.Add(CombatSound.Break, 2);
                        Freeze(tuning.FreezeArmorBreak);
                        Shake(0.45f);
                        break;
                    case RoomEventKind.HeroHit:
                        if (hero.Hurt(e.Amount, at, e.Telegraphed))
                        {
                            metrics.HeroHit(e.Amount, e.Telegraphed);
                            Shake(e.Telegraphed ? 0.5f : 0.3f);
                        }
                        break;
                    case RoomEventKind.Burst:
                        sounds.Budget.Add(CombatSound.Break, 3);
                        Shake(0.4f);
                        break;
                    case RoomEventKind.Erupt:
                        sounds.Budget.Add(CombatSound.Hit);
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
            if (!CombatFlags.Freeze.Value || frames <= 0)
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
                $"fight: frame {ms:F2} ms over {frames - 1} frames; {mode}, room {room.Wave} wave, "
                    + $"{room.Alive} enemies, {view?.OnScreen ?? 0} on screen, {view?.Shards ?? 0} shattering, "
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
                $"{mode}   HP {hero.Health}/{Hero.MaxHealth}   {timer}   {room.Alive} enemies";
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
            hazards?.Dispose();
            if (hero != null)
            {
                hero.Struck -= OnStruck;
                hero.Died -= OnDied;
            }
        }
    }
}
