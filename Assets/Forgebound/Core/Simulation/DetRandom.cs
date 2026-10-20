using System;

namespace Forgebound.Core.Simulation
{
    /// <summary>
    /// Deterministic, platform-independent PRNG (xoshiro128**). Integer-only so results are
    /// identical on Windows/Android/iOS and in the headless balance simulator.
    /// Each subsystem owns its own stream (map, shop, combat, rewards, events) so that
    /// adding a roll in one system never shifts the sequence of another.
    /// </summary>
    public sealed class DetRandom
    {
        private uint _s0, _s1, _s2, _s3;

        public DetRandom(ulong seed)
        {
            // SplitMix64 expansion of the seed into 128 bits of state.
            ulong z = seed;
            _s0 = (uint)SplitMix(ref z);
            _s1 = (uint)SplitMix(ref z);
            _s2 = (uint)SplitMix(ref z);
            _s3 = (uint)SplitMix(ref z);
            if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
        }

        private DetRandom() { }

        public static DetRandom FromState(uint[] state)
        {
            if (state == null || state.Length != 4) throw new ArgumentException("RNG state must have 4 words");
            var r = new DetRandom { _s0 = state[0], _s1 = state[1], _s2 = state[2], _s3 = state[3] };
            if ((r._s0 | r._s1 | r._s2 | r._s3) == 0) r._s0 = 1;
            return r;
        }

        public uint[] GetState() { return new[] { _s0, _s1, _s2, _s3 }; }

        private static ulong SplitMix(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static uint Rotl(uint x, int k) { return (x << k) | (x >> (32 - k)); }

        public uint NextUInt()
        {
            uint result = Rotl(_s1 * 5, 7) * 9;
            uint t = _s1 << 9;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 11);
            return result;
        }

        /// <summary>Uniform int in [0, maxExclusive). Unbiased (rejection sampling).</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            uint bound = (uint)maxExclusive;
            uint threshold = (uint)(-(int)bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return (int)(r % bound);
            }
        }

        public int Range(int minInclusive, int maxInclusive)
        {
            if (maxInclusive <= minInclusive) return minInclusive;
            return minInclusive + Next(maxInclusive - minInclusive + 1);
        }

        /// <summary>Roll a chance expressed in permille (0..1000).</summary>
        public bool Chance(int permille)
        {
            if (permille <= 0) return false;
            if (permille >= 1000) return true;
            return Next(1000) < permille;
        }

        /// <summary>Derive an independent child stream (stable for a given label).</summary>
        public static ulong DeriveSeed(ulong seed, string label)
        {
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < label.Length; i++)
            {
                h ^= label[i];
                h *= 1099511628211UL;
            }
            ulong z = seed ^ h;
            return SplitMix(ref z);
        }

        public int WeightedIndex(int[] weights)
        {
            int total = 0;
            for (int i = 0; i < weights.Length; i++) total += Math.Max(0, weights[i]);
            if (total <= 0) return -1;
            int roll = Next(total);
            for (int i = 0; i < weights.Length; i++)
            {
                int w = Math.Max(0, weights[i]);
                if (roll < w) return i;
                roll -= w;
            }
            return weights.Length - 1;
        }

        public void Shuffle<T>(System.Collections.Generic.IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }

    /// <summary>Integer helpers. All gameplay math uses ints: permille multipliers, milli-units, ticks.</summary>
    public static class FMath
    {
        public const int TicksPerSecond = 30;
        public const int Unit = 1000; // 1 world unit = 1000 milli-units

        public static int Permille(int value, int permille)
        {
            return (int)(((long)value * permille) / 1000);
        }

        public static int PermilleRound(int value, int permille)
        {
            long v = (long)value * permille;
            return (int)(v >= 0 ? (v + 500) / 1000 : (v - 500) / 1000);
        }

        public static int Clamp(int v, int min, int max) { return v < min ? min : (v > max ? max : v); }
        public static int Abs(int v) { return v < 0 ? -v : v; }
        public static int Sign(int v) { return v > 0 ? 1 : (v < 0 ? -1 : 0); }
        public static int Seconds(float s) { return (int)(s * TicksPerSecond + 0.5f); }
    }
}
