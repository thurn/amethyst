using System;
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

        public Attack CleaveA;
        public Attack CleaveB;
        public Attack CleaveC;
        public Attack Fencing;
        public Attack Stab;
        public Attack Leap;

        public AudioClip[] Swings;
        public AudioClip[] Finishers;
        public AudioClip Slam;

        /// <summary>An attack clip and its contact: seconds from its start to the peak speed of
        /// the sword hand, which the bake measures.</summary>
        [Serializable]
        public struct Attack
        {
            public AnimationClip Clip;
            public float Contact;
        }

        public static HeroKit Load()
        {
            var kit = Resources.Load<HeroKit>("HeroKit");
            return kit != null && kit.Knight != null ? kit : null;
        }
    }
}
