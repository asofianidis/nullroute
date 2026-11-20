using System;
using System.Collections.Generic;

namespace Nullroute.Core
{
    /// <summary>Thin wrapper over System.Random with game-friendly helpers.</summary>
    public class Rng
    {
        readonly Random r;

        public Rng(int seed) { r = new Random(seed); }

        /// <summary>Inclusive on both ends.</summary>
        public int Range(int min, int max) { return r.Next(min, max + 1); }

        public double Range(double min, double max) { return min + (max - min) * r.NextDouble(); }

        public double Value() { return r.NextDouble(); }

        public bool Chance(double p) { return r.NextDouble() < p; }

        public T Pick<T>(IList<T> list) { return list[r.Next(list.Count)]; }
    }
}
