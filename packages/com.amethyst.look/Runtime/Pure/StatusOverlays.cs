using System;
using System.Collections.Generic;

namespace Amethyst.Look
{
    /// <summary>
    /// The white overlays a suite's status draws on a crowd figure (docs/game-design.md, Look &gt;
    /// Color roles: statuses are the hero's, so white). Flags: several can show at once.
    /// </summary>
    [Flags]
    public enum Overlay : uint
    {
        None = 0,

        /// <summary>White sparks running over its lines.</summary>
        Sparks = 1,

        /// <summary>Frost facets: some of its faces filled pale white.</summary>
        Frost = 2,

        /// <summary>A white rim round its silhouette.</summary>
        Ring = 4,
    }

    /// <summary>
    /// Which overlays each crowd figure wears, and until when: a presenter attaches one for a
    /// while, may remove it early, and each frame writes <see cref="At"/> into the figure's
    /// instance. Figures are the presenter's own ids; times are its clock in seconds.
    /// </summary>
    public sealed class StatusOverlays
    {
        struct Worn
        {
            public Overlay Kind;
            public double Since,
                Until;
        }

        readonly Dictionary<int, List<Worn>> worn = new Dictionary<int, List<Worn>>();

        /// <summary>Puts <paramref name="kind"/> on the figure for <paramref name="seconds"/> from
        /// <paramref name="now"/>; attaching one it wears already extends it.</summary>
        public void Attach(int figure, Overlay kind, double now, double seconds)
        {
            if (!worn.TryGetValue(figure, out var list))
                worn[figure] = list = new List<Worn>(2);
            list.RemoveAll(w => w.Kind == kind);
            list.Add(
                new Worn
                {
                    Kind = kind,
                    Since = now,
                    Until = now + seconds,
                }
            );
        }

        /// <summary>Takes <paramref name="kind"/> off the figure now.</summary>
        public void Remove(int figure, Overlay kind)
        {
            if (worn.TryGetValue(figure, out var list))
                list.RemoveAll(w => (w.Kind & kind) != 0);
        }

        /// <summary>Forgets the figure (its death, its slot's reuse).</summary>
        public void Clear(int figure) => worn.Remove(figure);

        /// <summary>
        /// What the figure wears at <paramref name="now"/> and how long since the newest of it was
        /// attached (the shader's fade-in and flicker clock); expired ones are dropped.
        /// </summary>
        public Overlay At(int figure, double now, out float age)
        {
            age = 0;
            if (!worn.TryGetValue(figure, out var list))
                return Overlay.None;
            list.RemoveAll(w => w.Until <= now);
            var kinds = Overlay.None;
            double newest = double.MinValue;
            foreach (var w in list)
            {
                kinds |= w.Kind;
                newest = Math.Max(newest, w.Since);
            }
            if (kinds != Overlay.None)
                age = (float)(now - newest);
            return kinds;
        }
    }
}
