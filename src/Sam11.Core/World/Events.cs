using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sam11.Core.Common;

namespace Sam11.Core.World
{
    public sealed class EventChoice
    {
        public string Id = "";
        public string Text = "";
        public double AiWeight = 1;
        public JArray Effects = new JArray();
    }

    /// <summary>시나리오 이벤트 (07 이벤트·시나리오 설계서 3장, 09 데이터 형식 3.5).</summary>
    public sealed class EventDef
    {
        public string Id = "";
        public string Name = "";
        public string Text = "";
        public bool Once = true;
        public bool Locked;
        /// <summary>선택지를 고르는 세력. 없으면 무작위(가중치).</summary>
        public string? Decider;
        public JToken Trigger = new JObject();
        public List<EventChoice> Choices = new List<EventChoice>();
        public JArray Effects = new JArray();

        public static EventDef Parse(JToken e)
        {
            var d = new EventDef
            {
                Id = e.Value<string>("id")!,
                Name = e["name"]?.Value<string>("ko") ?? e.Value<string>("id")!,
                Text = e.Value<string>("text") ?? "",
                Once = e.Value<bool?>("once") ?? true,
                Locked = e.Value<bool?>("locked") ?? false,
                Decider = e.Value<string>("decider"),
                Trigger = e["trigger"] ?? new JObject(),
                Effects = e["effects"] as JArray ?? new JArray(),
            };
            foreach (var c in (e["choices"] as JArray) ?? new JArray())
                d.Choices.Add(new EventChoice
                {
                    Id = c.Value<string>("id")!,
                    Text = c.Value<string>("text") ?? "",
                    AiWeight = c.Value<double?>("ai_weight") ?? 1,
                    Effects = c["effects"] as JArray ?? new JArray(),
                });
            return d;
        }
    }

    /// <summary>이벤트 선택지를 고르는 쪽 (AI 또는 플레이어 UI).</summary>
    public interface IEventChooser
    {
        EventChoice Choose(Game game, EventDef ev, string faction);
    }

    /// <summary>
    /// 이벤트 해석기: 조건을 평가하고, 선택지를 고르게 한 뒤 결과를 적용한다.
    /// 조건·결과 형식은 데이터(events.json)로 정의한다.
    /// </summary>
    public static class EventSystem
    {
        /// <summary>월초마다: 잠기지 않았고 아직 일어나지 않은 이벤트 중 조건이 맞는 것을 일으킨다.</summary>
        public static void RunMonthly(Game g)
        {
            var w = g.World;
            foreach (var ev in w.Events.ToList())
            {
                if (ev.Locked || (ev.Once && w.EventsDone.Contains(ev.Id))) continue;
                if (ev.Decider != null && (!w.Factions.TryGetValue(ev.Decider, out var df) || !df.Alive)) continue;
                if (!Check(g, ev.Trigger)) continue;
                Fire(g, ev);
            }
        }

        public static void Fire(Game g, EventDef ev, string? choiceId = null)
        {
            var w = g.World;
            w.EventsDone.Add(ev.Id);
            w.Log.Add($"[{w.Date}] ◆ {ev.Name}: {ev.Text}");
            if (ev.Choices.Count > 0)
            {
                EventChoice choice;
                if (choiceId != null) choice = ev.Choices.First(c => c.Id == choiceId);
                else if (ev.Decider != null && g.Controllers.TryGetValue(ev.Decider, out var ctl) && ctl is IEventChooser chooser)
                    choice = chooser.Choose(g, ev, ev.Decider);
                else choice = WeightedPick(w.Rng, ev.Choices);
                w.Log.Add($"[{w.Date}]   → {choice.Text}");
                Apply(g, choice.Effects);
            }
            Apply(g, ev.Effects);
        }

        public static EventChoice WeightedPick(Rng rng, IReadOnlyList<EventChoice> choices)
        {
            double total = choices.Sum(c => Math.Max(0, c.AiWeight));
            double r = rng.NextDouble() * total;
            foreach (var c in choices)
            {
                r -= Math.Max(0, c.AiWeight);
                if (r < 0) return c;
            }
            return choices[choices.Count - 1];
        }

        // ------------------------------------------------------------------ 조건

        public static bool Check(Game g, JToken cond)
        {
            var w = g.World;
            if (cond is JArray arr) return arr.All(c => Check(g, c));
            if (!(cond is JObject o)) return true;
            foreach (var p in o.Properties())
            {
                var v = p.Value;
                bool ok = p.Name switch
                {
                    "all" => v.All(c => Check(g, c)),
                    "any" => v.Any(c => Check(g, c)),
                    "not" => !Check(g, v),
                    "after" => DateKey(w.Date.Year, w.Date.Month) >= ParseDate(v.Value<string>()!),
                    "before" => DateKey(w.Date.Year, w.Date.Month) < ParseDate(v.Value<string>()!),
                    "chance" => w.Rng.Chance(v.Value<double>()),
                    "ruler_of" => w.Factions.TryGetValue(v.Value<string>("faction")!, out var f) && f.Alive && f.Ruler == v.Value<string>("officer"),
                    "owns" => v["strongholds"]!.Values<string>().All(id => w.Cities[id!].Owner == v.Value<string>("faction")),
                    "officer_in" => w.Officers.TryGetValue(v.Value<string>("officer")!, out var oi)
                                    && oi.Status == OfficerStatus.Active && oi.Location == v.Value<string>("stronghold"),
                    "officer_in_faction" => w.Officers.TryGetValue(v.Value<string>("officer")!, out var of)
                                    && of.Status == OfficerStatus.Active && of.Faction == v.Value<string>("faction"),
                    "loyalty_lte" => w.Officers.TryGetValue(v.Value<string>("officer")!, out var ol) && ol.Loyalty <= v.Value<int>("value"),
                    "faction_alive" => w.Factions.TryGetValue(v.Value<string>()!, out var fa) && fa.Alive,
                    "faction_destroyed" => w.Factions.TryGetValue(v.Value<string>()!, out var fd) && !fd.Alive,
                    "event_done" => w.EventsDone.Contains(v.Value<string>()!),
                    "enemy_units_near" => EnemyNear(g, v),
                    _ => throw new InvalidOperationException($"알 수 없는 이벤트 조건: {p.Name}"),
                };
                if (!ok) return false;
            }
            return true;
        }

        private static bool EnemyNear(Game g, JToken v)
        {
            var c = g.World.Cities[v.Value<string>("stronghold")!];
            int radius = v.Value<int>("radius");
            int troops = g.Field.Alive.Where(u => c.Owner != null && g.Field.Hostile(u.Faction, c.Owner) && !u.IsGarrison
                                                  && u.Position.DistanceTo(c.Placement.Center) <= radius).Sum(u => u.Troops);
            return troops >= (v.Value<int?>("troops_gte") ?? 1);
        }

        private static int DateKey(int year, int month) => year * 12 + month;

        private static int ParseDate(string s)
        {
            var parts = s.Split('-');
            return DateKey(int.Parse(parts[0]), int.Parse(parts[1]));
        }

        // ------------------------------------------------------------------ 결과

        public static void Apply(Game g, JArray effects)
        {
            foreach (JObject fx in effects)
                ApplyOne(g, fx);
        }

        private static void ApplyOne(Game g, JObject fx)
        {
            var w = g.World;
            string type = fx.Value<string>("type")!;
            switch (type)
            {
                case "move_capital":
                    w.Factions[fx.Value<string>("faction")!].Capital = fx.Value<string>("to")!;
                    break;
                case "city_population":
                {
                    var c = w.Cities[fx.Value<string>("stronghold")!];
                    c.Population = (int)(c.Population * fx.Value<double>("mult"));
                    break;
                }
                case "city_morale":
                {
                    var c = w.Cities[fx.Value<string>("stronghold")!];
                    c.Morale = Math.Max(0, Math.Min(100, c.Morale + fx.Value<int>("delta")));
                    break;
                }
                case "destroy_facilities":
                {
                    var c = w.Cities[fx.Value<string>("stronghold")!];
                    double ratio = fx.Value<double>("ratio");
                    foreach (var slot in c.Facilities.Keys.OrderBy(k => k).ToList())
                        if (w.Rng.Chance(ratio)) c.Facilities.Remove(slot);
                    break;
                }
                case "transfer_resources":
                {
                    var from = w.Cities[fx.Value<string>("from")!];
                    var to = w.Cities[fx.Value<string>("to")!];
                    int gold = (int)(from.Gold * (fx.Value<double?>("gold") ?? 0));
                    int food = (int)(from.Food * (fx.Value<double?>("food") ?? 0));
                    int troops = (int)(from.Troops * (fx.Value<double?>("troops") ?? 0));
                    from.Gold -= gold; to.Gold += gold;
                    from.Food -= food; to.Food += food;
                    from.Troops -= troops; to.Troops += troops;
                    double officerRatio = fx.Value<double?>("officers") ?? 0;
                    if (officerRatio > 0 && from.Owner != null)
                    {
                        var movers = w.OfficersAt(from.Id).Where(o => o.Faction == from.Owner).OrderBy(o => o.Id).ToList();
                        foreach (var o in movers.Take((int)Math.Round(movers.Count * officerRatio)))
                            o.Location = to.Id;
                    }
                    w.Log.Add($"[{w.Date}]   {from} → {to}: 병력 {troops}, 금 {gold}, 군량 {food} 이동");
                    break;
                }
                case "trust":
                    w.ChangeTrust(fx.Value<string>("faction")!, fx.Value<int>("delta"));
                    break;
                case "friendship":
                    w.ChangeFriendship(fx.Value<string>("a")!, fx.Value<string>("b")!, fx.Value<int>("delta"));
                    break;
                case "unlock_event":
                {
                    var ev = w.Events.FirstOrDefault(e => e.Id == fx.Value<string>("event"));
                    if (ev != null) ev.Locked = false;
                    break;
                }
                case "give_treasure":
                    w.TreasureOwner[fx.Value<string>("treasure")!] = fx.Value<string>("officer");
                    break;
                case "loyalty":
                {
                    var o = w.Officers[fx.Value<string>("officer")!];
                    o.Loyalty = Math.Max(0, Math.Min(100, o.Loyalty + fx.Value<int>("delta")));
                    break;
                }
                case "kill_officer":
                {
                    var o = w.Officers[fx.Value<string>("officer")!];
                    o.Status = OfficerStatus.Dead;
                    o.Location = "";
                    w.Log.Add($"[{w.Date}]   {Korean.IGa(o.ToString())} 죽었다");
                    break;
                }
                case "pact":
                {
                    string a = fx.Value<string>("a")!, b = fx.Value<string>("b")!;
                    if (!w.Factions.TryGetValue(a, out var fa) || !fa.Alive || !w.Factions.TryGetValue(b, out var fb) || !fb.Alive) break;
                    var kind = fx.Value<string>("kind") == "alliance" ? PactKind.Alliance : PactKind.Truce;
                    w.AddPact(kind, a, b, fx.Value<int>("months"), kind == PactKind.Alliance ? "동맹" : "정전");
                    break;
                }
                case "officer_join":
                    OfficerJoin(g, fx);
                    break;
                case "transfer_city":
                    g.TransferCity(fx.Value<string>("stronghold")!, fx.Value<string>("to")!, fx.Value<bool?>("with_officers") ?? true);
                    break;
                case "split_faction":
                    SplitFaction(g, fx);
                    break;
                case "special":
                    w.Log.Add($"[{w.Date}]   (미구현 특수 효과: {fx.Value<string>("key")})");
                    break;
                default:
                    throw new InvalidOperationException($"알 수 없는 이벤트 결과: {type}");
            }
        }

        /// <summary>
        /// 무장 합류: 재야·미등장 무장이 세력에 들어온다. allow_defect면 다른 세력 무장도 옮겨 온다 (군주는 제외).
        /// 위치는 location(그 세력 소유일 때)이나 세력 본거지.
        /// </summary>
        private static void OfficerJoin(Game g, JObject fx)
        {
            var w = g.World;
            if (!w.Officers.TryGetValue(fx.Value<string>("officer")!, out var o)) return;
            string faction = fx.Value<string>("faction")!;
            if (!w.Factions.TryGetValue(faction, out var f) || !f.Alive || o.Faction == faction && o.Status == OfficerStatus.Active) return;
            bool defect = fx.Value<bool?>("allow_defect") ?? false;
            bool ok = o.Status == OfficerStatus.Free || o.Status == OfficerStatus.Unborn
                      || (defect && o.Status == OfficerStatus.Active && o.Faction != null
                          && !(w.Factions.TryGetValue(o.Faction, out var cur) && cur.Ruler == o.Id));
            if (!ok) return;
            string? loc = fx.Value<string>("location");
            if (loc == null || !w.Cities.TryGetValue(loc, out var lc) || lc.Owner != faction) loc = f.Capital;
            o.Status = OfficerStatus.Active;
            o.Faction = faction;
            o.Location = loc;
            o.Loyalty = fx.Value<int?>("loyalty") ?? 90;
            o.CaptiveMonths = 0;
            w.Log.Add($"[{w.Date}]   {Korean.IGa(o.ToString())} {w.NameOf(faction)} 세력에 합류했다");
        }

        /// <summary>
        /// 세력 분열: 지정한 무장들이 새 세력을 세우고, 주동자가 있는 거점(같은 세력 소유)을 가져간다.
        /// 남은 세력은 successor가 잇는다.
        /// </summary>
        private static void SplitFaction(Game g, JObject fx)
        {
            var w = g.World;
            string newId = fx.Value<string>("new_faction")!;
            string ruler = fx.Value<string>("ruler")!;
            var leader = w.Officers[ruler];
            string? oldId = leader.Faction;
            if (oldId == null || !w.Factions.TryGetValue(oldId, out var old)) return;

            string oldName = w.NameOf(oldId);
            string? successor = fx.Value<string>("successor");
            if (successor != null && w.Officers.TryGetValue(successor, out var heir) && heir.Status == OfficerStatus.Active && heir.Faction == oldId)
                old.Ruler = successor;

            // 근거지: seat(지정 거점) → 주동자 위치 → 본거지가 아닌 옛 세력 거점
            // 지정 거점을 이미 잃었으면 그 근처의 (본거지가 아닌) 옛 세력 거점을 쓴다
            string? wanted = fx.Value<string>("seat");
            string? seat = wanted;
            if (seat != null && (!w.Cities.TryGetValue(seat, out var named) || named.Owner != oldId))
            {
                var near = w.Cities[wanted!].Placement.Center;
                seat = w.Cities.Values.Where(c => c.Owner == oldId && c.Id != old.Capital)
                        .OrderBy(c => c.Placement.Center.DistanceTo(near)).ThenBy(c => c.Id).Select(c => c.Id).FirstOrDefault();
            }
            else if (seat == null)
                seat = w.Officers.TryGetValue(fx.Value<string>("take_location_of") ?? ruler, out var anchor) ? anchor.Location : null;
            if (seat == null || seat == "" || w.Cities[seat].Owner != oldId)
                seat = w.Cities.Values.Where(c => c.Owner == oldId && c.Id != old.Capital).OrderByDescending(c => c.Def.IsCity).ThenBy(c => c.Id)
                        .Select(c => c.Id).FirstOrDefault();
            if (seat == null) return;

            var nf = new FactionState { Id = newId, Ruler = ruler, Color = fx.Value<string>("color") ?? "#888888", Capital = seat };
            w.Factions[newId] = nf;
            w.Cities[seat].Owner = newId;
            w.Cities[seat].Governor = null;
            g.Field.Strongholds[seat].Owner = newId;
            // 함께 가져가는 거점 (옛 세력 소유이고 본거지가 아닐 때만)
            foreach (var extra in fx["strongholds"]?.Values<string>() ?? Enumerable.Empty<string>())
                if (extra != null && w.Cities.TryGetValue(extra, out var ec) && ec.Owner == oldId && extra != old.Capital)
                {
                    ec.Owner = newId;
                    ec.Governor = null;
                    g.Field.Strongholds[extra].Owner = newId;
                    foreach (var o in w.OfficersAt(extra).Where(o => o.Faction == oldId).ToList()) o.Location = old.Capital;
                }
            if (old.Capital == seat)
                old.Capital = w.Cities.Values.Where(c => c.Owner == oldId).OrderByDescending(c => c.Def.IsCity).Select(c => c.Id).FirstOrDefault() ?? old.Capital;

            foreach (var id in fx["officers"]!.Values<string>())
            {
                if (!w.Officers.TryGetValue(id!, out var o) || o.Status != OfficerStatus.Active || o.Faction != oldId) continue;
                o.Faction = newId;
                o.Loyalty = id == ruler ? 100 : 80;
                if (o.Location == "" || w.Cities[o.Location].Owner != newId) o.Location = seat;
            }
            // 새 세력 거점에 남은 옛 세력 무장은 옛 세력 본거지로
            foreach (var o in w.OfficersAt(seat).Where(o => o.Faction == oldId).ToList())
                o.Location = old.Capital;
            w.ChangeFriendship(newId, oldId, fx.Value<int?>("friendship") ?? -50);
            g.OnFactionCreated(newId);
            w.Log.Add($"[{w.Date}]   ★ {Korean.IGa(leader.ToString())} {w.Cities[seat]}에서 독립했다. {oldName} 세력은 {Korean.IGa(w.Officers[old.Ruler].ToString())} 잇는다");
        }
    }
}
