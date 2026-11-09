using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Battle;
using Sam11.Core.Common;
using Sam11.Core.Data;
using Sam11.Core.World;

namespace Sam11.Core.AI
{
    public enum CampaignState { Gathering, Marching, Done }

    /// <summary>원정: 한 거점을 목표로 병력을 모아 출진하고 함락까지 이끄는 작전 단위.</summary>
    public sealed class Campaign
    {
        public string Target = "";
        public string Source = "";
        public int Required;
        public double EstimatedDefense;
        public CampaignState State = CampaignState.Gathering;
        public List<Unit> Units = new List<Unit>();
        public int StartTurn;
        /// <summary>목표 대비 출진 병력 비 (축차 투입 측정용).</summary>
        public double LaunchRatio;
        /// <summary>전투 기록에서 이 원정이 시작·끝난 위치 (분석용).</summary>
        public int LogStart, LogEnd;
        public bool Won;
        /// <summary>방어(요격) 원정: Target은 지킬 아군 도시, 그 근처의 적 부대를 친다.</summary>
        public bool Defense;
        /// <summary>동맹이 치고 있는 세력을 함께 치는 공동 작전.</summary>
        public bool Joint;
        /// <summary>부대가 원정에서 빠진 사연 (분석용).</summary>
        public List<string> Exits = new List<string>();
        public List<string> Labels = new List<string>();
        /// <summary>집중 공격할 성벽·성문 칸 (무너지면 다시 고른다).</summary>
        public HexCoord? Focus;
        public override string ToString() => $"{Source}→{Target} ({State}, 필요 {Required})";
    }

    /// <summary>AI 통계 (06 AI 설계서 2장 약점 측정).</summary>
    public sealed class AiStats
    {
        public int CampaignsLaunched;
        public int LaunchedUnderRatio;   // 전력비 기준 미달 출진 (축차 투입)
        public int CampaignsWon;
        /// <summary>출진했지만 함락하지 못하고 끝난 원정.</summary>
        public int CampaignsFailed;
        /// <summary>병력을 모으다 포기한 원정 (출진 전).</summary>
        public int CampaignsAbandoned;
        public int StalledUnitTurns;     // 목표가 있는데 움직이지도 공격하지도 못한 부대·턴 (정체)
        public int UnitTurns;
        public int Retreats;
        /// <summary>공성 중인 원정군에 보낸 증원 부대 수.</summary>
        public int Reinforcements;
        public int Duels;
        public int DiplomacyAttempts;
        public int Stratagems;
        public int Missions;
        public int Intercepts;
        public int Reinforcements2;
        public int Bestowals;
        public int Executions;
        public int Releases;
        public int JointOps;
        public int DiplomacySuccesses;
        public int RetreatsMorale;
        public int RetreatsFood;
        /// <summary>원정 준비가 끝났는데 출진하지 못한 이유별 횟수 (병력 부족, 지휘 한도, 군량 부족 등).</summary>
        public Dictionary<string, int> LaunchBlocked = new Dictionary<string, int>();
        /// <summary>정체 원인별 횟수 (아군에 막힘, 적에 막힘, 이동 불가, 도착 후 할 일 없음, 경로 없음).</summary>
        public Dictionary<string, int> StallReasons = new Dictionary<string, int>();
    }

    /// <summary>
    /// 3계층 AI (06 AI 설계서 3장).
    /// 전략(매월: 목표 선정) → 작전(원정군 편성·진군) → 전술(부대별 행동). 내정 AI가 이를 뒷받침한다.
    /// </summary>
    public sealed partial class AiController : IFactionController, IEventChooser
    {
        public AiConfig Config { get; }
        public List<Campaign> Campaigns { get; } = new List<Campaign>();
        /// <summary>출진했다가 끝난 원정 (분석용).</summary>
        public List<Campaign> History { get; } = new List<Campaign>();
        public AiStats Stats { get; } = new AiStats();
        public Knowledge? Knowledge { get; private set; }

        private readonly Dictionary<Unit, Campaign> _unitCampaign = new Dictionary<Unit, Campaign>();
        /// <summary>
        /// 목표까지의 경로 지도 캐시. 원정 목표는 몇 달씩 같으므로 턴을 넘겨 쓰고,
        /// 함락이나 성벽·성문 붕괴로 길이 바뀌었을 때만 버린다.
        /// </summary>
        private readonly Dictionary<(string, string, string), (Dictionary<HexCoord, int> Field, int Covered)> _pathCache =
            new Dictionary<(string, string, string), (Dictionary<HexCoord, int>, int)>();
        private (int Captures, int Broken) _pathVersion = (-1, -1);
        private int _plannedMonth = -1;
        private int _diplomacyMonth = -1;
        /// <summary>(상대, 종류) → 거절당한 달. 같은 제안을 석 달 동안 다시 하지 않는다.</summary>
        private readonly Dictionary<(string, DiploType), int> _refused = new Dictionary<(string, DiploType), int>();

        public AiController(AiConfig? config = null) => Config = config ?? AiConfig.For(Difficulty.Normal);

        public void TakeTurn(Game g, string faction)
        {
            var w = g.World;
            w.Factions[faction].IncomeBonus = Config.IncomeBonus;
            Knowledge ??= new Knowledge(faction);
            Knowledge.Update(g);
            var version = (g.Field.Captures.Count, g.Field.Structures.Values.Count(x => x.MaxDurability > 0 && x.Durability <= 0));
            if (version != _pathVersion)
            {
                _pathCache.Clear();
                _pathVersion = version;
            }

            int monthKey = w.Date.Year * 12 + w.Date.Month;
            if (_plannedMonth != monthKey || Campaigns.Count == 0)
            {
                _plannedMonth = monthKey;
                Promotion.AppointAll(w, faction);
                if (_diplomacyMonth != monthKey)
                {
                    _diplomacyMonth = monthKey;
                    PlanDiplomacy(g, faction);
                    PlanMissions(g, faction);
                    PlanTreasures(g, faction);
                    PlanCaptives(g, faction);
                }
                Plan(g, faction);
            }
            // 출진을 먼저: 내정이 무장을 다 써 버리면 원정군을 꾸릴 수 없다
            foreach (var camp in Campaigns.Where(c => c.State == CampaignState.Gathering).ToList())
                TryLaunch(g, faction, camp);
            foreach (var camp in Campaigns.Where(c => c.State == CampaignState.Marching).ToList())
                TryReinforce(g, faction, camp);
            PlanDefense(g, faction);
            RunDomestic(g, faction);
            RunField(g, faction);
            Campaigns.RemoveAll(c => c.State == CampaignState.Done);
        }


        /// <summary>
        /// 전략 계략 (매월 한 건): 원정 목표에 내응 → 유언비어, 보이지 않는 목표엔 첩보,
        /// 이웃의 충성 낮은 무장은 매수, 그다음 이간. 지력 70 이상 무장이 놀고 있을 때만.
        /// </summary>
        private void PlanMissions(Game g, string me)
        {
            var w = g.World;
            var r = w.Data.Rules.Stratagem.Strategic;
            var agent = w.Officers.Values.Where(o => o.Faction == me && o.Status == OfficerStatus.Active && !o.Acted && o.Location != ""
                                                     && o.Def.Stats.Int >= 70 && !w.Missions.Any(m => m.Agent == o.Id)
                                                     && w.Factions[me].Ruler != o.Id)
                         .OrderByDescending(o => o.Def.Stats.Int).FirstOrDefault();
            if (agent == null) return;
            int gold = w.Cities.Values.Where(c => c.Owner == me).Select(c => c.Gold).DefaultIfEmpty(0).Max();
            bool Try(string kind, string? city, string? officer = null)
            {
                if (gold < r.Cost[kind] * 2) return false;
                try
                {
                    Sam11.Core.World.Missions.Start(w, agent.Id, kind, city, officer);
                    Stats.Missions++;
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            }
            foreach (var camp in Campaigns)
            {
                var t = w.Cities[camp.Target];
                if (t.Owner == null || w.Insiders.ContainsKey(t.Id)) continue;
                var mole = w.OfficersAt(t.Id).Where(o => o.Faction == t.Owner && w.Factions[t.Owner].Ruler != o.Id)
                            .OrderBy(o => o.Loyalty).FirstOrDefault();
                if (mole != null && mole.Loyalty < 70 && Try("insider", null, mole.Id)) return;
                if (!Knowledge!.Sees(t.Placement.Center) && !w.HasSpyReport(me, t.Id) && Try("spy", t.Id)) return;
                if (t.Morale > 40 && Try("rumor", t.Id)) return;
            }
            var neighbors = w.Factions.Values.Where(f => f.Alive && f.Id != me && !w.AtPeace(me, f.Id) && Diplomacy.Borders(w, me, f.Id))
                             .Select(f => f.Id).ToHashSet();
            var prey = w.Officers.Values.Where(o => o.Status == OfficerStatus.Active && o.Faction != null && neighbors.Contains(o.Faction)
                                                    && w.Factions[o.Faction].Ruler != o.Id && o.Location != "")
                        .OrderBy(o => o.Loyalty).ThenByDescending(o => o.Def.Stats.Ldr + o.Def.Stats.War + o.Def.Stats.Int).ToList();
            var weak = prey.FirstOrDefault(o => o.Loyalty < r.BribeLoyaltyMax - 10);
            if (weak != null && Try("bribe", null, weak.Id)) return;
            var shaky = prey.FirstOrDefault(o => o.Loyalty < 80);
            if (shaky != null && w.Rng.Chance(0.5)) Try("sow_discord", null, shaky.Id);
        }

        // ------------------------------------------------------------------ 공통 판단

        private static RulerPersonality Personality(WorldState w, string faction) =>
            w.Officers[w.Factions[faction].Ruler].Def.RulerPersonality ?? new RulerPersonality();

        /// <summary>도시 근처(10칸)에 보이는 적 야전 병력.</summary>
        public int Threat(Game g, CityState c) =>
            Knowledge!.VisibleEnemies(g).Where(u => !u.IsGarrison && u.Position.DistanceTo(c.Placement.Center) <= 10).Sum(u => u.Troops);

        /// <summary>거점에 남겨야 할 병력: 종류별 기본 + 위협.</summary>
        public int Reserve(Game g, CityState c, string faction)
        {
            int baseline = c.Def.Type switch
            {
                "major_city" => 3000, "city" => 2000, "gate" => 2000, "fortress" => 1500, "port" => 500, _ => 300,
            };
            if (g.World.Factions[faction].Capital == c.Id) baseline += 1000;
            return baseline + (int)(Threat(g, c) * 0.7);
        }

        public int Surplus(Game g, CityState c, string faction) => Math.Max(0, c.Troops - Reserve(g, c, faction));

        private static double WallFactor(string type) => type switch
        {
            "major_city" => 1.1, "city" => 1.05, "gate" => 1.2, "fortress" => 1.1, _ => 1.0,
        };

        private static double Value(WorldState w, CityState t)
        {
            double finish = 0;
            if (t.Owner != null)
            {
                // 약한 세력은 끝내기를 노린다 (빠른 판)
                int theirCities = w.Cities.Values.Count(c => c.Owner == t.Owner && c.Def.IsCity);
                if (theirCities <= 2) finish += 4;
                if (theirCities <= 1 && t.Def.IsCity) finish += 3;
            }
            double v = t.Def.Type switch
            {
                "major_city" => 8, "city" => 5, "gate" => 4, "fortress" => 3, "port" => 2, _ => 1,
            };
            v += t.Population / 200000.0 + finish;
            if (t.Owner == null) v += 2;
            else if (w.Factions.TryGetValue(t.Owner, out var f) && f.Capital == t.Id) v += 2;
            return v;
        }

        // ------------------------------------------------------------------ 전략 계층

        private void Plan(Game g, string faction)
        {
            var w = g.World;
            foreach (var c in Campaigns)
            {
                var t = w.Cities[c.Target];
                bool targetGone = t.Owner == faction || (t.Owner != null && w.AtPeace(t.Owner, faction));
                bool sourceLost = w.Cities[c.Source].Owner != faction;
                bool stale = c.State == CampaignState.Gathering && g.TurnCount - c.StartTurn > 30;
                if (c.State == CampaignState.Gathering && (targetGone || sourceLost || stale))
                {
                    c.State = CampaignState.Done;
                    Stats.CampaignsAbandoned++;
                }
            }
            Campaigns.RemoveAll(c => c.State == CampaignState.Done);

            var own = w.Cities.Values.Where(c => c.Owner == faction).ToList();
            int cityCount = own.Count(c => c.Def.IsCity);
            int max = cityCount >= 20 ? 6 : cityCount >= 15 ? 5 : cityCount >= 10 ? 4 : cityCount >= 6 ? 3 : cityCount >= 3 ? 2 : 1;
            if (Campaigns.Count(c => !c.Defense) >= max) return;

            var p = Personality(w, faction);
            double ratio = Config.ConcentrationRatio * (Config.ConcentrationRatio >= 1 ? 1.15 - 0.03 * p.Aggression : 1);
            // 수확 전(6~8월)에 군량이 넉넉하지 않으면 큰 원정을 미룬다 (호전적인 군주는 개의치 않는다)
            bool preHarvest = w.Date.Month >= 6 && w.Date.Month <= 8 && p.Aggression < 8;

            var options = new List<(double Score, Campaign C, bool Feasible)>();
            foreach (var s in own)
            {
                var connected = Domestic.ConnectedOwn(w, s.Id);
                int available = Surplus(g, s, faction)
                                + connected.Where(id => id != s.Id).Sum(id => Surplus(g, w.Cities[id], faction));
                // 이어진 거점의 무장들이 이끌 수 있는 병력 (원정군 지휘관이 모자라면 못 친다)
                int capacity = w.Officers.Values
                    .Where(o => o.Status == OfficerStatus.Active && o.Faction == faction && connected.Contains(o.Location) && o.InjuredTurns == 0)
                    .Select(o => UnitStats.MaxTroops(o.Def, o.Rank != null ? w.Data.Ranks[o.Rank] : null, w.Data.Rules.Unit))
                    .OrderByDescending(x => x).Skip(1).Take(8).Sum();
                foreach (var tid in s.Def.Links)
                {
                    var t = w.Cities[tid];
                    if (t.Owner == faction || (t.Owner != null && w.AtPeace(t.Owner, faction))) continue;
                    if (Campaigns.Any(c => c.Target == tid)) continue;
                    double d = Knowledge!.EstimateTroops(g, t) * WallFactor(t.Def.Type)
                               + Knowledge.VisibleEnemies(g).Where(u => !u.IsGarrison && u.Position.DistanceTo(t.Placement.Center) <= 6).Sum(u => u.Troops);
                    d = Math.Max(d, 500);
                    int need = (int)Math.Ceiling(d * ratio);
                    double score = Value(w, t) * (0.6 + p.Aggression / 10.0) / (1 + d / 8000.0)
                                   / (1 + s.Placement.Center.DistanceTo(t.Placement.Center) / 40.0);
                    // 공동 작전: 동맹이 지금 치고 있는 세력이면 함께 친다
                    bool joint = t.Owner != null && AllyIsFighting(g, faction, t.Owner);
                    if (joint) score *= 1.5;
                    int foodNeed = (int)(need * w.Data.Rules.Economy.FoodPerSoldierMonth * 6);
                    bool lean = preHarvest && s.Food - Domestic.MonthlyFoodUse(w, s) * 3 < foodNeed;
                    bool feasible = available >= need && capacity >= need && !lean;
                    if (capacity < need * 0.7) continue; // 지휘관이 턱없이 모자라면 계획도 세우지 않는다
                    var camp = new Campaign { Target = tid, Source = s.Id, Required = need, EstimatedDefense = d, StartTurn = g.TurnCount, Joint = joint };
                    options.Add((score, camp, feasible));
                }
            }
            foreach (var (score, camp, feasible) in options.OrderByDescending(o => o.Feasible).ThenByDescending(o => o.Score))
            {
                if (Campaigns.Count(c => !c.Defense) >= max) break;
                if (Campaigns.Any(c => c.Source == camp.Source || c.Target == camp.Target)) continue;
                if (!feasible)
                {
                    // 당장 칠 수 없으면 병력을 모으는 목표 하나만 둔다 (전력이 터무니없이 모자라면 포기)
                    var s = w.Cities[camp.Source];
                    int total = own.Sum(c => Surplus(g, c, faction));
                    if (Campaigns.Count > 0 || total < camp.Required * 0.5) continue;
                }
                Campaigns.Add(camp);
                if (camp.Joint) Stats.JointOps++;
                w.Log.Add($"[{w.Date}] (AI) {w.NameOf(faction)} 세력, {w.Cities[camp.Target]} 공략 계획 (출발 {w.Cities[camp.Source]}, 필요 병력 {camp.Required}){(camp.Joint ? " — 동맹과 공동 작전" : "")}");
            }
        }
    }
}
