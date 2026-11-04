using UnityEngine;

namespace Amethyst.Action
{
    /// <summary>
    /// The vendor assets the hero is made of (AGENTS.md, Vendor assets), as
    /// <c>Resources/HeroKit</c> in a project that has them; <c>Amethyst.Editor.HeroKitTasks.Bake</c>
    /// fills it from Assets/Vendor/. Without one the hero is a stand-in capsule.
    /// </summary>
    public sealed class HeroKit : ScriptableObject
    {
        public GameObject Knight;
        public GameObject Sword;
        public GameObject Shield;

        public AnimationClip Idle;
        public AnimationClip RunF;
        public AnimationClip RunB;
        public AnimationClip RunL;
        public AnimationClip RunR;
        public AnimationClip DodgeF;
        public AnimationClip DodgeB;
        public AnimationClip DodgeL;
        public AnimationClip DodgeR;

        public static HeroKit Load()
        {
            var kit = Resources.Load<HeroKit>("HeroKit");
            return kit != null && kit.Knight != null ? kit : null;
        }
    }
}
