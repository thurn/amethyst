using System;
using System.IO;
using Amethyst.Action;
using UnityEngine;

namespace Amethyst.Combat
{
    /// <summary>
    /// Feel §11.2's metrics for each room, appended as one JSON line to the run folder's
    /// metrics.jsonl (<see cref="Run.DataDir"/>) when the room ends, the hero dies or the run quits,
    /// each record naming its combat mode.
    /// </summary>
    sealed class FeelMetrics
    {
        [Serializable]
        sealed class Record
        {
            public string mode;
            public string variant;
            public int room;
            public string outcome;
            public float seconds;
            public int presses;
            public float inputHonorRate,
                bufferRate,
                dropRate,
                lockoutFraction;
            public int kills;
            public float killsPerSecond;
            public int heroHits,
                damageByContact,
                damageByTelegraph;
            public float actionsPerSecond;
        }

        readonly string mode;
        int room;
        float seconds;
        int frames,
            lockedFrames;
        int kills,
            heroHits,
            contact,
            telegraphed;
        int honored0,
            buffered0,
            dropped0;

        public FeelMetrics(CombatMode mode) => this.mode = mode.ToString().ToLowerInvariant();

        public string Path => System.IO.Path.Combine(Run.DataDir, "metrics.jsonl");

        /// <summary>A frame of the fight: its length and whether every press would be dropped.</summary>
        public void Frame(float dt, bool locked)
        {
            seconds += dt;
            frames++;
            if (locked)
                lockedFrames++;
        }

        public void Kills(int n) => kills += n;

        public void HeroHit(int damage, bool telegraphed)
        {
            heroHits++;
            if (telegraphed)
                this.telegraphed += damage;
            else
                contact += damage;
        }

        /// <summary>Writes the room's record and starts the next room's; <paramref name="presses"/>
        /// counts every press so far.</summary>
        public void End(string outcome, InputBuffer presses)
        {
            int honored = presses.Honored - honored0,
                buffered = presses.Buffered - buffered0,
                dropped = presses.Dropped - dropped0;
            int total = honored + buffered + dropped;
            var r = new Record
            {
                mode = mode,
                variant = Run.CurrentVariant?.Name ?? "",
                room = room,
                outcome = outcome,
                seconds = seconds,
                presses = total,
                inputHonorRate = Share(honored, total),
                bufferRate = Share(buffered, total),
                dropRate = Share(dropped, total),
                lockoutFraction = Share(lockedFrames, frames),
                kills = kills,
                killsPerSecond = seconds > 0f ? kills / seconds : 0f,
                heroHits = heroHits,
                damageByContact = contact,
                damageByTelegraph = telegraphed,
                actionsPerSecond = seconds > 0f ? (honored + buffered) / seconds : 0f,
            };
            File.AppendAllText(Path, JsonUtility.ToJson(r) + "\n");
            Debug.Log($"fight: room {room} {outcome}: {JsonUtility.ToJson(r)}");
            room++;
            seconds = 0f;
            frames = lockedFrames = kills = heroHits = contact = telegraphed = 0;
            honored0 = presses.Honored;
            buffered0 = presses.Buffered;
            dropped0 = presses.Dropped;
        }

        static float Share(int n, int of) => of > 0 ? (float)n / of : 0f;
    }
}
