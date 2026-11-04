using System;

namespace Amethyst.Action
{
    /// <summary>What a hit was, for its hitstop.</summary>
    public enum HitWeight
    {
        Light,
        Finisher,
        Kill,
    }

    /// <summary>
    /// Feel §3.1's hitstop, per target: the enemies hit freeze, the hero barely or not at all, and
    /// the whole screen only for big moments. Frames at 60 Hz, from the mode's row.
    /// </summary>
    public static class Hitstop
    {
        /// <summary>Frames the struck enemy freezes: by the hit's weight, scaled by
        /// <paramref name="scale"/> (a flag) and capped by the mode.</summary>
        public static int Target(ModeTuning mode, HitWeight weight, float scale = 1f)
        {
            var frames = weight switch
            {
                HitWeight.Kill => mode.HitstopKill,
                HitWeight.Finisher => mode.HitstopFinisher,
                _ => mode.HitstopLight,
            };
            return Math.Min(mode.HitstopCap, (int)MathF.Round(frames * scale));
        }

        /// <summary>
        /// Frames the hero freezes for a swing that hit <paramref name="hits"/> enemies: a light
        /// freeze for any hit, never more than the mode's (0 in the horde), however many it hit.
        /// </summary>
        public static int Hero(ModeTuning mode, int hits, float scale = 1f) =>
            hits <= 0 ? 0 : Math.Min(mode.HitstopCap, (int)MathF.Round(mode.HitstopHero * scale));

        /// <summary>A freeze already running stays at the longer of the two: hitstop never adds up
        /// past what one hit gives.</summary>
        public static float Extend(float leftSeconds, int frames) =>
            Math.Max(leftSeconds, frames / 60f);
    }
}
