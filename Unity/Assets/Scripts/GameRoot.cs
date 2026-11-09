using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sam11.Core.Data;
using Sam11.Core.Play;
using Sam11.Core.World;
using UnityEngine;

namespace Sam11.Unity
{
    /// <summary>
    /// 한 판의 뿌리: 데이터를 읽고, PlayerSession을 들고, 화면 요소들에게 바뀐 상태를 알린다.
    /// 화면은 모든 행동을 Execute(명령)로만 한다 (콘솔판과 같은 통로라 저장·불러오기가 그대로 맞는다).
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        public string DataDir => Path.Combine(Application.streamingAssetsPath, "data");
        public string ModsDir => Path.Combine(Application.streamingAssetsPath, "Mods");
        public string SaveDir => Path.Combine(Application.persistentDataPath, "saves");

        public GameData Data { get; private set; }
        public WorldMap Map { get; private set; }
        public PlayerSession Session { get; private set; }
        public List<ModInfo> AvailableMods { get; private set; } = new List<ModInfo>();

        /// <summary>상태가 바뀌었을 때 (명령 실행, 차례 진행, 불러오기).</summary>
        public event Action Changed;
        /// <summary>새 판을 시작하거나 불러왔을 때 (맵을 다시 그린다).</summary>
        public event Action SessionStarted;

        public MapView MapView { get; private set; }
        public Selection Selection { get; private set; }
        public string LastMessage { get; private set; } = "";
        public readonly List<string> News = new List<string>();
        private int _logShown;

        private void Awake()
        {
            Instance = this;
            if (!Directory.Exists(DataDir))
            {
                LastMessage = $"데이터가 없음: {DataDir}\ntools/unity/sync.sh를 먼저 실행하세요.";
                Debug.LogError(LastMessage);
                gameObject.AddComponent<TitleScreen>();
                return;
            }
            AvailableMods = ModLoader.Discover(Directory.Exists(ModsDir) ? ModsDir : null);
            LoadData(new List<string>());
            gameObject.AddComponent<TitleScreen>();
        }

        /// <summary>켤 모드를 정해 데이터를 다시 읽는다.</summary>
        public void LoadData(IEnumerable<string> modIds)
        {
            var mods = ModLoader.Order(AvailableMods, modIds);
            Data = GameData.Load(DataDir, mods);
            Map ??= WorldMap.Load(DataDir);
        }

        public void StartNew(string scenario, GameOptions options, ulong seed)
        {
            Session = PlayerSession.NewGame(Data, Map, scenario, seed, options);
            OnSessionStarted();
        }

        public void LoadGame(string path)
        {
            var save = JObject.Parse(File.ReadAllText(path));
            LoadData((save["mods"] as JArray)?.Values<string>() ?? Enumerable.Empty<string>());
            Session = PlayerSession.Load(Data, Map, save);
            OnSessionStarted();
        }

        public string SaveGame(string name = null)
        {
            Directory.CreateDirectory(SaveDir);
            string path = Path.Combine(SaveDir, (name ?? $"{Session.Scenario}_{Session.Faction}_{DateTime.Now:yyyyMMdd_HHmmss}") + ".json");
            File.WriteAllText(path, Session.Save().ToString());
            LastMessage = $"저장했다: {Path.GetFileName(path)}";
            return path;
        }

        public IEnumerable<string> SaveFiles() =>
            Directory.Exists(SaveDir) ? Directory.GetFiles(SaveDir, "*.json").OrderByDescending(File.GetLastWriteTime) : Enumerable.Empty<string>();

        private void OnSessionStarted()
        {
            if (MapView == null)
            {
                MapView = new GameObject("Map").AddComponent<MapView>();
                new GameObject("Cities").AddComponent<CityLayer>();
                new GameObject("Units").AddComponent<UnitLayer>();
                Selection = gameObject.AddComponent<Selection>();
                gameObject.AddComponent<Hud>();
                CameraRig.Ensure();
            }
            var title = GetComponent<TitleScreen>();
            if (title != null) title.enabled = false;
            _logShown = Session.World.Log.Count;
            News.Clear();
            SessionStarted?.Invoke();
            Changed?.Invoke();
            var capital = Session.World.Factions[Session.Faction ?? Session.Options.Players.First()].Capital;
            CameraRig.Instance.Focus(Session.World.Cities[capital].Placement.Center);
        }

        /// <summary>명령을 실행하고 결과를 알린다.</summary>
        public CommandResult Execute(JObject cmd)
        {
            var r = Session.Execute(cmd);
            LastMessage = r.Ok ? r.Message : "✗ " + r.Message;
            CollectNews();
            Changed?.Invoke();
            return r;
        }

        public CommandResult Execute(string type, object args = null)
        {
            var o = args == null ? new JObject() : JObject.FromObject(args);
            o["type"] = type;
            return Execute(o);
        }

        private void CollectNews()
        {
            var w = Session.World;
            string me = w.NameOf(Session.Faction ?? "");
            foreach (var l in w.Log.Skip(_logShown))
                if (l.Contains("★") || l.Contains("◆") || l.Contains("함락") || l.Contains(me) || l.Contains("외교"))
                    News.Add(l);
            if (News.Count > 200) News.RemoveRange(0, News.Count - 200);
            _logShown = w.Log.Count;
        }
    }
}
