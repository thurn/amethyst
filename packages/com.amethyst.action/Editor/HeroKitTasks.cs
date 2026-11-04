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

        static GameObject Prefab(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(Find(name, "Prefab"));

        static AnimationClip Clip(string name) =>
            AssetDatabase
                .LoadAllAssetsAtPath(Find(name, "Model"))
                .OfType<AnimationClip>()
                .First(c => !c.name.StartsWith("__preview__"));
    }
}
