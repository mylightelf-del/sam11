using UnityEngine;

namespace Sam11.Unity
{
    /// <summary>사용자 설정 (PlayerPrefs): 언어, 음량, 자동 저장, 화면.</summary>
    public static class Settings
    {
        public static string Language
        {
            get => PlayerPrefs.GetString("language", DefaultLanguage());
            set { PlayerPrefs.SetString("language", value); PlayerPrefs.Save(); }
        }

        /// <summary>자동 저장: 0 끔, 1 매월, 2 매 턴.</summary>
        public static int AutoSave
        {
            get => PlayerPrefs.GetInt("autosave", 1);
            set { PlayerPrefs.SetInt("autosave", value); PlayerPrefs.Save(); }
        }

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat("volume", 0.8f);
            set { PlayerPrefs.SetFloat("volume", Mathf.Clamp01(value)); AudioListener.volume = Mathf.Clamp01(value); PlayerPrefs.Save(); }
        }

        /// <summary>처음 켤 때: 시스템 언어가 한국어면 한국어, 중국어면 중국어(표가 있으면), 그 밖에는 영어.</summary>
        private static string DefaultLanguage() => Application.systemLanguage switch
        {
            SystemLanguage.Korean => "ko",
            SystemLanguage.Chinese or SystemLanguage.ChineseSimplified or SystemLanguage.ChineseTraditional => "zh",
            _ => "en",
        };
    }
}
