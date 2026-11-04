using UnityEngine;
using UnityEngine.Rendering;

namespace Amethyst.Action
{
    /// <summary>
    /// Materials in one flat unlit colour (Resources/AmethystFlat.shader), alpha blended without
    /// depth writes, for effects and ground shapes. The queue orders them where they overlap: the
    /// hero's effects at <see cref="Effects"/>, enemy telegraphs above them (docs/game-design.md,
    /// Look &gt; Visual priority). From <see cref="Telegraphs"/> on they skip the depth test, so a
    /// crowd's figures never hide a telegraph or a bar.
    /// </summary>
    public static class Flat
    {
        public const int Effects = 3000,
            Markers = 3050,
            Telegraphs = 3100,
            Bars = 3150;

        static Shader shader;

        /// <summary>A new material in <paramref name="color"/> (linear; HDR blooms), the caller's to own.</summary>
        public static Material Make(Color color, int queue)
        {
            if (shader == null)
                shader = Resources.Load<Shader>("AmethystFlat");
            var m = new Material(shader) { renderQueue = queue };
            m.SetColor("_Color", color);
            m.SetFloat(
                "_ZTest",
                (float)(queue >= Telegraphs ? CompareFunction.Always : CompareFunction.LessEqual)
            );
            return m;
        }
    }
}
