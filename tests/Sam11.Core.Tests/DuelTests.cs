using Sam11.Core.Duel;

namespace Sam11.Core.Tests;

public class CardDuelTests
{
    [Test]
    public void CardCycleAndSpecial()
    {
        Assert.That(CardDuel.Beats(CardType.First, CardType.Second), Is.EqualTo(1));
        Assert.That(CardDuel.Beats(CardType.Second, CardType.Third), Is.EqualTo(1));
        Assert.That(CardDuel.Beats(CardType.Third, CardType.First), Is.EqualTo(1));
        Assert.That(CardDuel.Beats(CardType.Second, CardType.First), Is.EqualTo(-1));
        Assert.That(CardDuel.Beats(CardType.Special, CardType.Third, 10, 8), Is.EqualTo(1), "특수 카드는 위력으로 겨룬다");
        Assert.That(CardDuel.Beats(CardType.Special, CardType.Third, 8, 10), Is.EqualTo(-1));
        Assert.That(CardDuel.Beats(CardType.First, CardType.First), Is.EqualTo(0));
    }

    private static double WinRate(string a, string b, DuelKind kind, int n = 400)
    {
        var d = TestData.Data;
        int wins = 0;
        for (int i = 0; i < n; i++)
            if (new CardDuel(d, new Rng((ulong)i), kind, d.Officers[a], d.Officers[b]).Run()) wins++;
        return wins / (double)n;
    }

    [Test]
    public void StrongerWarriorUsuallyWinsCombat()
    {
        Assert.That(WinRate("lu_bu", "cao_hong", DuelKind.Combat), Is.GreaterThan(0.8));
        Assert.That(WinRate("cao_hong", "lu_bu", DuelKind.Combat), Is.LessThan(0.2));
        Assert.That(WinRate("yan_liang", "wen_chou", DuelKind.Combat), Is.InRange(0.3, 0.7), "비슷한 무장끼리는 승부를 알 수 없다");
        // 일기당천 특기는 유리하지만 승부를 다 정하지는 않는다 (관우 대 장비, 무력 98 동률)
        Assert.That(WinRate("guan_yu", "zhang_fei", DuelKind.Combat), Is.InRange(0.3, 0.55));
        Assert.That(WinRate("guan_yu", "hua_xiong", DuelKind.Combat), Is.GreaterThan(0.6), "무력 차이 8은 드러나되 이변도 있다 (약 2:1)");
    }

    [Test]
    public void DebateUsesIntelligence()
    {
        Assert.That(WinRate("zhuge_liang", "zhang_fei", DuelKind.Debate), Is.GreaterThan(0.8));
    }

    [Test]
    public void DuelIsDeterministicPerSeed()
    {
        var d = TestData.Data;
        var x = new CardDuel(d, new Rng(7), DuelKind.Combat, d.Officers["guan_yu"], d.Officers["hua_xiong"]);
        var y = new CardDuel(d, new Rng(7), DuelKind.Combat, d.Officers["guan_yu"], d.Officers["hua_xiong"]);
        Assert.That(x.Run(), Is.EqualTo(y.Run()));
        Assert.That(x.Log, Is.EqualTo(y.Log));
    }
}

public class BattleDuelTests
{
    [Test]
    public void RefusingADuelCostsMorale()
    {
        var b = TestData.NewBattle();
        var lu = b.Deploy("a", new[] { "lu_bu" }, "fish_scale", 5000, new HexCoord(3, 3));
        var weak = b.Deploy("b", new[] { "cao_bao" }, "fish_scale", 5000, new HexCoord(4, 3)); // 사기 특기 없음
        int morale = weak.Morale;
        Assert.That(b.WouldAccept(lu, weak), Is.False);
        Assert.That(b.ChallengeDuel(lu, weak), Is.Null);
        Assert.That(weak.Morale, Is.EqualTo(morale - 10));
        Assert.That(lu.Energy, Is.EqualTo(30));
    }

    [Test]
    public void AcceptedDuelSwingsMoraleAndCanRemoveSub()
    {
        var b = TestData.NewBattle(seed: 3);
        var lu = b.Deploy("a", new[] { "lu_bu" }, "fish_scale", 5000, new HexCoord(3, 3));
        var foe = b.Deploy("b", new[] { "liu_bei", "zhang_fei" }, "fish_scale", 5000, new HexCoord(4, 3));
        int luMorale = lu.Morale, foeMorale = foe.Morale;
        bool? won = b.ChallengeDuel(lu, foe, accept: true);
        Assert.That(won, Is.Not.Null);
        Assert.That(b.Log.Lines.Any(l => l.Contains("1합")));
        if (won == true) Assert.That(foe.Morale, Is.LessThan(foeMorale));
        else Assert.That(lu.Morale, Is.LessThan(luMorale));
    }

    /// <summary>항상 정해진 종류의 카드를 낸다 (승부를 조작하는 시험용).</summary>
    private sealed class Fixed : ICardPicker
    {
        private readonly CardType _type;
        public Fixed(CardType t) => _type = t;
        public Card Pick(CardDuel duel, Duelist me, Duelist opponent)
        {
            var c = me.Hand.FirstOrDefault(x => x.Type == _type) ?? me.Hand.First(x => x.Type != CardType.Special);
            c.Type = _type;
            return c;
        }
    }

    [Test]
    public void GarrisonDoesNotCollapseFromALostDuel()
    {
        int leaderLost = 0;
        for (ulong seed = 0; seed < 30; seed++)
        {
            var w = TestData.NewWorld(seed);
            var g = new World.Game(w, seed);
            var center = w.Map.Strongholds["beihai"].Center; // 공융 혼자: 주장이 곧 대표
            var spot = center.Neighbors().SelectMany(n => n.Neighbors()).First(h => g.Field.UnitAt(h) == null
                        && !g.Field.Structures.ContainsKey(h) && TestData.Data.Terrain[g.Field.Map.TerrainAt(h)].PassableLand);
            var lu = g.Field.Deploy("dong_zhuo", new[] { "lu_bu" }, "fish_scale", 5000, spot);
            g.SyncGarrisons();
            var gar = g.Garrisons["beihai"];
            lu.Position = center.Neighbors().First(h => g.Field.UnitAt(h) == null);
            // 도전자는 항상 이기는 카드(Third가 First를 이긴다)를 낸다
            bool? won = g.Field.ChallengeDuel(lu, gar, accept: true, new Fixed(CardType.Third), new Fixed(CardType.First));
            Assert.That(won, Is.True);
            if (g.Field.Fates.Any(f => f.OfficerId == gar.Leader.Id && f.Outcome != DefeatOutcome.Injury)) leaderLost++;
            Assert.That(gar.Status, Is.Not.EqualTo(UnitStatus.Routed), $"시드 {seed}");
        }
        Assert.That(leaderLost, Is.GreaterThan(0), "수비대 주장이 사로잡히거나 쓰러지는 경우가 한 번은 있어야 시험이 된다");
    }
}
