using System;
using System.IO;
using System.Linq;
using Amethyst.Action;
using UnityEditor;
using UnityEngine;

namespace Amethyst.Editor
{
    /// <summary>
    /// <c>am unity PROJECT Amethyst.Editor.HeroKitTasks.Bake</c>: writes Assets/Resources/HeroKit.asset,
    /// pointing at the knight, its weapons and its clips in Assets/Vendor/ (the project's
    /// vendor.json lists them). Rerun after changing which files the hero uses.
    /// </summary>
    public static class HeroKitTasks
    {
        const string Path = "Assets/Resources/HeroKit.asset";

        public static void Bake()
        {
            var kit = AssetDatabase.LoadAssetAtPath<HeroKit>(Path);
            var created = kit == null;
            if (created)
                kit = ScriptableObject.CreateInstance<HeroKit>();
            kit.Knight = Prefab("Character_Knight_01_Orange");
            kit.Sword = Prefab("SM_Wep_Broadsword_01");
            kit.Shield = Prefab("SM_Wep_Shield_01");
            kit.Idle = Clip("A_Idle_Base_Sword");
            kit.RunF = Clip("A_Run_F_Masc");
            kit.RunB = Clip("A_Run_BckStrafeB_Masc");
            kit.RunL = Clip("A_Run_FwdStrafeL_Masc");
            kit.RunR = Clip("A_Run_FwdStrafeR_Masc");
            kit.DodgeF = Clip("A_Dodge_F_Sword");
            kit.DodgeB = Clip("A_Dodge_B_Sword");
            kit.DodgeL = Clip("A_Dodge_L_Sword");
            kit.DodgeR = Clip("A_Dodge_R_Sword");
            kit.CleaveA = Attack(kit, "A_Attack_LightCombo01A_Sword");
            kit.CleaveB = Attack(kit, "A_Attack_LightCombo01B_Sword");
            kit.CleaveC = Attack(kit, "A_Attack_LightCombo01C_Sword");
            kit.Fencing = Attack(kit, "A_Attack_LightFencing01_Sword");
            kit.Stab = Attack(kit, "A_Attack_HeavyStab01_Sword");
            kit.Leap = Attack(kit, "A_Attack_LightLeaping01_Sword");
            kit.Swings = Enumerable
                .Range(1, 4)
                .Select(i => Audio($"WHOOSH_Air_Blade_RR{i}_mono"))
                .ToArray();
            kit.Finishers = Enumerable
                .Range(1, 3)
                .Select(i => Audio($"WHOOSH_Air_Super_Fast_RR{i}_mono"))
                .ToArray();
            kit.Slam = Audio("SWORD_Swing_Hit_Solid_Metal_Deep_mono");
            if (created)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                AssetDatabase.CreateAsset(kit, Path);
            }
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            Debug.Log("HeroKitTasks.Bake: " + Path);
        }

        static string Find(string name, string type)
        {
            var paths = AssetDatabase
                .FindAssets($"{name} t:{type}", new[] { "Assets/Vendor" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p) == name)
                .ToArray();
            if (paths.Length != 1)
                throw new Exception(
                    $"HeroKitTasks: {paths.Length} files named {name} in Assets/Vendor/; add it with `just vendor add`"
                );
            return paths[0];
        }

        static HeroKit.Attack Attack(HeroKit kit, string name)
        {
            var clip = Clip(name);
            var contact = Contact(kit.Knight, clip);
            Debug.Log(
                $"HeroKitTasks.Bake: {name} is {clip.length:F3} s, contact at {contact:F3} s"
            );
            return new HeroKit.Attack { Clip = clip, Contact = contact };
        }

        // The contact frame: where the sword hand moves fastest, sampled at 60 Hz in the knight's frame.
        static float Contact(GameObject prefab, AnimationClip clip)
        {
            const float Step = 1f / 60f;
            var knight = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var hand = knight
                    .GetComponentsInChildren<Transform>()
                    .First(t => t.name == "Hand_R");
                var best = 0f;
                var at = 0f;
                Vector3? last = null;
                for (int i = 0; i * Step <= clip.length; i++)
                {
                    clip.SampleAnimation(knight, i * Step);
                    var p = knight.transform.InverseTransformPoint(hand.position);
                    if (last != null && (p - last.Value).magnitude > best)
                    {
                        best = (p - last.Value).magnitude;
                        at = i * Step;
                    }
                    last = p;
                }
                return at;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(knight);
            }
        }

        static AudioClip Audio(string name) =>
            AssetDatabase.LoadAssetAtPath<AudioClip>(Find(name, "AudioClip"));

        static GameObject Prefab(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(Find(name, "Prefab"));

        // Sword Combat's files also split each attack into WindUp, Hit and FollowThrough clips.
        static AnimationClip Clip(string name)
        {
            var clips = AssetDatabase
                .LoadAllAssetsAtPath(Find(name, "Model"))
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToArray();
            return clips.FirstOrDefault(c => c.name == name) ?? clips.First();
        }
    }
}
