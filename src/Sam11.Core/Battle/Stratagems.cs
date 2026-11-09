using System;
using System.Linq;
using Sam11.Core.Common;
using Sam11.Core.Data;

namespace Sam11.Core.Battle
{
    /// <summary>전투 계략 (05 외교·계략 설계서 3.1). 지력으로 겨루고 기력을 쓴다.</summary>
    public static class Stratagems
    {
        public static readonly string[] Kinds = { "fire", "confuse", "false_report", "ambush", "calm" };

        public static string NameOf(string kind) => kind switch
        {
            "fire" => "화계", "confuse" => "교란", "false_report" => "위보", "ambush" => "매복", "calm" => "진정", _ => kind,
        };

        private static BattleStratagemRules R(Battle b) => b.Data.Rules.Stratagem.Battle;

        /// <summary>위보로 움직일 수 있는 부대인지: 성 안·성벽 위가 아니고, 옆에 빈 칸이 있어야 한다.</summary>
        public static bool CanBeMisled(Battle b, Unit target) =>
            !b.Structures.ContainsKey(target.Position)
            && target.Position.Neighbors().Any(n => b.MoveCost2(target, n) != null && b.UnitAt(n) == null && !b.Structures.ContainsKey(n));

        /// <summary>성공률: 기본 + (지력 차) ÷ 100 + 계략 특기 − 상대 간파 특기.</summary>
        public static double Chance(Battle b, Unit actor, Unit target, string kind)
        {
            var r = R(b);
            double p = r.BaseChance + (actor.EffInt - target.EffInt) / r.IntDivisor;
            var ctx = new Modifiers.Context { Unit = actor, Terrain = b.Map.TerrainAt(actor.Position), Weather = b.WeatherId, Stratagem = kind };
            p += Modifiers.SumMul(Modifiers.Collect(actor, null), "stratagem_success", ctx);
            var tctx = new Modifiers.Context { Unit = target, Terrain = b.Map.TerrainAt(target.Position), Weather = b.WeatherId };
            p -= Modifiers.SumMul(Modifiers.Collect(target, null), "stratagem_resist", tctx);
            return Math.Max(0.05, Math.Min(0.95, p));
        }

        /// <summary>
        /// 계략 실행. target은 화계·교란·위보는 적 부대, 진정은 아군 부대(자신 포함), 매복은 null.
        /// 성공하면 true. 실패하고 상대 지력이 20 이상 높으면 간파당해 역으로 혼란에 빠질 수 있다.
        /// </summary>
        public static bool Use(Battle b, Unit actor, string kind, Unit? target = null)
        {
            var r = R(b);
            b.EnsureCanActPublic(actor);
            if (!r.Energy.TryGetValue(kind, out int cost)) throw new InvalidOperationException($"없는 계략: {kind}");
            if (actor.Energy < cost) throw new InvalidOperationException($"기력 부족: {actor.Energy}/{cost}");
            string name = NameOf(kind);

            if (kind == "ambush")
            {
                string t = b.Map.TerrainAt(actor.Position);
                if (!(t == "forest" || t == "mountain" || b.WeatherId == "fog"))
                    throw new InvalidOperationException("매복은 숲·산이나 안개 속에서만");
                actor.Energy -= cost;
                actor.Hidden = true;
                b.Log.Add($"{Korean.IGa(actor.Label)} 매복했다");
                b.FinishPublic(actor);
                return true;
            }
            if (kind == "calm")
            {
                target ??= actor;
                if (target.Faction != actor.Faction || target.Position.DistanceTo(actor.Position) > 1)
                    throw new InvalidOperationException("진정은 자신이나 붙어 있는 아군에게");
                actor.Energy -= cost;
                target.TimedEffects.RemoveAll(e => e.Key == "confused");
                if (target.Status == UnitStatus.Confused) target.Status = UnitStatus.Ready;
                b.Log.Add($"{actor.Label}의 진정 → {Korean.IGa(target.Label)} 혼란에서 벗어났다");
                if (target != actor) b.FinishPublic(actor);
                else actor.HasActed = true;
                return true;
            }

            if (target == null || !target.IsAlive || !b.Hostile(actor.Faction, target.Faction))
                throw new InvalidOperationException("계략 대상이 아님");
            if (actor.Position.DistanceTo(target.Position) > r.Range) throw new InvalidOperationException("사거리 밖");
            if (kind == "fire" && b.WeatherId == "rain") throw new InvalidOperationException("비가 와서 불을 놓을 수 없다");
            if (kind == "false_report" && !CanBeMisled(b, target))
                throw new InvalidOperationException("성벽 위에 있거나 움직일 곳이 없어 위보가 통하지 않는다");
            actor.Energy -= cost;
            actor.Hidden = false;
            double chance = Chance(b, actor, target, kind);
            if (!b.Rng.Chance(chance))
            {
                b.Log.Add($"{actor.Label}의 {name} 실패 ({chance:P0})");
                if (target.EffInt - actor.EffInt >= r.BackfireIntGap && b.Rng.Chance(r.BackfireChance))
                {
                    b.Log.Add($"  {Korean.IGa(target.Label)} 간파했다! {Korean.IGa(actor.Label)} 역으로 혼란에 빠졌다");
                    b.ConfusePublic(actor);
                }
                b.FinishPublic(actor);
                return false;
            }
            switch (kind)
            {
                case "fire":
                {
                    b.Map.Ignite(target.Position, b.Data.Rules.Combat.FireTurns);
                    if (b.WeatherId == "gale")
                        foreach (var n in target.Position.Neighbors()) b.Map.Ignite(n, b.Data.Rules.Combat.FireTurns);
                    var ctx = new Modifiers.Context { Unit = actor, Terrain = b.Map.TerrainAt(actor.Position), Weather = b.WeatherId };
                    double mult = 1 + Modifiers.SumMul(Modifiers.Collect(actor, null), "fire_damage", ctx);
                    int dmg = Math.Max(1, (int)(target.Troops * r.FireImmediateRatio * mult));
                    target.Troops -= dmg;
                    b.Log.Add($"{actor.Label}의 화계! {target.Label} 주변에 불길 — {dmg} 피해");
                    b.LoseMorale(target, r.FireMorale);
                    if (target.Troops <= 0) b.DestroyPublic(target, actor);
                    break;
                }
                case "confuse":
                    b.Log.Add($"{actor.Label}의 교란!");
                    b.ConfusePublic(target);
                    break;
                case "false_report":
                {
                    int moved = 0;
                    for (int i = 0; i < r.FalseReportSteps; i++)
                    {
                        var options = target.Position.Neighbors().Where(n => b.MoveCost2(target, n) != null && b.UnitAt(n) == null
                                                                             && !b.Structures.ContainsKey(n)).ToList();
                        if (options.Count == 0 || b.Structures.ContainsKey(target.Position)) break;
                        target.Position = options[b.Rng.NextInt(options.Count)];
                        moved++;
                    }
                    b.Log.Add($"{actor.Label}의 위보! {Korean.IGa(target.Label)} 거짓 소식에 {moved}칸 엉뚱하게 움직였다");
                    break;
                }
                default:
                    throw new InvalidOperationException($"없는 계략: {kind}");
            }
            b.FinishPublic(actor);
            return true;
        }
    }
}
