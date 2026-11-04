using UnityEngine;

namespace Amethyst.Look
{
    /// <summary>
    /// The neon edge look's materials (docs/game-design.md, Look): Playlist Survivors' edge
    /// preset as its trailer drew it. Crowd figures are near-black glass whose only light is their
    /// type's palette hue, levelled to one luminance, in a fresnel rim, in lines on the mesh's
    /// feature edges and in a small inner core; the hero is the same grammar in white, a rung
    /// brighter, so no monster outshines it. The caller owns each material.
    /// </summary>
    public static class EdgeLook
    {
        /// <summary>The luminance every type's hue is drawn at (linear), so the bloom treats hues alike.</summary>
        public const float HueLuminance = 0.4f;

        // The trailer's gains (Playlist Survivors' look package at git 138684f2).
        const float Rim = 2f,
            RimPower = 4f,
            Edge = 2.6f,
            EdgePx = 1.25f,
            CoreGlow = 0.1f,
            CoreFlare = 0.35f,
            CoreSize = 0.18f;

        /// <summary>
        /// A hue scaled to <see cref="HueLuminance"/>: the enemy hues span about 0.25 (blue,
        /// magenta) to 0.6 (cyan, amber) in linear luminance, and drawn as they are an amber
        /// elite blooms to yellow-white while a blue demon stays dim.
        /// </summary>
        public static Color Level(Color linear)
        {
            float l = linear.r * 0.2126f + linear.g * 0.7152f + linear.b * 0.0722f;
            var c = linear * (HueLuminance / Mathf.Max(l, 0.05f));
            c.a = 1;
            return c;
        }

        /// <summary>A palette colour as a linear Color.</summary>
        public static Color Linear(Rgb c) => ((Color)new Color32(c.R, c.G, c.B, 255)).linear;

        /// <summary>A type's hue as its figures draw it: the palette's, levelled.</summary>
        public static Color Hue(MonsterType type, Palette palette) =>
            Level(Linear(palette.ForType(type.Id)));

        /// <summary>A new material for a crowd type's figures.</summary>
        public static Material Body(MonsterType type, Palette palette)
        {
            var m = new Material(MonsterLibrary.Load<Shader>("AmethystLook/MonsterEdge"))
            {
                name = type.Id + "_edge",
                enableInstancing = false,
            };
            m.SetColor("_Tint", Hue(type, palette));
            m.SetColor("_Core", Linear(palette.Glass));
            m.SetColor("_Flash", Color.white);
            m.SetColor("_Status", Linear(palette.Hero));
            m.SetFloat("_Rim", Rim);
            m.SetFloat("_RimPower", RimPower);
            m.SetFloat("_Edge", Edge);
            m.SetFloat("_EdgePx", EdgePx);
            m.SetFloat("_CoreGlow", CoreGlow);
            m.SetFloat("_CoreFlare", CoreFlare);
            m.SetFloat("_CoreSize", CoreSize);
            return m;
        }

        /// <summary>A new material for a crowd type's kill shatter (<see cref="MonsterShatter"/>), at
        /// the trailer's shard gains.</summary>
        public static Material Shards(MonsterType type, Palette palette)
        {
            var m = new Material(MonsterLibrary.Load<Shader>("AmethystLook/MonsterEdgeShard"))
            {
                name = type.Id + "_shards",
                enableInstancing = false,
            };
            m.SetColor("_Tint", Hue(type, palette));
            m.SetColor("_Core", Linear(palette.Glass));
            m.SetColor("_Flash", Color.white);
            m.SetFloat("_Rim", Rim);
            m.SetFloat("_RimPower", RimPower);
            m.SetFloat("_Edge", 1.4f);
            m.SetFloat("_EdgePx", EdgePx);
            m.SetFloat("_Fresh", 0.6f);
            return m;
        }

        /// <summary>A new material for the hero (<see cref="HeroLook"/>), in the palette's white.</summary>
        public static Material Hero(Palette palette)
        {
            var m = new Material(MonsterLibrary.Load<Shader>("AmethystLook/HeroEdge"))
            {
                name = "hero_edge",
                enableInstancing = false,
            };
            m.SetColor("_Body", Linear(palette.Hero));
            // The trailer tuned the hero under a scene exposure of 0.8 stops and drew it at that level
            // whatever the grade's own exposure, so it sits on the hero's rung.
            m.SetFloat("_Level", Mathf.Pow(2f, 0.8f - Grade.Exposure));
            return m;
        }

        internal static void Destroy(Object o)
        {
            if (o == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(o);
            else
                Object.DestroyImmediate(o);
        }
    }
}
