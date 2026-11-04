using System.Collections.Generic;

namespace Amethyst.Action
{
    /// <summary>
    /// Feel §6.4's aggression tokens: an enemy winds up an attack only while it holds one, and at
    /// most <see cref="Capacity"/> are out at once, so a crowd keeps only a few threats readable.
    /// </summary>
    public sealed class AggressionTokens
    {
        readonly HashSet<int> holders = new HashSet<int>();

        public AggressionTokens(int capacity) => Capacity = capacity;

        public int Capacity { get; set; }

        public int Held => holders.Count;

        /// <summary>A token for <paramref name="id"/> if one is free; true too if it holds one.</summary>
        public bool TryTake(int id)
        {
            if (holders.Contains(id))
                return true;
            if (holders.Count >= Capacity)
                return false;
            holders.Add(id);
            return true;
        }

        /// <summary>Gives its token back (its attack ended, it was staggered or it died).</summary>
        public void Release(int id) => holders.Remove(id);
    }
}
