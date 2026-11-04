using Amethyst.Action;
using Amethyst.Flags;

namespace Amethyst.Feel
{
    /// <summary>The feel room's combat mode and its feedback channels, each with its own toggle (feel §11.1).</summary>
    public static class FeelFlags
    {
        public static readonly Flag<CombatMode> Mode = Flag.Enum(
            "combat.mode",
            CombatMode.Focused,
            "Focused: a few tough skeletons that all telegraph; horde: a stream of contact-damage fodder; blind: one of the two at random each run"
        );

        public static readonly Flag<int> Alive = Flag.Int(
            "combat.alive",
            0,
            "Skeletons alive at once in the horde (0 keeps the mode's)"
        );

        public static readonly Flag<float> RoomSeconds = Flag.Float(
            "combat.room-seconds",
            0f,
            "The horde room's timer, s, until its buzzer (0 keeps the mode's)"
        );

        public static readonly Flag<float> Hitstop = Flag.Float(
            "feedback.hitstop",
            1f,
            "Per-target hitstop, times the mode's frames: 0 turns it off"
        );

        public static readonly Flag<bool> Flash = Flag.Bool(
            "feedback.flash",
            true,
            "A struck skeleton flashes white for 2 frames"
        );

        public static readonly Flag<bool> Knockback = Flag.Bool(
            "feedback.knockback",
            true,
            "Hits push skeletons along the attack"
        );

        public static readonly Flag<bool> Shatter = Flag.Bool(
            "feedback.shatter",
            true,
            "Kills shatter in the skeleton's hue (off: the body just goes)"
        );

        public static readonly Flag<bool> Shake = Flag.Bool(
            "feedback.shake",
            true,
            "Trauma-model screen shake for heavy hits and the hero's damage"
        );

        public static readonly Flag<bool> Freeze = Flag.Bool(
            "feedback.freeze",
            true,
            "Global hitstop with a slow-motion tail for a room's clear and Empowered kills"
        );

        public static readonly Flag<bool> Sound = Flag.Bool(
            "feedback.sound",
            true,
            "Hit, kill and telegraph sounds through the voice budget"
        );
    }
}
