using System;

namespace Sam11.Core.Common
{
    /// <summary>
    /// 결정론적 난수 (xoshiro128**). 같은 시드와 같은 명령이면 같은 결과가 나와야
    /// 리플레이와 버그 재현이 가능하다. System.Random은 런타임마다 구현이 달라 쓰지 않는다.
    /// </summary>
    public sealed class Rng
    {
        private uint _s0, _s1, _s2, _s3;

        public Rng(ulong seed)
        {
            // splitmix64로 상태를 채운다.
            ulong x = seed;
            _s0 = (uint)Next64(ref x);
            _s1 = (uint)Next64(ref x);
            _s2 = (uint)Next64(ref x);
            _s3 = (uint)Next64(ref x);
            if ((_s0 | _s1 | _s2 | _s3) == 0)
                _s0 = 1;
        }

        private static ulong Next64(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public uint NextUInt()
        {
            uint result = RotL(_s1 * 5, 7) * 9;
            uint t = _s1 << 9;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = RotL(_s3, 11);
            return result;
        }

        private static uint RotL(uint x, int k) => (x << k) | (x >> (32 - k));

        /// <summary>[0, 1) 범위 실수.</summary>
        /// <summary>상태를 바꾸지 않고 내부 상태를 요약한다 (저장 파일 검증용).</summary>
        public string StateDigest => $"{_s0:x8}{_s1:x8}{_s2:x8}{_s3:x8}";

        public double NextDouble() => (NextUInt() >> 8) * (1.0 / (1 << 24));

        public bool Chance(double p) => NextDouble() < p;

        /// <summary>
        /// 실행마다 같은 값을 내는 문자열 해시 (FNV-1a). string.GetHashCode()는 .NET Core에서
        /// 실행할 때마다 달라져 결정론을 깨므로 게임 로직에서는 이것을 쓴다.
        /// </summary>
        public static uint StableHash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s)
            {
                h ^= c;
                h *= 16777619;
            }
            return h;
        }

        /// <summary>[min, max) 범위 실수.</summary>
        public double Range(double min, double max) => min + (max - min) * NextDouble();

        /// <summary>[0, max) 범위 정수.</summary>
        public int NextInt(int max)
        {
            if (max <= 0)
                throw new ArgumentOutOfRangeException(nameof(max));
            return (int)(NextUInt() % (uint)max);
        }
    }
}
