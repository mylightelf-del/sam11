using System.Collections.Generic;

namespace Sam11.Core.Battle
{
    /// <summary>전투 기록. UI 연출과 리플레이·디버깅에 쓴다.</summary>
    public sealed class BattleLog
    {
        private readonly List<string> _lines = new List<string>();
        public IReadOnlyList<string> Lines => _lines;
        public void Add(string line) => _lines.Add(line);
    }

    public enum DefeatOutcome { Return, Injury, Capture, Death, SeriousInjury }

    public sealed class OfficerFate
    {
        public string OfficerId = "";
        public DefeatOutcome Outcome;
        /// <summary>무장이 속했던 부대의 세력.</summary>
        public string Faction = "";
        /// <summary>궤멸시킨 쪽 세력 (화재·굶주림이면 null). 포로는 이 세력에 잡힌다.</summary>
        public string? By;
        public string? Home;
        public bool Garrison;
        public Common.HexCoord Position;
    }

    /// <summary>공격 한 번의 결과.</summary>
    public sealed class AttackResult
    {
        public bool Success = true;
        public int Damage;
        public int CounterDamage;
        public int ChainLinks;
        public List<(Unit Target, int Damage)> ExtraHits = new List<(Unit, int)>();
        public bool TargetDestroyed;
    }
}
