using Amethyst.Action;
using Amethyst.Combat;
using Amethyst.Flags;
using Amethyst.Look;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Amethyst.Chain
{
    public enum ChainWorld
    {
        /// <summary>The early tier's four arena modules in one world, walked through in order.</summary>
        Modules,

        /// <summary>One square room, its rooms one after another in place, for the roster's and the
        /// hazards' variants.</summary>
        Room,
    }

    public enum ChainCrowd
    {
        /// <summary>M1's rooms: skeletons only.</summary>
        Skeletons,

        /// <summary>Skeletons with the counter family's big orks and trolls among them.</summary>
        Heavies,

        /// <summary>The key family's fodder mixed, with Empowered skeletons, slimes, demons and bats.</summary>
        FodderElites,

        /// <summary>The key family's fodder mixed, with Empowered goblins, mushrooms, big orks and trolls.</summary>
        SpecialElites,
    }

    public static class ChainFlags
    {
        public static readonly Flag<ChainWorld> World = Flag.Enum(
            "chain.world",
            ChainWorld.Modules,
            "The four early arena modules joined by corridors, rooms 1-4 playing Escalation rows 1-4; or one room in place, by chain.crowd, chain.room and combat.hazards"
        );

        public static readonly Flag<bool> Autoclear = Flag.Bool(
            "chain.autoclear",
            false,
            "Each module's room clears as the hero steps in, so hero.demo walks the whole chain (headless checks)"
        );

        public static readonly Flag<ChainCrowd> Crowd = Flag.Enum(
            "chain.crowd",
            ChainCrowd.Skeletons,
            "What the single room spawns without chain.room: skeletons, skeletons with the counter family's big orks and trolls, or the key family's fodder mixed with Empowered fodder or Empowered specials and heavies"
        );

        public static readonly Flag<int> Room = Flag.Int(
            "chain.room",
            -1,
            "The Escalation row (0-4) each of the single room's spawn schedules is drawn from, in the mode's table; -1 plays chain.crowd by the mode's own numbers"
        );

        public static readonly Flag<float> BreakAt = Flag.Float(
            "chain.break-at",
            0f,
            "Room time, s, when every armored enemy's armor breaks at once, for stills of the break (0: never)"
        );
    }

    /// <summary>
    /// The chain (docs/game-design.md, Build order step 2): the roster, elites, hazards and
    /// connected rooms as M2 builds them, on the combat package's fight, with a variant per
    /// combat mode (StreamingAssets/variants/): the four arena modules chained, or one room.
    /// </summary>
    static class ChainEntry
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Enter()
        {
            Grade.Use();
            Build(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            SceneManager.sceneLoaded += Build;
        }

        // A variant switch or the hero's death reloads the scene, which must be rebuilt.
        static void Build(Scene scene, LoadSceneMode mode)
        {
            if (Object.FindAnyObjectByType<Fight>() != null)
                return;
            if (ChainFlags.World.Value == ChainWorld.Modules)
                Fight.BeginChain(ArenaModule.Early, ChainFlags.Autoclear.Value);
            else
                Fight.Begin(
                    ChainFlags.Crowd.Value switch
                    {
                        ChainCrowd.Heavies => SpawnList.Heavies,
                        ChainCrowd.FodderElites => SpawnList.FodderElites,
                        ChainCrowd.SpecialElites => SpawnList.SpecialElites,
                        _ => SpawnList.Skeletons,
                    },
                    Mathf.Clamp(ChainFlags.Room.Value, -1, 4)
                );
            if (ChainFlags.BreakAt.Value > 0f)
                new GameObject("Break").AddComponent<StagedBreak>();
        }
    }

    /// <summary>Breaks every armored enemy's armor at <see cref="ChainFlags.BreakAt"/> in the first
    /// room, so a still can catch the break.</summary>
    sealed class StagedBreak : MonoBehaviour
    {
        void LateUpdate()
        {
            var room = Object.FindAnyObjectByType<Fight>()?.Room;
            if (room == null || room.Time < ChainFlags.BreakAt.Value)
                return;
            foreach (var s in room.Enemies)
                if (s.Fighting && s.Armored)
                    room.BreakArmor(s, System.Numerics.Vector2.UnitY);
            Destroy(gameObject);
        }
    }
}
