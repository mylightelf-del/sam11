using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Common;
using Sam11.Core.Data;

namespace Sam11.Core.Battle
{
    public enum StructureKind { Center, Street, Wall, Gate }

    /// <summary>전장 위의 거점 구조물 칸 (04 군사·전투 설계서 11장).</summary>
    public sealed class SiegeStructure
    {
        public string Stronghold = "";
        public StructureKind Kind;
        public int Durability;
        public int MaxDurability;

        /// <summary>성벽·성문이 서 있으면 적은 지나갈 수 없다.</summary>
        public bool Blocking => (Kind == StructureKind.Wall || Kind == StructureKind.Gate) && Durability > 0;
        public bool IsInterior => Kind == StructureKind.Center || Kind == StructureKind.Street;
    }

    public sealed class StrongholdOnField
    {
        public string Id = "";
        public string Name = "";
        public string? Owner;
        public HexCoord Center;
    }

    public sealed class CaptureEvent
    {
        public string Stronghold = "";
        public string? From;
        public string To = "";
    }

    /// <summary>수비대 부대의 주장이 없을 때 쓰는 이름 없는 지휘관.</summary>
    public static class Garrison
    {
        public static OfficerDef Anonymous(GameData data)
        {
            var o = new OfficerDef { Id = "_garrison", Name = new LocalizedText { Ko = "수비대" } };
            o.Stats.Ldr = 50; o.Stats.War = 50; o.Stats.Int = 50; o.Stats.Pol = 30; o.Stats.Cha = 30;
            foreach (var f in data.Formations.Keys)
                o.Aptitude[f] = f == "square" || f == "wild_geese" ? AptitudeRank.B : AptitudeRank.C;
            return o;
        }
    }
}
