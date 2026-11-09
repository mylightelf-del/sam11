using System.Collections.Generic;
using UnityEngine;
#if STEAMWORKS_NET
using Steamworks;
#endif

namespace Sam11.Unity
{
    /// <summary>
    /// 플랫폼 연동 (Steam 또는 로컬). Steam 빌드는 Steamworks.NET 패키지를 넣고 스크립팅 정의에 STEAMWORKS_NET을 더한다.
    /// 업적 id는 data/base/achievements.json의 id와 Steamworks 관리 화면의 API 이름을 똑같이 맞춘다.
    /// 클라우드 저장은 Steam 자동 클라우드로 persistentDataPath/saves 폴더를 동기화한다 (코드 없음, docs/release 참고).
    /// </summary>
    public interface IPlatform
    {
        string Name { get; }
        void Tick();
        void Unlock(string achievementId);
        bool IsUnlocked(string achievementId);
        IEnumerable<string> UnlockedIds { get; }
        void Shutdown();
    }

    public static class Platform
    {
        private static IPlatform _current;

        public static IPlatform Current => _current ??= Create();

        private static IPlatform Create()
        {
#if STEAMWORKS_NET
            var steam = new SteamPlatform();
            if (steam.Ready) return steam;
            Debug.LogWarning("Steam을 시작하지 못해 로컬 기록을 쓴다");
#endif
            return new LocalPlatform();
        }
    }

    /// <summary>Steam 없이: 업적을 PlayerPrefs에 적는다.</summary>
    public sealed class LocalPlatform : IPlatform
    {
        private const string Key = "achievements";
        private readonly HashSet<string> _unlocked;

        public LocalPlatform()
        {
            _unlocked = new HashSet<string>(PlayerPrefs.GetString(Key, "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries));
        }

        public string Name => "Local";
        public void Tick() { }
        public bool IsUnlocked(string id) => _unlocked.Contains(id);
        public IEnumerable<string> UnlockedIds => _unlocked;

        public void Unlock(string id)
        {
            if (!_unlocked.Add(id)) return;
            PlayerPrefs.SetString(Key, string.Join(",", _unlocked));
            PlayerPrefs.Save();
        }

        public void Shutdown() { }
    }

#if STEAMWORKS_NET
    public sealed class SteamPlatform : IPlatform
    {
        public bool Ready { get; }
        private readonly HashSet<string> _unlocked = new HashSet<string>();

        public SteamPlatform()
        {
            try { Ready = SteamAPI.Init(); }
            catch (System.DllNotFoundException e) { Debug.LogError(e); Ready = false; }
        }

        public string Name => "Steam";
        public void Tick() { if (Ready) SteamAPI.RunCallbacks(); }

        public bool IsUnlocked(string id) =>
            _unlocked.Contains(id) || (Ready && SteamUserStats.GetAchievement(id, out bool done) && done);

        public IEnumerable<string> UnlockedIds => _unlocked;

        public void Unlock(string id)
        {
            if (!Ready || !_unlocked.Add(id)) return;
            SteamUserStats.SetAchievement(id);
            SteamUserStats.StoreStats();
        }

        public void Shutdown() { if (Ready) SteamAPI.Shutdown(); }
    }
#endif
}
