using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace RimTalk.Memory
{
    /// <summary>
    /// Pawn 匹配关键词词表 / pawn matching keyword vocabulary
    ///
    /// 旧版本把中文关键词硬编码进匹配文本，存档里的常识标签也都是中文，
    /// 所以中文词必须保留，否则老条目立刻失配。
    /// 当前语言的等价词取自 Keyed 翻译，两者一起进入匹配文本，
    /// 于是中文标签和本地语言的标签都能命中（匹配是子串比较，重复无害）。
    /// </summary>
    internal static class KeywordVocabulary
    {
        private static readonly char[] Separators = { ' ', '\t', ',', '，', '、' };

        /// <summary>
        /// 当前语言的关键词（空格分隔）；没有翻译时返回空数组
        /// </summary>
        internal static string[] Localized(string key)
        {
            string text = key.Translate().ToString();

            // 缺失的 key 会原样返回，此时没有可用的本地化词
            if (string.IsNullOrEmpty(text) || text == key)
                return new string[0];

            return text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// 追加中文关键词及当前语言的等价词
        /// </summary>
        internal static void Append(StringBuilder sb, string legacyChinese, string key)
        {
            sb.Append(legacyChinese);
            sb.Append(' ');

            foreach (string word in Localized(key))
            {
                if (legacyChinese.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                sb.Append(word);
                sb.Append(' ');
            }
        }

        /// <summary>
        /// 把中文关键词及当前语言的等价词逐个交给 add 回调
        /// </summary>
        internal static void ForEach(string legacyChinese, string key, Action<string> add)
        {
            foreach (string word in legacyChinese.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
                add(word);

            foreach (string word in Localized(key))
            {
                if (legacyChinese.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                add(word);
            }
        }

        /// <summary>
        /// 技能等级标记："技能名+后缀"，中文直接拼接，其它语言由翻译决定格式
        /// </summary>
        internal static void ForEachSkillLevel(string skillLabel, string legacySuffix, string key, Action<string> add)
        {
            string legacy = skillLabel + legacySuffix;
            add(legacy);

            string localized = key.Translate(skillLabel).ToString();
            if (!string.IsNullOrEmpty(localized) && localized != legacy && localized != key)
                add(localized);
        }

        /// <summary>
        /// 中文关键词或当前语言的等价词是否出现在 text 中
        /// </summary>
        internal static bool ContainsAny(string text, string[] legacyChinese, string key)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (string word in legacyChinese)
                if (text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

            foreach (string word in Localized(key))
                if (text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

            return false;
        }

        /// <summary>
        /// 中文关键词 + 当前语言的等价词，合并成一个数组
        /// </summary>
        internal static string[] Merge(string[] legacyChinese, string key)
        {
            var merged = new List<string>(legacyChinese);

            foreach (string word in Localized(key))
                if (!merged.Contains(word))
                    merged.Add(word);

            return merged.ToArray();
        }

        /// <summary>
        /// <see cref="ForEachSkillLevel"/> 的 StringBuilder 版本
        /// </summary>
        internal static void AppendSkillLevel(StringBuilder sb, string skillLabel, string legacySuffix, string key)
        {
            ForEachSkillLevel(skillLabel, legacySuffix, key, word =>
            {
                sb.Append(word);
                sb.Append(' ');
            });
        }
    }
}
