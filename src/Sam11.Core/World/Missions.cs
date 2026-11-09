using System;
using System.Collections.Generic;
using System.Linq;
using Sam11.Core.Common;
using Sam11.Core.Data;

namespace Sam11.Core.World
{
    /// <summary>진행 중인 전략 계략 임무. 끝날 때까지 실행 무장은 다른 일을 할 수 없다.</summary>
    public sealed class Mission
    {
        public string Kind = "";
        public string Agent = "";
        public string Faction = "";
        /// <summary>대상 도시 (유언비어·내응·첩보) 또는 대상 무장이 있는 도시.</summary>
        public string? TargetCity;
        /// <summary>대상 무장 (이간·매수·내응).</summary>
        public string? TargetOfficer;
        public int TurnsLeft;
        public int Gold;
    }

    /// <summary>전략 계략 (05 외교·계략 설계서 3.2).</summary>
    public static class Missions
    {
        public static readonly string[] Kinds = { "rumor", "sow_discord", "bribe", "insider", "spy" };

        public static string NameOf(string kind) => kind switch
        {
            "rumor" => "유언비어", "sow_discord" => "이간", "bribe" => "매수", "insider" => "내응", "spy" => "첩보", _ => kind,
        };

        private static StrategicStratagemRules R(WorldState w) => w.Data.Rules.Stratagem.Strategic;

        /// <summary>
        /// 임무 시작: 금을 내고 무장을 보낸다. 대상은 적 세력 도시(유언비어·첩보), 적 무장(이간·매수),
        /// 적 도시에 있는 적 무장(내응: 그 도시를 칠 때 성문 하나가 안에서 열린다).
        /// </summary>
        public static Mission Start(WorldState w, string agentId, string kind, string? targetCity, string? targetOfficer = null)
        {
            var r = R(w);
            if (!w.Officers.TryGetValue(agentId, out var agent) || agent.Status != OfficerStatus.Active || agent.Faction == null)
                throw new InvalidOperationException("보낼 수 없는 무장");
            if (agent.Acted || w.Missions.Any(m => m.Agent == agentId))
                throw new InvalidOperationException($"{Korean.EunNeun(agent.ToString())} 지금 움직일 수 없음");
            if (!r.Cost.TryGetValue(kind, out int cost)) throw new InvalidOperationException($"없는 계략: {kind}");
            string faction = agent.Faction;
            if (targetOfficer != null)
            {
                var t = w.Officers[targetOfficer];
                if (t.Status != OfficerStatus.Active || t.Faction == null || t.Faction == faction) throw new InvalidOperationException("대상이 적 무장이 아님");
                targetCity = t.Location;
            }
            if (targetCity == null || !w.Cities.TryGetValue(targetCity, out var city)) throw new InvalidOperationException("대상 도시가 없음");
            string? enemy = targetOfficer != null ? w.Officers[targetOfficer].Faction : city.Owner;
            if (enemy == null || enemy == faction || w.AtPeace(enemy, faction)) throw new InvalidOperationException("적 세력이 아님");
            if ((kind == "sow_discord" || kind == "bribe" || kind == "insider") && targetOfficer == null)
                throw new InvalidOperationException($"{NameOf(kind)}에는 대상 무장이 필요");
            var home = w.Cities.Values.Where(c => c.Owner == faction).OrderByDescending(c => c.Gold).First();
            if (home.Gold < cost) throw new InvalidOperationException($"금 부족: {home.Gold}/{cost}");
            home.Gold -= cost;
            agent.Acted = true;
            var m = new Mission
            {
                Kind = kind, Agent = agentId, Faction = faction, TargetCity = targetCity, TargetOfficer = targetOfficer,
                TurnsLeft = r.Turns.TryGetValue(kind, out var turns) ? turns : 3, Gold = cost,
            };
            w.Missions.Add(m);
            w.Log.Add($"[{w.Date}] {Korean.IGa(agent.ToString())} {w.Cities[targetCity]}에 {NameOf(kind)} 공작을 시작했다");
            return m;
        }

        /// <summary>성공률: 기본 + (실행 무장 지력 − 상대 세력 최고 지력) ÷ 100 + 귀모 특기 − 간파 특기.</summary>
        public static double Chance(WorldState w, Mission m)
        {
            var r = R(w);
            var agent = w.Officers[m.Agent].Def;
            string? enemy = m.TargetOfficer != null ? w.Officers[m.TargetOfficer].Faction : w.Cities[m.TargetCity!].Owner;
            var guard = w.Officers.Values.Where(o => o.Faction == enemy && o.Status == OfficerStatus.Active && o.Location == m.TargetCity)
                         .Select(o => o.Def).OrderByDescending(o => o.Stats.Int).FirstOrDefault();
            double p = r.BaseChance + (agent.Stats.Int - (guard?.Stats.Int ?? 50)) / r.IntDivisor;
            p += TraitStratagem(w.Data, agent, m.Kind);
            if (guard != null) p -= TraitResist(w.Data, guard);
            if (m.TargetOfficer != null)
            {
                var t = w.Officers[m.TargetOfficer];
                var ruler = w.Officers[w.Factions[m.Faction].Ruler].Def;
                int diff = Math.Abs(ruler.Hidden.Compat - t.Def.Hidden.Compat);
                diff = Math.Min(diff, 150 - diff);
                if (m.Kind == "sow_discord") p += (75 - diff) / 300.0; // 우리 군주와 상성이 가까울수록 잘 흔들린다
                if (m.Kind == "bribe" || m.Kind == "insider")
                    p += (r.BribeLoyaltyMax - t.Loyalty) / 100.0 - t.Def.Hidden.Loyalty * 0.03 + (75 - diff) / 300.0;
            }
            return Math.Max(0.05, Math.Min(0.9, p));
        }

        private static double TraitStratagem(GameData data, OfficerDef o, string kind) =>
            o.Traits.Where(data.Traits.ContainsKey).SelectMany(t => data.Traits[t].Effects)
             .Where(e => e.Type == "modifier" && e.Stat == "stratagem_success"
                         && (e.When == null || e.When["stratagem"]?.ToString().Contains(kind) == true))
             .Sum(e => e.Value);

        private static double TraitResist(GameData data, OfficerDef o) =>
            o.Traits.Where(data.Traits.ContainsKey).SelectMany(t => data.Traits[t].Effects)
             .Where(e => e.Type == "modifier" && e.Stat == "stratagem_resist").Sum(e => e.Value);

        /// <summary>매 턴: 임무를 진행하고 끝난 것은 결과를 낸다.</summary>
        public static void Tick(WorldState w)
        {
            foreach (var m in w.Missions.ToList())
            {
                var agent = w.Officers[m.Agent];
                if (agent.Status != OfficerStatus.Active || agent.Faction != m.Faction) { w.Missions.Remove(m); continue; }
                agent.Acted = true; // 임무 중에는 다른 일을 못 한다
                if (--m.TurnsLeft > 0) continue;
                w.Missions.Remove(m);
                Resolve(w, m);
            }
        }

        private static void Resolve(WorldState w, Mission m)
        {
            var r = R(w);
            var agent = w.Officers[m.Agent];
            var city = w.Cities[m.TargetCity!];
            string? enemy = m.TargetOfficer != null ? w.Officers[m.TargetOfficer].Faction : city.Owner;
            if (enemy == null || enemy == m.Faction || w.AtPeace(enemy, m.Faction)) return;
            double p = Chance(w, m);
            string name = NameOf(m.Kind);
            if (!w.Rng.Chance(p))
            {
                w.ChangeFriendship(m.Faction, enemy, r.DetectFriendship);
                if (w.Rng.Chance(r.DetectCapture))
                {
                    agent.PreviousFaction = agent.Faction;
                    agent.Status = OfficerStatus.Captive;
                    agent.Faction = enemy;
                    agent.Location = city.Owner == enemy ? city.Id : w.Factions[enemy].Capital;
                    w.Log.Add($"[{w.Date}] {name} 발각! {Korean.IGa(agent.ToString())} {w.NameOf(enemy)} 세력에 사로잡혔다");
                }
                else w.Log.Add($"[{w.Date}] {agent}의 {name} 실패 ({p:P0})");
                return;
            }
            switch (m.Kind)
            {
                case "rumor":
                {
                    int loss = r.RumorMorale + agent.Def.Stats.Int / 10;
                    city.Morale = Math.Max(0, city.Morale - loss);
                    w.Log.Add($"[{w.Date}] 유언비어! {city} 민심 -{loss}");
                    break;
                }
                case "sow_discord":
                {
                    var t = w.Officers[m.TargetOfficer!];
                    int loss = r.DiscordLoyalty + agent.Def.Stats.Int / 10;
                    t.Loyalty = Math.Max(0, t.Loyalty - loss);
                    w.Log.Add($"[{w.Date}] 이간! {t}의 충성 -{loss} (→ {t.Loyalty})");
                    break;
                }
                case "bribe":
                {
                    var t = w.Officers[m.TargetOfficer!];
                    if (t.Loyalty > r.BribeLoyaltyMax || w.Factions[t.Faction!].Ruler == t.Id)
                    {
                        w.Log.Add($"[{w.Date}] 매수 실패: {Korean.EunNeun(t.ToString())} 흔들리지 않았다");
                        break;
                    }
                    t.Faction = m.Faction;
                    t.Location = w.Factions[m.Faction].Capital;
                    t.Loyalty = 60;
                    w.Log.Add($"[{w.Date}] 매수! {Korean.IGa(t.ToString())} {w.NameOf(m.Faction)} 세력으로 넘어왔다");
                    break;
                }
                case "insider":
                {
                    var t = w.Officers[m.TargetOfficer!];
                    w.Insiders[city.Id] = m.Faction;
                    w.Log.Add($"[{w.Date}] 내응 약속: {Korean.IGa(t.ToString())} {city}를 칠 때 성문을 열기로 했다");
                    break;
                }
                case "spy":
                {
                    int until = w.Date.Year * 12 + w.Date.Month + r.SpyMonths;
                    w.SpyReports[(m.Faction, city.Id)] = until;
                    w.Log.Add($"[{w.Date}] 첩보: {city}의 사정을 {r.SpyMonths}개월 동안 훤히 안다");
                    break;
                }
            }
        }

        /// <summary>
        /// 내응 발동: 약속받은 세력의 부대가 그 도시 근처(5칸)에 오면 성문 하나가 안에서 열린다. 한 번만.
        /// </summary>
        public static void TriggerInsiders(Game g)
        {
            var w = g.World;
            foreach (var kv in w.Insiders.ToList())
            {
                var city = w.Cities[kv.Key];
                if (city.Owner == kv.Value) { w.Insiders.Remove(kv.Key); continue; }
                bool near = g.Field.Alive.Any(u => u.Faction == kv.Value && !u.IsGarrison && u.Position.DistanceTo(city.Placement.Center) <= 5);
                if (!near) continue;
                HexCoord? gate = city.Placement.Tiles.Where(t => t.Value == TileKind.Gate).Select(t => (HexCoord?)t.Key)
                                     .FirstOrDefault(h => g.Field.Structures.TryGetValue(h!.Value, out var s) && s.Blocking);
                w.Insiders.Remove(kv.Key);
                if (gate == null) continue; // 이미 성문이 다 열렸다
                g.Field.Structures[gate.Value].Durability = 0;
                g.Field.StructureVersion++;
                g.Field.Map.Set(gate.Value, "street");
                w.Log.Add($"[{w.Date}] ★ 내응! {city}의 성문이 안에서 열렸다");
                g.Field.Log.Add($"내응! {city}의 성문이 안에서 열렸다");
            }
        }

        /// <summary>충성이 바닥난 야심가는 세력을 떠난다 (매월).</summary>
        public static void Defections(WorldState w)
        {
            var r = w.Data.Rules.Stratagem.Defection;
            foreach (var o in w.Officers.Values.Where(o => o.Status == OfficerStatus.Active && o.Faction != null).ToList())
            {
                if (w.Factions[o.Faction!].Ruler == o.Id) continue;
                if (o.Loyalty >= r.LoyaltyBelow || o.Def.Hidden.Ambition < r.AmbitionAtLeast) continue;
                if (!w.Rng.Chance(r.MonthlyChance)) continue;
                w.Log.Add($"[{w.Date}] {Korean.IGa(o.ToString())} {w.NameOf(o.Faction!)} 세력을 떠났다 (충성 {o.Loyalty})");
                o.Status = OfficerStatus.Free;
                o.Faction = null;
                if (o.Location == "") o.Location = w.Cities.Keys.First();
            }
        }
    }
}
