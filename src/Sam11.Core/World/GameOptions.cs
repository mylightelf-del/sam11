using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Sam11.Core.World
{
    /// <summary>수명: 사실(역사 사망 연도 근처), 가상(무장마다 -5~+15년 흔들림), 없음(병사하지 않음).</summary>
    public enum LifespanMode { Historical, Fictional, Off }

    /// <summary>새 게임 설정 (01·04·06·07 설계서의 ON/OFF 옵션). 저장 파일에 함께 기록한다.</summary>
    public sealed class GameOptions
    {
        /// <summary>전투 중 전사 (끄면 전사 대신 중상).</summary>
        public bool DeathInBattle = true;
        public LifespanMode Lifespan = LifespanMode.Historical;
        /// <summary>반최강세력 연합 (플레이어·AI 모두에 적용).</summary>
        public bool Coalition = true;
        /// <summary>시나리오 목표를 이루면 승리.</summary>
        public bool GoalVictory = true;
        /// <summary>플레이어가 조종하는 세력. 비어 있으면 AI끼리의 자동 대전.</summary>
        public HashSet<string> Players = new HashSet<string>();
    }

    /// <summary>시나리오 목표 (07 설계서 2.1). 조건은 이벤트 조건 문법을 그대로 쓴다.</summary>
    public sealed class FactionGoal
    {
        public string Id = "";
        public string Description = "";
        public JToken Conditions = new JObject();
    }

    public enum GameOutcome { None, Unification, Goal, Defeat }
}
