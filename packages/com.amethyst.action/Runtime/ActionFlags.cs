using Amethyst.Flags;

namespace Amethyst.Action
{
    public enum MoveModel
    {
        Snappy,
        Floaty,
        Momentum,
    }

    public enum HeroLook
    {
        Capsule,
        Crystal,
        Orb,
    }

    public enum CameraView
    {
        High,
        Chase,
        Side,
    }

    public static class ActionFlags
    {
        public static readonly Flag<MoveModel> Move = Flag.Enum(
            "hero.move",
            MoveModel.Snappy,
            "How the hero reaches its top speed"
        );

        public static readonly Flag<float> Speed = Flag.Float(
            "hero.speed",
            6f,
            "The hero's top speed, m/s"
        );

        public static readonly Flag<bool> Dash = Flag.Bool(
            "hero.dash",
            true,
            "Space dashes in the move direction"
        );

        public static readonly Flag<HeroLook> Look = Flag.Enum(
            "hero.look",
            HeroLook.Crystal,
            "The hero's stand-in shape"
        );

        public static readonly Flag<CameraView> View = Flag.Enum(
            "camera.view",
            CameraView.High,
            "Where the camera follows from"
        );

        public static readonly Flag<float> Distance = Flag.Float(
            "camera.distance",
            14f,
            "The camera's distance from the hero, m"
        );

        public static readonly Flag<int> Props = Flag.Int(
            "arena.props",
            40,
            "Crystals the native hash scatters over the arena"
        );
    }
}
