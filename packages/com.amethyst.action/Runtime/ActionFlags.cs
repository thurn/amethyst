using Amethyst.Flags;

namespace Amethyst.Action
{
    public enum AimMode
    {
        Cursor,
        Movement,
    }

    public static class ActionFlags
    {
        public static readonly Flag<float> Speed = Flag.Float(
            "hero.speed",
            7f,
            "The hero's run speed, m/s: faster than every enemy"
        );

        public static readonly Flag<AimMode> Aim = Flag.Enum(
            "hero.aim",
            AimMode.Cursor,
            "Cursor: face and aim at the mouse, snapping to enemies; movement: aim where the hero runs"
        );

        public static readonly Flag<float> SnapAngle = Flag.Float(
            "hero.aim-snap",
            25f,
            "Cursor aim snaps to the nearest enemy within this many degrees"
        );

        public static readonly Flag<int> Buffer = Flag.Int(
            "hero.buffer",
            9,
            "Frames (60 Hz) an early press waits to become legal; the last press wins"
        );

        public static readonly Flag<float> DashDistance = Flag.Float(
            "dash.distance",
            3.5f,
            "How far the dash goes, m"
        );

        public static readonly Flag<int> DashFrames = Flag.Int(
            "dash.frames",
            10,
            "How long the dash takes, frames at 60 Hz"
        );

        public static readonly Flag<float> DashCooldown = Flag.Float(
            "dash.cooldown",
            0.35f,
            "Seconds after a dash ends before the next"
        );

        public static readonly Flag<float> DashClipSpeed = Flag.Float(
            "dash.clip-speed",
            2.2f,
            "Playback speed of the Dodge clips"
        );

        public static readonly Flag<float> Distance = Flag.Float(
            "camera.distance",
            14f,
            "The camera's distance from the hero, m: closer for focused fights, pulled back for the horde"
        );

        public static readonly Flag<float> Lean = Flag.Float(
            "camera.lean",
            1.5f,
            "How far the camera leans toward the cursor at most, m"
        );

        public static readonly Flag<bool> Demo = Flag.Bool(
            "hero.demo",
            false,
            "Scripted input instead of the keyboard and mouse: runs a loop and dashes (headless checks)"
        );

        public static readonly Flag<int> Props = Flag.Int(
            "arena.props",
            40,
            "Crystals the native hash scatters over the arena"
        );
    }
}
