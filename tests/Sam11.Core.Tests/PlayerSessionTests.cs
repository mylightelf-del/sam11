using Newtonsoft.Json.Linq;
using Sam11.Core.Play;
using Sam11.Core.World;

namespace Sam11.Core.Tests;

public class PlayerSessionTests
{
    private static PlayerSession New(string faction, ulong seed = 7) =>
        PlayerSession.NewGame(TestData.Data, TestData.Map, "190", seed, new GameOptions { Players = { faction } });

    private static JObject C(string type, object? args = null)
    {
        var o = args == null ? new JObject() : JObject.FromObject(args);
        o["type"] = type;
        return o;
    }

    [Test]
    public void WaitsForPlayerAndEndTurnAdvancesDate()
    {
        var s = New("cao_cao");
        Assert.That(s.Faction, Is.EqualTo("cao_cao"));
        string date = s.World.Date.ToString();
        var o = s.World.Officers.Values.First(x => x.Faction == "cao_cao" && x.Status == OfficerStatus.Active);
        var r = s.Execute(C("train", new { officer = o.Id, city = o.Location }));
        Assert.That(r.Ok, r.Message);
        var again = s.Execute(C("train", new { officer = o.Id, city = o.Location }));
        Assert.That(again.Ok, Is.False, "한 차례에 한 번만 행동");
        s.Execute(C("end_turn"));
        Assert.That(s.Faction, Is.EqualTo("cao_cao"));
        Assert.That(s.World.Date.ToString(), Is.Not.EqualTo(date));
    }

    [Test]
    public void RejectsOtherFactionsOfficersAndUnknownCommands()
    {
        var s = New("cao_cao");
        var foe = s.World.Officers.Values.First(x => x.Faction == "dong_zhuo" && x.Status == OfficerStatus.Active);
        Assert.That(s.Execute(C("train", new { officer = foe.Id, city = foe.Location })).Ok, Is.False);
        Assert.That(s.Execute(C("fly_to_moon")).Ok, Is.False);
        Assert.That(s.Execute(C("train", new { })).Ok, Is.False);
    }

    [Test]
    public void SaveAndLoadReproducesStateAndFuture()
    {
        var s = New("cao_cao");
        var cao = s.World.Officers["cao_cao"];
        for (int t = 0; t < 12; t++)
        {
            if (t == 2) s.Execute(C("recruit", new { officer = "cao_cao", city = cao.Location }));
            if (t == 4)
            {
                var r = s.Execute(C("sortie", new { city = cao.Location, officers = new[] { "cao_cao" }, formation = "fish_scale", troops = 2000, food = 2000 }));
                Assert.That(r.Ok, r.Message);
                var u = s.Game.Field.Alive.First(x => x.Faction == "cao_cao" && !x.IsGarrison && x.Leader.Id == "cao_cao");
                var dest = s.Game.Field.Reachable(u).Keys.OrderByDescending(h => h.DistanceTo(u.Position)).First();
                Assert.That(s.Execute(C("move", new { unit = u.Id, q = dest.Q, r = dest.R })).Ok);
                Assert.That(s.Execute(C("move", new { unit = u.Id, q = u.Position.Q, r = u.Position.R })).Ok, Is.False, "한 차례에 한 번 이동");
            }
            if (t % 3 == 1) s.Execute(C("delegate"));
            s.Execute(C("end_turn"));
        }
        var save = s.Save();
        var json = save.ToString();
        var loaded = PlayerSession.Load(TestData.Data, TestData.Map, JObject.Parse(json));
        Assert.That(loaded.Digest(), Is.EqualTo(s.Digest()));
        Assert.That(loaded.World.Date.ToString(), Is.EqualTo(s.World.Date.ToString()));
        for (int t = 0; t < 6; t++)
        {
            s.Execute(C("delegate")); s.Execute(C("end_turn"));
            loaded.Execute(C("delegate")); loaded.Execute(C("end_turn"));
        }
        Assert.That(loaded.Digest(), Is.EqualTo(s.Digest()), "불러온 뒤에도 같은 미래");
    }

    [Test]
    public void TamperedSaveIsRejected()
    {
        var s = New("cao_cao");
        s.Execute(C("end_turn"));
        var save = s.Save();
        save["seed"] = "8";
        Assert.Throws<InvalidOperationException>(() => PlayerSession.Load(TestData.Data, TestData.Map, save));
    }

    [Test]
    public void PlayerDecidesOwnEvents()
    {
        var s = New("dong_zhuo");
        for (int t = 0; t < 9 && !s.World.PendingEvents.Any(); t++) s.Execute(C("end_turn"));
        var pe = s.World.PendingEvents.FirstOrDefault(p => p.Event == "relocate_changan");
        Assert.That(pe, Is.Not.Null, "장안 천도는 플레이어(동탁)가 고른다");
        var ev = s.World.Events.First(e => e.Id == "relocate_changan");
        string other = ev.Choices.OrderBy(c => c.AiWeight).First().Id;
        var r = s.Execute(C("event_choice", new { @event = "relocate_changan", choice = other }));
        Assert.That(r.Ok, r.Message);
        Assert.That(s.World.PendingEvents, Is.Empty);
    }

    [Test]
    public void UnansweredEventGetsDefaultAtEndOfTurn()
    {
        var s = New("dong_zhuo");
        for (int t = 0; t < 9 && !s.World.PendingEvents.Any(); t++) s.Execute(C("end_turn"));
        Assert.That(s.World.PendingEvents, Is.Not.Empty);
        s.Execute(C("end_turn"));
        Assert.That(s.World.PendingEvents.Where(p => p.Event == "relocate_changan"), Is.Empty);
        Assert.That(s.World.Factions["dong_zhuo"].Capital, Is.EqualTo("changan"), "기본값은 가중치가 가장 큰 선택지(천도)");
    }

    [Test]
    public void DelegatedPlayerCanFinishYears()
    {
        var s = New("liu_bei", 3);
        for (int t = 0; t < 72 && !s.IsOver; t++)
        {
            s.Execute(C("delegate"));
            s.Execute(C("end_turn"));
        }
        Assert.That(s.World.Date.Year, Is.GreaterThanOrEqualTo(191));
        Assert.That(s.Commands.Count, Is.GreaterThan(100));
    }

    [Test]
    public void ManualCampaignMarchesAndFights()
    {
        // 사람이 하듯 명령만으로: 출진 → 가장 가까운 적 거점으로 행군 → 붙으면 전법·공격
        var s = New("cao_cao", 5);
        var w = s.World;
        var home = w.Cities[w.Officers["cao_cao"].Location];
        var r = s.Execute(C("sortie", new { city = home.Id, officers = new[] { "cao_cao", "xiahou_dun", "xiahou_yuan" }, formation = "fish_scale", troops = 9000, food = 9000 }));
        Assert.That(r.Ok, r.Message);
        int id = s.Game.Field.Alive.First(u => u.Faction == "cao_cao" && u.Leader.Id == "cao_cao").Id;
        int fights = 0;
        for (int t = 0; t < 40 && !s.IsOver; t++)
        {
            var b = s.Game.Field;
            var u = b.Units.FirstOrDefault(x => x.Id == id);
            if (u == null || !u.IsAlive) break;
            var foe = b.Alive.Where(e => b.Hostile(e.Faction, "cao_cao")).OrderBy(e => e.Position.DistanceTo(u.Position)).FirstOrDefault();
            var target = w.Cities.Values.Where(c => c.Owner != null && c.Owner != "cao_cao" && !w.AtPeace(c.Owner, "cao_cao"))
                          .OrderBy(c => c.Placement.Center.DistanceTo(u.Position)).First();
            if (foe == null || !b.InRange(u, foe))
            {
                var field = b.PathField(u, target.Placement.Center);
                var reach = b.Reachable(u);
                var dest = reach.Keys.Where(field.ContainsKey).OrderBy(h => field[h]).FirstOrDefault(u.Position);
                if (dest != u.Position) s.Execute(C("move", new { unit = id, q = dest.Q, r = dest.R }));
                foe = b.Alive.Where(e => b.Hostile(e.Faction, "cao_cao")).OrderBy(e => e.Position.DistanceTo(u.Position)).FirstOrDefault();
            }
            if (foe != null && u.IsAlive && b.InRange(u, foe))
            {
                var tac = b.AvailableTactics(u).FirstOrDefault(x => x.EnergyCost <= u.Energy && x.Power > 0 && b.InRange(u, foe, b.TacticRange(u, x)) && !b.TacticTargetsStructureOnly(x));
                var res = tac != null ? s.Execute(C("tactic", new { unit = id, tactic = tac.Id, target = foe.Id }))
                                      : s.Execute(C("attack", new { unit = id, target = foe.Id }));
                if (res.Ok) fights++;
            }
            s.Execute(C("end_turn"));
        }
        Assert.That(fights, Is.GreaterThan(0), "적과 싸웠다");
        var save = s.Save();
        Assert.That(PlayerSession.Load(TestData.Data, TestData.Map, save).Digest(), Is.EqualTo(s.Digest()));
    }

    [Test]
    public void CorpsRunsOnlyItsCities()
    {
        var s = New("dong_zhuo", 2);
        var w = s.World;
        var own = w.Cities.Values.Where(c => c.Owner == "dong_zhuo" && c.Def.IsCity).OrderBy(c => c.Id).ToList();
        var corpsCities = own.Where(c => c.Id != w.Factions["dong_zhuo"].Capital).Take(3).Select(c => c.Id).ToList();
        var r = s.Execute(C("corps_set", new { corps = "서부", cities = corpsCities }));
        Assert.That(r.Ok, r.Message);
        Assert.That(s.Execute(C("corps_set", new { corps = "x", cities = new[] { "xiapi" } })).Ok, Is.False, "남의 도시는 못 맡긴다");
        int logStart = w.Log.Count;
        for (int t = 0; t < 18; t++) s.Execute(C("end_turn"));
        var outside = own.Select(c => c.Id).Except(s.Corps.Values.SelectMany(x => x)).Select(id => w.Cities[id].ToString()).ToList();
        var inside = corpsCities.Select(id => w.Cities[id].ToString()).ToList();
        var domestic = w.Log.Skip(logStart).Where(l => l.Contains("징병") || l.Contains(" 훈련 (") || l.Contains("착공")).ToList();
        Assert.That(domestic.Any(l => inside.Any(l.Contains)), "군단 도시는 내정을 한다");
        Assert.That(domestic.Where(l => outside.Any(n => l.Contains($", {n}")) && !inside.Any(l.Contains)), Is.Empty, "맡기지 않은 도시는 손대지 않는다");

        var loaded = PlayerSession.Load(TestData.Data, TestData.Map, s.Save());
        Assert.That(loaded.Digest(), Is.EqualTo(s.Digest()));
        Assert.That(loaded.Corps.Keys, Is.EqualTo(s.Corps.Keys));
        Assert.That(s.Execute(C("corps_remove", new { corps = "서부" })).Ok);
        Assert.That(s.Corps, Is.Empty);
    }
}
