using System;
using System.Collections.Generic;

namespace Sam11.Core.Common
{
    /// <summary>헥스 좌표 (axial q, r). 큐브 좌표의 s = -q - r.</summary>
    public readonly struct HexCoord : IEquatable<HexCoord>
    {
        public readonly int Q;
        public readonly int R;

        public HexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        public int S => -Q - R;

        public static readonly HexCoord[] Directions =
        {
            new HexCoord(1, 0), new HexCoord(1, -1), new HexCoord(0, -1),
            new HexCoord(-1, 0), new HexCoord(-1, 1), new HexCoord(0, 1),
        };

        public static HexCoord operator +(HexCoord a, HexCoord b) => new HexCoord(a.Q + b.Q, a.R + b.R);
        public static HexCoord operator -(HexCoord a, HexCoord b) => new HexCoord(a.Q - b.Q, a.R - b.R);
        public static bool operator ==(HexCoord a, HexCoord b) => a.Equals(b);
        public static bool operator !=(HexCoord a, HexCoord b) => !a.Equals(b);

        public int DistanceTo(HexCoord other)
        {
            var d = this - other;
            return (Math.Abs(d.Q) + Math.Abs(d.R) + Math.Abs(d.S)) / 2;
        }

        public IEnumerable<HexCoord> Neighbors()
        {
            foreach (var d in Directions)
                yield return this + d;
        }

        /// <summary>from → this 방향으로 한 칸 더 나아간 칸. 밀어내기에 쓴다.</summary>
        public HexCoord StepAwayFrom(HexCoord from)
        {
            int dist = DistanceTo(from);
            if (dist == 0)
                return this;
            double fq = Q + (double)(Q - from.Q) / dist;
            double fr = R + (double)(R - from.R) / dist;
            return Round(fq, fr);
        }

        /// <summary>this → toward 방향으로 한 칸 다가간 칸. 끌어오기에 쓴다.</summary>
        public HexCoord StepToward(HexCoord toward)
        {
            int dist = DistanceTo(toward);
            if (dist <= 1)
                return this;
            double fq = Q + (double)(toward.Q - Q) / dist;
            double fr = R + (double)(toward.R - R) / dist;
            return Round(fq, fr);
        }

        public static HexCoord Round(double q, double r)
        {
            double s = -q - r;
            double rq = Math.Round(q), rr = Math.Round(r), rs = Math.Round(s);
            double dq = Math.Abs(rq - q), dr = Math.Abs(rr - r), ds = Math.Abs(rs - s);
            if (dq > dr && dq > ds)
                rq = -rr - rs;
            else if (dr > ds)
                rr = -rq - rs;
            return new HexCoord((int)rq, (int)rr);
        }

        public bool Equals(HexCoord other) => Q == other.Q && R == other.R;
        public override bool Equals(object? obj) => obj is HexCoord h && Equals(h);
        public override int GetHashCode() => (Q * 397) ^ R;
        public override string ToString() => $"({Q},{R})";
    }
}
