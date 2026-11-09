using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Battle;
using Sam11.Core.Common;

namespace Sam11.Core.World
{
    /// <summary>세력 하나의 턴을 진행하는 주체 (AI 또는 플레이어 입력).</summary>
    public interface IFactionController
    {
        void TakeTurn(Game game, string faction);
    }

    /// <summary>
    /// 게임 루프: 세계 턴(10일)과 전장 라운드를 하나로 묶는다 (11식 단일 맵).
    /// 한 턴 = 세력마다 차례로 행동 → 라운드 종료 → 날짜 진행(월초 처리).
    /// </summary>
    public sealed class Game
    {
        public WorldState World { get; }
        public Battle.Battle Field { get; }
        public Dictionary<string, IFactionController> Controllers { get; } = new Dictionary<string, IFactionController>();
        public Dictionary<string, Unit> Garrisons { get; } = new Dictionary<string, Unit>();
        public int TurnCount { get; private set; }

        /// <summary>이벤트로 새 세력이 생겼을 때 그 세력의 컨트롤러를 만드는 방법 (예: AI). 없으면 아무도 조종하지 않는다.</summary>
        public Func<string, IFactionController>? ControllerFactory { get; set; }

        public void OnFactionCreated(string faction)
        {
            if (ControllerFactory != null && !Controllers.ContainsKey(faction))
                Controllers[faction] = ControllerFactory(faction);
        }
        public string? Winner { get; private set; }

        /// <summary>적이 이 거리 안에 오면 수비대를 성에 배치한다.</summary>
        public int GarrisonAlertRadius = 5;
        /// <summary>적이 이 거리 밖으로 물러나면 수비대를 해산한다.</summary>
        public int GarrisonStandDownRadius = 7;

        private int _capturesDone, _fatesDone;

        public Game(WorldState world, ulong seed)
        {
            World = world;
            Field = SiegeSetup.CreateBattle(world, seed);
            Field.Hostile = (a, b) => !world.AtPeace(a, b);
            Field.OfficerResolver = id => Treasures.Effective(world, id);
        }

        public IEnumerable<string> AliveFactions => World.Factions.Values.Where(f => f.Alive).Select(f => f.Id);

        public bool IsOver => Winner != null;

        /// <summary>한 턴(10일)을 진행한다.</summary>
        public void PlayTurn()
        {
            if (IsOver) return;
            var order = AliveFactions.OrderBy(f => f).ToList();
            if (order.Count > 0)
            {
                int shift = TurnCount % order.Count; // 매 턴 순서를 돌려 선공 이점을 고르게
                order = order.Skip(shift).Concat(order.Take(shift)).ToList();
            }
            Missions.Tick(World);
            foreach (var f in order)
            {
                if (!World.Factions[f].Alive) continue;
                Missions.TriggerInsiders(this);
                SyncGarrisons();
                Field.BeginTurn(f);
                Resolve();
                if (Controllers.TryGetValue(f, out var c))
                    c.TakeTurn(this, f);
                Resolve();
            }
            // 무주지 수비대: 붙어 있는 적을 공격만 한다
            SyncGarrisons();
            Field.BeginTurn(SiegeSetup.Neutral);
            foreach (var g in Garrisons.Values.Where(g => g.Faction == SiegeSetup.Neutral && g.Status == UnitStatus.Ready).ToList())
                GarrisonStrike(g);
            Resolve();

            BlockadePressure();
            Resolve();
            Field.EndRound();
            foreach (var kv in Garrisons) World.Cities[kv.Key].FieldGarrison = kv.Value.IsAlive ? kv.Value.Troops : 0;
            bool monthStart;
            Domestic.AdvanceTurn(World);
            monthStart = World.Date.IsMonthStart;
            if (monthStart)
            {
                MonthlyUpkeep();
                Missions.Defections(World);
                Diplomacy.Monthly(this);
                EventSystem.RunMonthly(this);
                Resolve();
            }
            TurnCount++;
            CheckVictory();
        }

        /// <summary>포위된 성의 수비대는 매 턴 사기가 떨어진다 (02 맵·도시 설계서 4.2의 포위 판정).</summary>
        public int BlockadeMoraleLoss = 5;

        private void BlockadePressure()
        {
            foreach (var kv in Garrisons)
                if (kv.Value.IsAlive && Field.IsBlockaded(kv.Key))
                {
                    Field.LoseMorale(kv.Value, BlockadeMoraleLoss);
                    World.Log.Add($"[{World.Date}] {World.Cities[kv.Key]} 포위: 수비대 사기 {kv.Value.Morale}");
                }
        }

        /// <summary>수비대가 사거리 안의 적 중 가장 약한 부대를 친다.</summary>
        public void GarrisonStrike(Unit g)
        {
            // 앞선 교전의 반격으로 혼란에 빠졌거나 이미 행동했을 수 있다
            if (g.Status != UnitStatus.Ready || g.HasActed) return;
            var foe = Field.Alive.Where(e => Field.Hostile(e.Faction, g.Faction) && Field.InRange(g, e))
                                 .OrderBy(e => e.Troops).FirstOrDefault();
            if (foe != null) Field.Attack(g, foe);
        }

        // ------------------------------------------------------------------ 수비대

        /// <summary>적이 다가온 거점은 수비대를 정청에 세우고, 적이 물러나면 다시 성 안으로 들인다.</summary>
        public void SyncGarrisons()
        {
            var hostiles = Field.Alive.Where(u => !u.IsGarrison).ToList();
            foreach (var c in World.Cities.Values)
            {
                string owner = c.Owner ?? SiegeSetup.Neutral;
                var center = c.Placement.Center;
                int nearest = hostiles.Where(u => Field.Hostile(u.Faction, owner))
                                      .Select(u => u.Position.DistanceTo(center)).DefaultIfEmpty(int.MaxValue).Min();
                Garrisons.TryGetValue(c.Id, out var g);
                if (g != null && !g.IsAlive)
                {
                    Garrisons.Remove(c.Id);
                    c.FieldGarrison = 0;
                    g = null;
                }
                if (g == null && nearest <= GarrisonAlertRadius && c.Troops > 0 && Field.UnitAt(center) == null)
                {
                    var officers = World.OfficersAt(c.Id).Where(o => o.Faction == c.Owner && o.InjuredTurns == 0)
                                        .OrderByDescending(o => o.Def.Stats.Ldr).Take(3).Select(o => o.Id).ToList();
                    if (c.Owner == null) officers.Clear();
                    // 성 위치가 맞지 않게 점거된 경우(아군 부대가 정청에 있음)는 그 부대가 지킨다
                    var unit = Field.DeployGarrison(c.Id, officers, c.Troops, c.Equipment, 50 + c.Training / 2);
                    unit.IsGarrison = true;
                    unit.Home = c.Id;
                    c.FieldGarrison = c.Troops;
                    c.Troops = 0;
                    Garrisons[c.Id] = unit;
                }
                else if (g != null && nearest > GarrisonStandDownRadius)
                {
                    c.Troops += g.Troops;
                    c.FieldGarrison = 0;
                    g.Status = UnitStatus.Withdrawn;
                    Garrisons.Remove(c.Id);
                }
            }
        }

        // ------------------------------------------------------------------ 전투 결과 반영

        /// <summary>새로 생긴 함락·무장 운명을 세계 상태에 반영하고 세력의 존망을 확인한다.</summary>
        public void Resolve()
        {
            while (_capturesDone < Field.Captures.Count)
            {
                var cap = Field.Captures[_capturesDone++];
                if (Garrisons.TryGetValue(cap.Stronghold, out var g))
                {
                    Garrisons.Remove(cap.Stronghold);
                    if (g.IsAlive) g.Status = UnitStatus.Withdrawn;
                }
                if (World.Cities[cap.Stronghold].Owner is string loser && World.Factions.ContainsKey(cap.To))
                    World.ChangeFriendship(loser, cap.To, World.Data.Rules.Diplomacy.CaptureFriendship);
                SiegeSetup.ApplyCapture(World, cap);
                foreach (var id in cap.Officers)
                    if (World.Officers.TryGetValue(id, out var o)) o.Merit += World.Data.Rules.Merit.Capture;
            }
            SiegeSetup.SyncStructures(World, Field);

            while (_fatesDone < Field.Fates.Count)
                ApplyFate(Field.Fates[_fatesDone++]);

            foreach (var f in World.Factions.Values.Where(f => f.Alive).ToList())
                CheckFaction(f);
        }

        private void ApplyFate(OfficerFate fate)
        {
            for (int i = 0; i < fate.ByOfficers.Count; i++)
                if (World.Officers.TryGetValue(fate.ByOfficers[i], out var hero))
                    hero.Merit += i == 0 ? World.Data.Rules.Merit.DestroyUnitLeader : World.Data.Rules.Merit.DestroyUnitMember;
            if (!World.Officers.TryGetValue(fate.OfficerId, out var o)) return;
            switch (fate.Outcome)
            {
                case DefeatOutcome.Death:
                    Treasures.Seize(World, o.Id, fate.By != null && World.Factions.ContainsKey(fate.By) ? fate.By : o.Faction);
                    o.Status = OfficerStatus.Dead;
                    o.Location = "";
                    World.Log.Add($"[{World.Date}] {Korean.IGa(o.ToString())} 전사했다");
                    return;
                case DefeatOutcome.Capture when fate.By != null && World.Factions.ContainsKey(fate.By):
                    Treasures.Seize(World, o.Id, fate.By);
                    o.PreviousFaction = o.Faction;
                    if (o.Faction != null) World.ChangeFriendship(o.Faction, fate.By, -5);
                    o.Status = OfficerStatus.Captive;
                    o.Faction = fate.By;
                    o.CaptiveMonths = 0;
                    o.Location = NearestCityOf(fate.By, fate.Position) ?? o.Location;
                    World.Log.Add($"[{World.Date}] {Korean.IGa(o.ToString())} {World.NameOf(fate.By)} 세력에 사로잡혔다");
                    return;
                case DefeatOutcome.Injury:
                    o.InjuredTurns = Math.Max(o.InjuredTurns, 6);
                    break;
                case DefeatOutcome.SeriousInjury:
                    o.InjuredTurns = Math.Max(o.InjuredTurns, 18);
                    break;
            }
            // 귀환·부상: 수비대 무장은 성에 그대로, 야전 무장은 출진 거점이나 가장 가까운 아군 거점으로
            if (fate.Garrison && o.Location != "" && World.Cities[o.Location].Owner == o.Faction) return;
            string? home = fate.Home != null && World.Cities[fate.Home].Owner == o.Faction ? fate.Home : NearestCityOf(o.Faction, fate.Position);
            if (home != null) o.Location = home;
            else
            {
                o.Status = OfficerStatus.Free;
                o.Faction = null;
                o.Location = NearestCityOf(null, fate.Position, anyOwner: true) ?? o.Location;
            }
        }

        public string? NearestCityOf(string? faction, HexCoord from, bool anyOwner = false) =>
            World.Cities.Values.Where(c => anyOwner || (faction != null && c.Owner == faction))
                 .OrderBy(c => c.Placement.Center.DistanceTo(from)).ThenBy(c => c.Id).Select(c => c.Id).FirstOrDefault();

        /// <summary>
        /// 거점을 싸움 없이 넘긴다 (항복·양도 이벤트). 병력과 물자는 그대로 남는다.
        /// withOfficers면 그 거점의 옛 세력 무장도 함께 귀순하고, 아니면 이웃한 옛 세력 거점으로 물러난다.
        /// </summary>
        public void TransferCity(string stronghold, string to, bool withOfficers)
        {
            var c = World.Cities[stronghold];
            string? from = c.Owner;
            if (from == to || !World.Factions.TryGetValue(to, out var target) || !target.Alive) return;
            if (Garrisons.TryGetValue(c.Id, out var g))
            {
                if (g.IsAlive) { c.Troops += g.Troops; g.Status = UnitStatus.Withdrawn; }
                Garrisons.Remove(c.Id);
                c.FieldGarrison = 0;
            }
            c.Owner = to;
            c.Governor = null;
            Field.Strongholds[c.Id].Owner = to;
            if (from != null)
            {
                foreach (var o in World.OfficersAt(c.Id).Where(o => o.Faction == from).ToList())
                {
                    if (withOfficers && World.Factions.TryGetValue(from, out var ff) && ff.Ruler != o.Id)
                    {
                        o.Faction = to;
                        o.Loyalty = Math.Max(o.Loyalty, 70);
                        continue;
                    }
                    var retreat = c.Def.Links.Select(id => World.Cities[id]).FirstOrDefault(x => x.Owner == from)
                                  ?? World.Cities.Values.Where(x => x.Owner == from).OrderBy(x => x.Id).FirstOrDefault();
                    if (retreat != null) o.Location = retreat.Id;
                    else
                    {
                        // 갈 곳이 없으면 (마지막 거점을 바친 군주 포함) 새 주인의 신하가 된다
                        o.Faction = to;
                        o.Loyalty = Math.Max(o.Loyalty, 60);
                    }
                }
                if (World.Factions.TryGetValue(from, out var old) && old.Capital == c.Id)
                    old.Capital = World.Cities.Values.Where(x => x.Owner == from).OrderByDescending(x => x.Def.IsCity).ThenBy(x => x.Id)
                                       .Select(x => x.Id).FirstOrDefault() ?? old.Capital;
            }
            World.Log.Add($"[{World.Date}]   ★ {Korean.IGa(c.ToString())} {World.NameOf(to)} 세력에게 넘어갔다" + (from == null ? "" : $" ({World.NameOf(from)} 세력에게서)"));
            if (from != null && World.Factions.TryGetValue(from, out var lost) && lost.Alive) CheckFaction(lost);
        }

        // ------------------------------------------------------------------ 세력 존망

        private void CheckFaction(FactionState f)
        {
            var ruler = World.Officers[f.Ruler];
            if (ruler.Status != OfficerStatus.Active || ruler.Faction != f.Id)
                Succession(f);
            if (!f.Alive) return;
            bool hasLand = World.Cities.Values.Any(c => c.Owner == f.Id);
            bool hasArmy = Field.Alive.Any(u => u.Faction == f.Id);
            if (!hasLand && !hasArmy) Destroy(f, "거점과 군대를 모두 잃었다");
        }

        /// <summary>후계: 아들 → 공적·매력·통솔 순. 아무도 없으면 멸망.</summary>
        private void Succession(FactionState f)
        {
            var old = World.Officers[f.Ruler];
            var heir = World.Officers.Values
                .Where(o => o.Status == OfficerStatus.Active && o.Faction == f.Id && o.Id != old.Id)
                .OrderByDescending(o => o.Def.Kin?.Father == old.Id)
                .ThenByDescending(o => o.Def.Stats.Cha + o.Def.Stats.Ldr + o.Merit / 50)
                .ThenBy(o => o.Id)
                .FirstOrDefault();
            if (heir == null)
            {
                Destroy(f, $"군주 {Korean.IGa(old.ToString())} 사라지고 이을 사람이 없다");
                return;
            }
            f.Ruler = heir.Id;
            heir.Loyalty = 100;
            if (World.Cities[f.Capital].Owner != f.Id)
                f.Capital = World.Cities.Values.Where(c => c.Owner == f.Id).Select(c => c.Id).FirstOrDefault() ?? f.Capital;
            World.Log.Add($"[{World.Date}] {Korean.IGa(heir.ToString())} {old}의 뒤를 이어 군주가 되었다");
        }

        private void Destroy(FactionState f, string reason)
        {
            f.Alive = false;
            foreach (var c in World.Cities.Values.Where(c => c.Owner == f.Id))
            {
                c.Owner = null;
                c.Governor = null;
                Field.Strongholds[c.Id].Owner = SiegeSetup.Neutral;
            }
            foreach (var u in Field.Alive.Where(u => u.Faction == f.Id).ToList())
                u.Status = UnitStatus.Withdrawn;
            foreach (var o in World.Officers.Values.Where(o => o.Faction == f.Id && (o.Status == OfficerStatus.Active || o.Status == OfficerStatus.Captive)))
            {
                o.Status = OfficerStatus.Free;
                o.Faction = null;
                if (o.Location == "") o.Location = f.Capital;
            }
            World.Log.Add($"[{World.Date}] ★ {World.NameOf(f.Id)} 세력 멸망: {reason}");
        }

        private void MonthlyUpkeep()
        {
            foreach (var f in World.Factions.Values.Where(f => f.Alive))
                Promotion.UpdateTitle(World, f);
            foreach (var o in World.Officers.Values.Where(o => o.Status == OfficerStatus.Captive))
            {
                if (++o.CaptiveMonths < 6) continue;
                o.Status = OfficerStatus.Free;
                o.Faction = null;
                o.CaptiveMonths = 0;
                World.Log.Add($"[{World.Date}] {Korean.IGa(o.ToString())} 포로에서 풀려나 재야가 되었다");
            }
        }

        private void CheckVictory()
        {
            var alive = AliveFactions.ToList();
            var cityOwners = World.Cities.Values.Where(c => c.Def.IsCity).Select(c => c.Owner).Distinct().ToList();
            if (alive.Count == 1) Winner = alive[0];
            else if (cityOwners.Count == 1 && cityOwners[0] != null) Winner = cityOwners[0];
            if (Winner != null) World.Log.Add($"[{World.Date}] ★★ {World.NameOf(Winner)} 세력이 천하를 통일했다");
        }
    }
}
