namespace Sam11.Core.Common
{
    /// <summary>받침에 맞는 조사를 붙인다 ("유비 부대가", "관우가", "장료가").</summary>
    public static class Korean
    {
        public static bool HasBatchim(string word)
        {
            if (string.IsNullOrEmpty(word))
                return false;
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3)
                return false;
            return (c - 0xAC00) % 28 != 0;
        }

        /// <summary>이/가</summary>
        public static string IGa(string word) => word + (HasBatchim(word) ? "이" : "가");

        /// <summary>은/는</summary>
        public static string EunNeun(string word) => word + (HasBatchim(word) ? "은" : "는");

        /// <summary>을/를</summary>
        public static string EulReul(string word) => word + (HasBatchim(word) ? "을" : "를");
    }
}
