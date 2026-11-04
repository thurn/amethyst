using Amethyst.Flags;

namespace Amethyst.Action
{
    public enum AimMode
    {
        Cursor,
        Movement,
    }

    public enum LungeClip
    {
        Fencing,
        Stab,
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

        public static readonly Flag<float> HurtInvulnerable = Flag.Float(
            "hero.hurt-invulnerable",
            0.8f,
            "Seconds the hero can't be hit again after any hit"
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

        public static readonly Flag<int> CleaveStartup = Flag.Int(
            "cleave.startup",
            3,
            "Frames from the press to a Cleave's contact: each clip starts that far before its contact frame"
        );

        public static readonly Flag<float> CleaveSpeed = Flag.Float(
            "cleave.speed",
            1.3f,
            "Playback speed of the Cleave clips (LightCombo01 A, B, C)"
        );

        public static readonly Flag<int> CleaveRecovery = Flag.Int(
            "cleave.recovery",
            14,
            "Frames a Cleave plays after its contact before it ends; the dash cancels them"
        );

        public static readonly Flag<int> CleaveCadence = Flag.Int(
            "cleave.cadence",
            15,
            "Frames from one Cleave's start to the next's at the earliest"
        );

        public static readonly Flag<LungeClip> LungeClipFlag = Flag.Enum(
            "lunge.clip",
            LungeClip.Fencing,
            "The Lunge's clip: Sword Combat's LightFencing01 or HeavyStab01"
        );

        public static readonly Flag<int> LungeStartup = Flag.Int(
            "lunge.startup",
            5,
            "Frames from the press to the Lunge's contact"
        );

        public static readonly Flag<float> LungeSpeed = Flag.Float(
            "lunge.speed",
            1.3f,
            "Playback speed of the Lunge's clip"
        );

        public static readonly Flag<int> LungeRecovery = Flag.Int(
            "lunge.recovery",
            16,
            "Frames the Lunge plays after its contact"
        );

        public static readonly Flag<int> LungeWindow = Flag.Int(
            "lunge.window",
            9,
            "Frames after a dash ends in which an attack is still the Lunge"
        );

        public static readonly Flag<float> LungeTravel = Flag.Float(
            "lunge.travel",
            1.5f,
            "How far the Lunge carries the hero along the dash line before contact, m"
        );

        public static readonly Flag<int> SlamStartup = Flag.Int(
            "slam.startup",
            12,
            "Frames from the press to the Leap Slam's landing: the leap's airtime"
        );

        public static readonly Flag<float> SlamSpeed = Flag.Float(
            "slam.speed",
            1f,
            "Playback speed of the Leap Slam's clip (LightLeaping01)"
        );

        public static readonly Flag<int> SlamRecovery = Flag.Int(
            "slam.recovery",
            18,
            "Frames the Leap Slam plays after it lands"
        );

        public static readonly Flag<float> SlamRange = Flag.Float(
            "slam.range",
            4f,
            "The Leap Slam's longest leap toward the aim point, m"
        );

        public static readonly Flag<float> SlamCooldown = Flag.Float(
            "slam.cooldown",
            2f,
            "Seconds from one Leap Slam to the next"
        );

        public static readonly Flag<int> BlendIn = Flag.Int(
            "attack.blend-in",
            0,
            "Frames an attack clip fades in over: 0 snaps, so the first frame is the strike"
        );

        public static readonly Flag<int> BlendOut = Flag.Int(
            "attack.blend-out",
            6,
            "Frames an ended attack fades back into the run cycles over"
        );

        public static readonly Flag<bool> RunCancel = Flag.Bool(
            "attack.run-cancel",
            true,
            "Running ends an attack's recovery, as the dash does"
        );

        public static readonly Flag<float> Hitbox = Flag.Float(
            "attack.hitbox",
            1.15f,
            "Attack hitboxes over the size their effects draw (feel §4.2)"
        );

        public static readonly Flag<float> Distance = Flag.Float(
            "camera.distance",
            14f,
            "The camera's distance from the hero, m (a combat mode sets its own)"
        );

        public static readonly Flag<float> Lean = Flag.Float(
            "camera.lean",
            1.5f,
            "How far the camera leans toward the cursor at most, m"
        );

        public static readonly Flag<bool> Demo = Flag.Bool(
            "hero.demo",
            false,
            "Scripted input instead of the keyboard and mouse: every move, then a loop with dashes (headless checks)"
        );

        public static readonly Flag<int> Props = Flag.Int(
            "arena.props",
            40,
            "Crystals the native hash scatters over the arena"
        );
    }
}
