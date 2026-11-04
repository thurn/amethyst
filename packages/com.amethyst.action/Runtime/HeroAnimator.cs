using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Amethyst.Action
{
    /// <summary>
    /// Dresses and animates the knight from a <see cref="HeroKit"/> with a playable mixer, no
    /// animator controller: idle and four run cycles blended by the run's direction relative to
    /// facing, and the four Dodge clips over them during a dash.
    /// </summary>
    sealed class HeroAnimator : IDisposable
    {
        const int IdleSlot = 0,
            RunSlot = 1,
            DodgeSlot = 5,
            Count = 9;

        // The in-place run cycles' ground speed at speed 1, m/s, so the feet keep up.
        const float RunClipSpeed = 4.2f;
        const float Blend = 14f;
        const float DodgeOut = 0.12f;

        readonly PlayableGraph graph;
        readonly AnimationMixerPlayable mixer;
        readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[Count];
        readonly float[] weights = new float[Count];
        int dodge = -1;
        float dodgeWeight;

        HeroAnimator(Animator animator, HeroKit kit)
        {
            graph = PlayableGraph.Create("Hero");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, Count);
            var sources = new[]
            {
                kit.Idle,
                kit.RunF,
                kit.RunB,
                kit.RunL,
                kit.RunR,
                kit.DodgeF,
                kit.DodgeB,
                kit.DodgeL,
                kit.DodgeR,
            };
            for (int i = 0; i < Count; i++)
            {
                clips[i] = AnimationClipPlayable.Create(graph, sources[i]);
                clips[i].SetApplyFootIK(false);
                graph.Connect(clips[i], 0, mixer, i);
            }
            weights[IdleSlot] = 1f;
            Apply();
            AnimationPlayableOutput.Create(graph, "Hero", animator).SetSourcePlayable(mixer);
            graph.Play();
        }

        /// <summary>Puts the knight, its sword and shield under <paramref name="root"/>.</summary>
        public static HeroAnimator Dress(Transform root, HeroKit kit)
        {
            var knight = UnityEngine.Object.Instantiate(kit.Knight, root, false);
            knight.name = "Knight";
            Hold(kit.Sword, Find(knight.transform, "Hand_R"));
            Hold(kit.Shield, Find(knight.transform, "Hand_L"));
            foreach (var renderer in knight.GetComponentsInChildren<Renderer>(true))
                Arena.Restyle(renderer);
            var animator = knight.GetComponent<Animator>();
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return new HeroAnimator(animator, kit);
        }

        static void Hold(GameObject prefab, Transform hand)
        {
            if (prefab == null || hand == null)
                return;
            var item = UnityEngine.Object.Instantiate(prefab, hand, false);
            // The rig's bones carry the FBX's 0.01 scale; the weapons are made in metres.
            var scale = hand.lossyScale;
            item.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            foreach (var collider in item.GetComponentsInChildren<Collider>())
                UnityEngine.Object.Destroy(collider);
        }

        static Transform Find(Transform parent, string name)
        {
            foreach (var child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name)
                    return child;
            return null;
        }

        /// <summary>Starts the Dodge clip nearest <paramref name="local"/>, a direction in the
        /// hero's frame.</summary>
        public void Dodge(Vector3 local)
        {
            dodge =
                Mathf.Abs(local.z) >= Mathf.Abs(local.x)
                    ? (local.z >= 0f ? DodgeSlot : DodgeSlot + 1)
                    : (local.x < 0f ? DodgeSlot + 2 : DodgeSlot + 3);
            clips[dodge].SetTime(0);
            clips[dodge].SetSpeed(ActionFlags.DashClipSpeed.Value);
            dodgeWeight = 1f;
        }

        /// <summary>Advances the blend: <paramref name="run"/> is the run velocity in the hero's
        /// frame, m/s.</summary>
        public void Tick(Vector3 run, bool dashing, float dt)
        {
            var speed = run.magnitude;
            var amount = Mathf.Clamp01(speed / 0.5f);
            var dir = speed > 1e-3f ? run / speed : Vector3.forward;
            var f = Mathf.Max(0f, dir.z);
            var b = Mathf.Max(0f, -dir.z);
            var l = Mathf.Max(0f, -dir.x);
            var r = Mathf.Max(0f, dir.x);
            var sum = f + b + l + r;
            var k = 1f - Mathf.Exp(-Blend * dt);
            Approach(IdleSlot, 1f - amount, k);
            Approach(RunSlot, amount * f / sum, k);
            Approach(RunSlot + 1, amount * b / sum, k);
            Approach(RunSlot + 2, amount * l / sum, k);
            Approach(RunSlot + 3, amount * r / sum, k);
            var stride = Mathf.Max(speed, 0.5f) / RunClipSpeed;
            for (int i = RunSlot; i < DodgeSlot; i++)
                clips[i].SetSpeed(stride);
            if (!dashing)
                dodgeWeight = Mathf.MoveTowards(dodgeWeight, 0f, dt / DodgeOut);
            Apply();
        }

        void Approach(int slot, float target, float k) =>
            weights[slot] = Mathf.Lerp(weights[slot], target, k);

        void Apply()
        {
            var total = 0f;
            for (int i = 0; i < DodgeSlot; i++)
                total += weights[i];
            for (int i = 0; i < DodgeSlot; i++)
                mixer.SetInputWeight(i, (1f - dodgeWeight) * weights[i] / Mathf.Max(total, 1e-4f));
            for (int i = DodgeSlot; i < Count; i++)
                mixer.SetInputWeight(i, i == dodge ? dodgeWeight : 0f);
        }

        public void Dispose()
        {
            if (graph.IsValid())
                graph.Destroy();
        }
    }
}
