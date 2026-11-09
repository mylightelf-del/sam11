using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sam11.Core.AI;
using Sam11.Core.Battle;
using Sam11.Core.Common;
using Sam11.Core.Data;
using Sam11.Core.World;

namespace Sam11.Core.Play
{
    /// <summary>명령 한 건의 결과.</summary>
    public sealed class CommandResult
    {
        public bool Ok;
        public string Message = "";
        public override string ToString() => (Ok ? "" : "✗ ") + Message;
    }

    /// <summary>
    /// 플레이어 한 판 (08 설계서의 M4 수직 단면). UI는 이 클래스로만 게임을 움직인다.
    /// 모든 명령은 JSON(JObject)으로 기록되고, 저장 파일은 시나리오·시드·옵션 + 명령 기록이다.
    /// 코어가 결정론적이므로 불러오기는 같은 명령을 다시 실행해 같은 상태를 만든다.
    /// </summary>
    public sealed class PlayerSession
    {
        public const int SaveFormat = 1;

        public GameData Data { get; }
        public string Scenario { get; }
        public ulong Seed { get; }
        public Game Game { get; }
        public WorldState World => Game.World;
        public GameOptions Options => Game.World.Options;
        public List<JObject> Commands { get; } = new List<JObject>();

        /// <summary>이번 차례에 이미 이동한 부대 (부대는 차례마다 한 번 이동하고 한 번 행동한다).</summary>
        private readonly HashSet<int> _moved = new HashSet<int>();
        private readonly Dictionary<string, AiController> _delegates = new Dictionary<string, AiController>();
        private readonly SortedDictionary<string, AiController> _corps = new SortedDictionary<string, AiController>(StringComparer.Ordinal);

        /// <summary>군단 (이름 → 맡은 도시). 군단은 플레이어가 차례를 마칠 때 맡은 도시만 움직인다.</summary>
        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Corps =>
            _corps.ToDictionary(kv => kv.Key, kv => (IReadOnlyCollection<string>)kv.Value.Scope!);

        private PlayerSession(GameData data, WorldMap map, string scenario, ulong seed, GameOptions options)
        {
            Data = data;
            Scenario = scenario;
            Seed = seed;
            var w = ScenarioLoader.Load(data, map, scenario, seed);
            w.Options = options;
            Game = new Game(w, seed);
            foreach (var f in w.Factions.Keys.Where(f => !options.Players.Contains(f)))
                Game.Controllers[f] = new AiController(AiConfig.For(options.Difficulty));
            Game.ControllerFactory = f => new AiController(AiConfig.For(options.Difficulty));
        }

        /// <summary>새 게임을 시작하고 첫 플레이어 차례까지 진행한다.</summary>
        public static PlayerSession NewGame(GameData data, WorldMap map, string scenario, ulong seed, GameOptions options)
        {
            if (options.Players.Count == 0) throw new InvalidOperationException("플레이어 세력을 하나 이상 골라야 함");
            var s = new PlayerSession(data, map, scenario, seed, options);
            foreach (var p in options.Players)
                if (!s.World.Factions.ContainsKey(p)) throw new InvalidOperationException($"시나리오에 없는 세력: {p}");
            s.Game.Advance();
            return s;
        }

        /// <summary>지금 명령을 내리는 세력 (게임이 끝났으면 null).</summary>
        public string? Faction => Game.CurrentPlayer;
        public bool IsOver => Game.IsOver;

        // ------------------------------------------------------------------ 실행

        /// <summary>명령을 실행하고 기록한다. 실패한 명령도 기록한다 (실패에도 난수가 쓰일 수 있어 다시 실행해야 같아진다).</summary>
        public CommandResult Execute(JObject cmd)
        {
            var copy = (JObject)cmd.DeepClone();
            Commands.Add(copy);
            return Run(copy);
        }

        private CommandResult Run(JObject cmd)
        {
            string type = cmd.Value<string>("type") ?? "";
            if (Game.IsOver) return Fail("게임이 끝났음");
            if (Faction == null) return Fail("플레이어 차례가 아님");
            int logStart = World.Log.Count;
            try
            {
                string msg = Dispatch(type, cmd);
                Game.Resolve();
                return new CommandResult { Ok = true, Message = msg };
            }
            catch (InvalidOperationException e)
            {
                return Fail(e.Message);
            }
            catch (KeyNotFoundException)
            {
                return Fail("없는 대상 (아이디를 확인)");
            }
            catch (NullReferenceException)
            {
                return Fail($"명령 인자가 모자람: {cmd.ToString(Newtonsoft.Json.Formatting.None)}");
            }
        }

        private static CommandResult Fail(string why) => new CommandResult { Ok = false, Message = why };

        private string Dispatch(string type, JObject c)
        {
            var w = World;
            var b = Game.Field;
            string me = Faction!;
            string S(string k) => c.Value<string>(k) ?? throw new InvalidOperationException($"'{k}' 인자가 필요");
            int I(string k) => c.Value<int?>(k) ?? 0;
            OfficerState Mine(string k)
            {
                var o = w.Officers[S(k)];
                if (o.Faction != me || o.Status != OfficerStatus.Active) throw new InvalidOperationException($"{Korean.EunNeun(o.ToString())} 우리 세력 무장이 아님");
                return o;
            }
            Unit MyUnit()
            {
                int id = I("unit");
                var u = b.Units.FirstOrDefault(x => x.Id == id && x.IsAlive) ?? throw new InvalidOperationException($"{id}번 부대가 없음");
                if (u.Faction != me || u.IsGarrison) throw new InvalidOperationException($"{Korean.EunNeun(u.Label)} 우리가 움직일 부대가 아님");
                return u;
            }
            Unit AnyUnit(string k)
            {
                int id = I(k);
                return b.Units.FirstOrDefault(x => x.Id == id && x.IsAlive) ?? throw new InvalidOperationException($"{id}번 부대가 없음");
            }
            HexCoord Hex() => new HexCoord(I("q"), I("r"));

            switch (type)
            {
                // ---- 내정
                case "build": Mine("officer"); return Domestic.Build(w, S("officer"), S("city"), I("slot"), S("facility"));
                case "recruit": Mine("officer"); return Domestic.Recruit(w, S("officer"), S("city"));
                case "train": Mine("officer"); return Domestic.Train(w, S("officer"), S("city"));
                case "patrol": Mine("officer"); return Domestic.Patrol(w, S("officer"), S("city"));
                case "charity": Mine("officer"); return Domestic.Charity(w, S("officer"), S("city"));
                case "produce": Mine("officer"); return Domestic.Produce(w, S("officer"), S("city"), S("equipment"));
                case "buy_food": Mine("officer"); return Domestic.BuyFood(w, S("officer"), S("city"), I("gold"));
                case "sell_food": Mine("officer"); return Domestic.SellFood(w, S("officer"), S("city"), I("food"));
                case "move_officer": Mine("officer"); return Domestic.MoveOfficer(w, S("officer"), S("from"), S("to"));
                case "transport": Mine("officer"); return Domestic.Transport(w, S("officer"), S("from"), S("to"), I("gold"), I("food"), I("troops"));
                case "employ": Mine("officer"); return Domestic.Employ(w, S("officer"), S("city"), S("target"));
                case "appoint":
                    Mine("officer");
                    Promotion.Appoint(w, S("officer"), S("rank"));
                    return $"{w.Officers[S("officer")]} → {w.Data.Ranks[S("rank")].Name.Ko}";
                case "tax":
                {
                    string level = S("level");
                    if (!w.Data.Rules.Domestic.Tax.ContainsKey(level)) throw new InvalidOperationException($"없는 세율: {level}");
                    w.Factions[me].Tax = level;
                    return $"세율을 {level}(으)로 정했다";
                }

                // ---- 군사
                case "sortie":
                {
                    var ids = c["officers"]!.Values<string>().Select(x => x!).ToList();
                    foreach (var id in ids) if (w.Officers[id].Faction != me) throw new InvalidOperationException("우리 무장이 아님");
                    var city = w.Cities[S("city")];
                    if (city.Owner != me) throw new InvalidOperationException($"{Korean.EunNeun(city.ToString())} 우리 거점이 아님");
                    var members = ids.Select(id => Treasures.Effective(w, id)).ToList();
                    string? why = Battle.Battle.CanDeploy(w.Data, members, S("formation"), I("troops"), city.Equipment);
                    if (why != null) throw new InvalidOperationException(why);
                    var u = SiegeSetup.Sortie(w, b, city.Id, ids, S("formation"), I("troops"), I("food"));
                    return $"{u.Id}번 부대 {u.Label} 출진 {u.Position}";
                }
                case "move":
                {
                    var u = MyUnit();
                    if (_moved.Contains(u.Id)) throw new InvalidOperationException($"{Korean.EunNeun(u.Label)} 이번 차례에 이미 이동함");
                    b.Move(u, Hex());
                    _moved.Add(u.Id);
                    return $"{u.Label} → {u.Position}";
                }
                case "attack":
                {
                    var u = MyUnit();
                    var r = b.Attack(u, AnyUnit("target"));
                    return $"{u.Label} 공격: 피해 {r.Damage}, 반격 {r.CounterDamage}" + (r.TargetDestroyed ? ", 격파!" : "");
                }
                case "tactic":
                {
                    var u = MyUnit();
                    var r = b.UseTactic(u, S("tactic"), AnyUnit("target"));
                    return $"{u.Label} {w.Data.Tactics[S("tactic")].Name.Ko}: 피해 {r.Damage}" + (r.TargetDestroyed ? ", 격파!" : "");
                }
                case "strike":
                {
                    var u = MyUnit();
                    string? tactic = c.Value<string>("tactic");
                    int dmg = tactic != null ? b.UseTacticOnStructure(u, tactic, Hex()) : b.AttackStructure(u, Hex());
                    return $"{u.Label} 성벽·성문 공격: 내구 -{dmg}";
                }
                case "stratagem":
                {
                    var u = MyUnit();
                    Unit? target = c["target"] != null ? AnyUnit("target") : null;
                    bool ok = Stratagems.Use(b, u, S("kind"), target);
                    return $"{u.Label} {Stratagems.NameOf(S("kind"))} {(ok ? "성공" : "실패")}";
                }
                case "duel":
                {
                    var u = MyUnit();
                    var r = b.ChallengeDuel(u, AnyUnit("target"));
                    return r == null ? "상대가 일기토를 거절했다" : r.Value ? "일기토 승리!" : "일기토 패배";
                }
                case "withdraw":
                {
                    var u = MyUnit();
                    SiegeSetup.Withdraw(w, b, u, S("city"));
                    return $"{u.Label} 귀환";
                }

                // ---- 외교·계략·보물·포로
                case "propose":
                {
                    Mine("envoy");
                    var p = new Proposal
                    {
                        Type = Enum.TryParse<DiploType>(S("kind"), true, out var t) ? t : throw new InvalidOperationException($"없는 외교: {S("kind")}"),
                        From = me, To = S("to"), Envoy = S("envoy"), Gold = I("gold"), Captive = c.Value<string>("captive"),
                    };
                    return $"{Diplomacy.Describe(p)} 제안: {(Diplomacy.Propose(Game, p) ? "성사" : "거절")}";
                }
                case "respond":
                {
                    var mine = w.PendingProposals.Where(x => x.To == me).ToList();
                    int i = I("index");
                    if (i < 0 || i >= mine.Count) throw new InvalidOperationException("없는 제안 번호");
                    bool ok = Diplomacy.Respond(Game, mine[i], c.Value<bool>("accept"));
                    return ok ? "제안을 받아들였다" : "제안을 거절했다";
                }
                case "break_pact":
                    Diplomacy.Break(Game, me, S("other"));
                    return $"{w.NameOf(S("other"))}와의 약속을 깼다";
                case "mission":
                {
                    Mine("agent");
                    var m = Missions.Start(w, S("agent"), S("kind"), c.Value<string>("city"), c.Value<string>("target"));
                    return $"{Missions.NameOf(m.Kind)} 공작 시작 ({m.TurnsLeft}턴)";
                }
                case "bestow":
                    Mine("officer");
                    Treasures.Bestow(w, S("treasure"), S("officer"));
                    return $"{w.Data.Treasures[S("treasure")].Name.Ko} 하사";
                case "release":
                case "execute":
                {
                    var o = w.Officers[S("officer")];
                    if (o.Status != OfficerStatus.Captive || o.Faction != me) throw new InvalidOperationException("우리가 잡은 포로가 아님");
                    var r = w.Data.Rules.Captives;
                    string? home = o.PreviousFaction;
                    if (type == "release")
                    {
                        o.Status = OfficerStatus.Free;
                        o.Faction = null;
                        if (home != null && w.Factions.ContainsKey(home)) w.ChangeFriendship(me, home, r.ReleaseFriendship);
                        return $"포로 {Korean.EulReul(o.ToString())} 풀어 주었다";
                    }
                    if (!Options.DeathInBattle) throw new InvalidOperationException("전사 옵션이 꺼져 있으면 처단할 수 없음");
                    o.Status = OfficerStatus.Dead;
                    o.Location = "";
                    if (home != null && w.Factions.ContainsKey(home)) w.ChangeFriendship(me, home, r.ExecuteFriendship);
                    w.ChangeTrust(me, r.ExecuteTrust);
                    return $"포로 {Korean.EulReul(o.ToString())} 처단했다";
                }
                case "event_choice":
                {
                    var pe = w.PendingEvents.FirstOrDefault(x => x.Faction == me && x.Event == S("event"))
                             ?? throw new InvalidOperationException("기다리는 이벤트가 아님");
                    Game.ResolvePendingEvent(pe, S("choice"));
                    return "선택했다";
                }

                // ---- 차례
                case "delegate":
                {
                    // 위임: 이번 차례를 AI 군사(軍師)에게 맡긴다 (9식 군단 위임의 간단한 형태)
                    if (!_delegates.TryGetValue(me, out var ai)) _delegates[me] = ai = new AiController(AiConfig.For(Difficulty.Normal));
                    foreach (var pe in w.PendingEvents.Where(x => x.Faction == me).ToList())
                        Game.ResolvePendingEvent(pe, ai.Choose(Game, w.Events.First(e => e.Id == pe.Event), me).Id);
                    foreach (var pp in w.PendingProposals.Where(x => x.To == me).ToList())
                        Diplomacy.Respond(Game, pp, ai.Wants(Game, pp));
                    ai.TakeTurn(Game, me);
                    return "이번 차례를 위임했다";
                }
                case "corps_set":
                {
                    // 군단 만들기·바꾸기: 도시는 한 군단에만 속한다
                    string name = S("corps");
                    var cities = c["cities"]!.Values<string>().Select(x => x!).ToList();
                    if (cities.Count == 0) throw new InvalidOperationException("맡길 도시가 없음");
                    foreach (var id in cities)
                        if (w.Cities[id].Owner != me) throw new InvalidOperationException($"{Korean.EunNeun(w.Cities[id].ToString())} 우리 거점이 아님");
                    foreach (var other in _corps.Values) other.Scope!.ExceptWith(cities);
                    if (!_corps.TryGetValue(name, out var ai))
                        _corps[name] = ai = new AiController(AiConfig.For(Difficulty.Normal)) { Scope = new HashSet<string>() };
                    ai.Scope!.Clear();
                    ai.Scope.UnionWith(cities);
                    foreach (var empty in _corps.Where(kv => kv.Value.Scope!.Count == 0).Select(kv => kv.Key).ToList()) _corps.Remove(empty);
                    return $"{name} 군단: {string.Join(", ", cities.Select(id => w.Cities[id].ToString()))}";
                }
                case "corps_remove":
                    if (!_corps.Remove(S("corps"))) throw new InvalidOperationException($"없는 군단: {S("corps")}");
                    return $"{S("corps")} 군단을 해산했다";
                case "end_turn":
                    EndTurn();
                    return "차례를 마쳤다";
                default:
                    throw new InvalidOperationException($"모르는 명령: {type}");
            }
        }

        private void EndTurn()
        {
            // 군단이 맡은 도시를 움직인다 (군단이 도시를 모두 잃으면 해산)
            foreach (var kv in _corps.ToList())
            {
                kv.Value.TakeTurn(Game, Faction!);
                Game.Resolve();
                if (kv.Value.Scope!.Count == 0) _corps.Remove(kv.Key);
            }
            Game.EndPlayerTurn();
            _moved.Clear();
            Game.Advance();
            // 플레이어 세력이 망해 차례가 없으면 끝날 때까지 진행
            while (!Game.IsOver && Game.CurrentPlayer == null) Game.Advance();
        }

        // ------------------------------------------------------------------ 저장·불러오기

        public JObject Save() => new JObject
        {
            ["format"] = SaveFormat,
            ["scenario"] = Scenario,
            ["seed"] = Seed.ToString(),
            ["options"] = new JObject
            {
                ["players"] = new JArray(Options.Players.OrderBy(p => p)),
                ["death_in_battle"] = Options.DeathInBattle,
                ["lifespan"] = Options.Lifespan.ToString(),
                ["coalition"] = Options.Coalition,
                ["goal_victory"] = Options.GoalVictory,
                ["difficulty"] = Options.Difficulty.ToString(),
            },
            ["mods"] = new JArray(Data.Mods),
            ["date"] = World.Date.ToString(),
            ["commands"] = new JArray(Commands),
            ["digest"] = Digest(),
        };

        /// <summary>저장 파일을 불러온다. 명령을 다시 실행한 뒤 요약값이 저장 때와 다르면 예외 (버전·데이터가 달라진 경우).</summary>
        public static PlayerSession Load(GameData data, WorldMap map, JObject save)
        {
            if (save.Value<int>("format") != SaveFormat) throw new InvalidOperationException("지원하지 않는 저장 형식");
            var savedMods = (save["mods"] as JArray)?.Values<string>().Select(x => x!).ToList() ?? new List<string>();
            if (!savedMods.SequenceEqual(data.Mods))
                throw new InvalidOperationException($"저장할 때와 모드가 다름: 저장 [{string.Join(", ", savedMods)}] / 지금 [{string.Join(", ", data.Mods)}]");
            var o = save["options"]!;
            var options = new GameOptions
            {
                Players = new HashSet<string>(o["players"]!.Values<string>().Select(x => x!)),
                DeathInBattle = o.Value<bool>("death_in_battle"),
                Lifespan = Enum.Parse<LifespanMode>(o.Value<string>("lifespan")!),
                Coalition = o.Value<bool>("coalition"),
                GoalVictory = o.Value<bool>("goal_victory"),
                Difficulty = Enum.Parse<Difficulty>(o.Value<string>("difficulty")!),
            };
            var s = NewGame(data, map, save.Value<string>("scenario")!, ulong.Parse(save.Value<string>("seed")!), options);
            foreach (JObject cmd in save["commands"]!) s.Execute(cmd);
            string expected = save.Value<string>("digest") ?? "";
            if (expected != "" && s.Digest() != expected)
                throw new InvalidOperationException("저장 파일과 상태가 맞지 않음 (게임 버전이나 데이터·모드가 바뀌었을 수 있음)");
            return s;
        }

        /// <summary>상태 요약 (저장·불러오기 검증용).</summary>
        public string Digest()
        {
            var w = World;
            var sb = new System.Text.StringBuilder();
            sb.Append(w.Date).Append('|').Append(Game.TurnCount).Append('|').Append(w.Rng.StateDigest).Append('|').Append(Game.Field.Rng.StateDigest);
            foreach (var c in w.Cities.Values.OrderBy(x => x.Id))
                sb.Append(c.Id).Append(c.Owner).Append(c.Troops).Append(',').Append(c.Gold).Append(',').Append(c.Food).Append(',').Append(c.Population);
            foreach (var o in w.Officers.Values.OrderBy(x => x.Id))
                sb.Append(o.Id).Append((int)o.Status).Append(o.Faction).Append(o.Location).Append(o.Loyalty);
            foreach (var u in Game.Field.Units.Where(u => u.IsAlive).OrderBy(u => u.Id))
                sb.Append(u.Id).Append(u.Position).Append(u.Troops).Append(',').Append(u.Morale);
            return Rng.StableHash(sb.ToString()).ToString("x8") + Rng.StableHash(sb.ToString().Length + "|" + sb).ToString("x8");
        }
    }
}
