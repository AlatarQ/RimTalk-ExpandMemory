using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Memory;

/// <summary>
/// 记忆条目
/// 本质是超长期储存单元，可能出现跨存档甚至跨版本的情况
/// 因此其字段应当尽可能由基本类型构成
/// </summary>
public class MemoryEntry : IExposable
{
    // 基本信息
    public long Id = 0;                 // 唯一ID，0 表示未初始化
    // 复制品的原始 Id（目前仅用于 summarizer）
    private long _originId = 0;
    public long OriginId
    {
        get => _originId == 0 ? Id : _originId;
        set => _originId = value;
    }
    // 更应当存储 AbsTick，无奈屎山已经堆起来了
    public int GameTick = -1;           // 时间戳（单位 tick）
    private int _endGameTick = -1;        // 结束时间戳，CLPA 独有
    public int EndGameTick
    {
        get => _endGameTick == -1 ? GameTick : _endGameTick;
        set => _endGameTick = value;
    }
    public string Content;              // 内容

    // 分类
    public MemoryType Type;             // 类型
    public MemoryLayer Layer;           // 层级，此字段计划在未来移除

    // 重要性和活跃度
    private float _importance = -1;       // 重要性 (0-1)
    /// <summary>
    /// 重要性，set 时自动收束到0-1
    /// </summary>
    public float Importance
    {
        get => _importance;
        set
        {
            _importance = Math.Clamp(value, 0f, 1f);
        }
    }
    public float Activity = -1;         // 活跃度 (随时间衰减)

    // 关联信息
    public string relatedPawnId;        // 相关小人ID
    public string relatedPawnName;      // 相关小人名字
    public string location;             // 地点
    public List<string> tags = new();   // 标签（中文）
    public List<string> keywords = new();   // 关键词

    // 元数据
    public bool IsUserEdited = false;   // 是否被用户编辑过
    public bool IsPinned = false;       // 是否固定（不会被删除）
    public string Notes;                // 用户备注

    /// <summary>
    /// 获取层级名称（中文）
    /// </summary>
    public string LayerName => Layer switch
    {
        MemoryLayer.Active => "RimTalk_Memory_LayerName_Active".Translate().ToString(),
        MemoryLayer.Situational => "RimTalk_Memory_LayerName_Situational".Translate().ToString(),
        MemoryLayer.EventLog => "RimTalk_Memory_LayerName_EventLog".Translate().ToString(),
        MemoryLayer.Archive => "RimTalk_Memory_LayerName_Archive".Translate().ToString(),
        _ => "RimTalk_Memory_Unknown".Translate().ToString()
    };

    /// <summary>
    /// 获取类型名称（中文）
    /// </summary>
    public string TypeName => Type switch
    {
        MemoryType.Conversation => "RimTalk_Memory_TypeName_Conversation".Translate().ToString(),
        MemoryType.Action => "RimTalk_Memory_TypeName_Action".Translate().ToString(),
        MemoryType.Summarization => "RimTalk_Memory_TypeName_Summarization".Translate().ToString(),
        MemoryType.Event => "RimTalk_Memory_TypeName_Event".Translate().ToString(),
        MemoryType.Emotion => "RimTalk_Memory_TypeName_Emotion".Translate().ToString(),
        MemoryType.Relationship => "RimTalk_Memory_TypeName_Relationship".Translate().ToString(),
        MemoryType.Internal => "RimTalk_Memory_TypeName_Internal".Translate().ToString(),
        _ => "RimTalk_Memory_Unknown".Translate().ToString()
    };

    /// <summary>
    /// 是否应当被清理
    /// </summary>
    public bool ShouldBeCleaned => Activity < 0.025f && !IsPinned;

    /// <summary>
    /// 获取记忆年龄描述
    /// 如果层级为 Archive，则直接返回年月日期
    /// 根据年龄大小返回相对描述（如“刚刚”、“一天前”）或具体日期（如“5501年素象1日”）
    /// </summary>
    public string AgeString => Layer switch
    {
        // 此乃权宜之计，未来考虑为 CLPA 额外记录“起始时间”，以时间段来描述 Age
        MemoryLayer.Archive => GenDate.DateMonthYearStringAt(GenDate.TickGameToAbs(GameTick), Vector2.zero),
        _ => (Find.TickManager?.TicksGame - GameTick) switch
        {
            null or < 0 => "RimTalk_Memory_Age_Invalid".Translate().ToString(),
            < GenDate.TicksPerHour => "RimTalk_Memory_Age_JustNow".Translate().ToString(),
            < GenDate.TicksPerHour * 6 => "RimTalk_Memory_Age_HoursAgo".Translate().ToString(),
            < GenDate.TicksPerDay => "RimTalk_Memory_Age_WithinDay".Translate().ToString(),
            < GenDate.TicksPerDay * 2 => "RimTalk_Memory_Age_Yesterday".Translate().ToString(),
            < GenDate.TicksPerDay * 3 => "RimTalk_Memory_Age_DayBeforeYesterday".Translate().ToString(),
            _ => GenDate.DateFullStringAt(GenDate.TickGameToAbs(GameTick), Vector2.zero)
        }
    };

    public MemoryEntry() { }

    public MemoryEntry(string content, MemoryType type, MemoryLayer layer, float importance = 0.5f, string relatedPawn = null)
    {
        Id = GenerateId();
        GameTick = Find.TickManager?.TicksGame ?? -1;
        Content = content;

        Type = type;
        Layer = layer;

        Activity = 1f;
        Importance = importance;
        relatedPawnName = relatedPawn;

        // 自动添加类型标签
        AddTypeTag();
    }

    // 存档读写
    // label 更应当用 PascalCase，但此处屎山已成
    public virtual void ExposeData()
    {
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            Scribe_Values.Look(ref Id, "id");
        }
#warning 等正式版迭代稳定后，将移除此处的向后兼容逻辑
        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            string serializedId = null;
            Scribe_Values.Look(ref serializedId, "id");
            Id = ParseId(serializedId);
        }

        Scribe_Values.Look(ref _originId, "OriginId", 0L);
        Scribe_Values.Look(ref GameTick, "timestamp", -1);
        Scribe_Values.Look(ref _endGameTick, "EndGameTick", 0); // -1 是初始化后的无效值，而 0 则代表根本未初始化
        Scribe_Values.Look(ref Content, "content");

        Scribe_Values.Look(ref Type, "type");
        Scribe_Values.Look(ref Layer, "layer");

#warning 等正式版迭代稳定后，将移除此处的向后兼容逻辑
        if (Scribe.mode is LoadSaveMode.LoadingVars
            && Layer is MemoryLayer.Archive
            && EndGameTick == 0)
            EndGameTick = GameTick + 15 * GenDate.TicksPerDay; // 旧存档 CPLA 默认跨度 15 天

        Scribe_Values.Look(ref _importance, "importance", -1);
        Scribe_Values.Look(ref Activity, "activity", -1);

        Scribe_Values.Look(ref relatedPawnId, "relatedPawnId");
        Scribe_Values.Look(ref relatedPawnName, "relatedPawnName");
        Scribe_Values.Look(ref location, "location");
        Scribe_Collections.Look(ref tags, "tags", LookMode.Value);
        Scribe_Collections.Look(ref keywords, "keywords", LookMode.Value);

        Scribe_Values.Look(ref IsUserEdited, "isUserEdited", false);
        Scribe_Values.Look(ref IsPinned, "isPinned", false);
        Scribe_Values.Look(ref Notes, "notes");

        // 集合型字段应当在读档后进行防空处理
        tags ??= new();
        keywords ??= new();
    }

    // 静态工具方法
    // 生成随机唯一 ID
    private static long GenerateId()
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        long id;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            id = BitConverter.ToInt64(bytes) & long.MaxValue;
        }
        while (id == 0);

        return id;
    }

    // ID 向后兼容
    private static long ParseId(string serializedId)
    {
        if (serializedId is null)
        {
            Log.Warning($"[RimTalk.Memory] 记忆 ID 为 null，已生成新 ID");
            return GenerateId();
        }

        if (long.TryParse(serializedId, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0)
            return id;

        const string legacyPrefix = "mem-";
        if (serializedId.StartsWith(legacyPrefix, StringComparison.Ordinal)
            && long.TryParse(
                serializedId.Substring(legacyPrefix.Length),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out id
                )
            && id > 0)
            return id;

        Log.Warning($"[RimTalk.Memory] 记忆 ID \"{serializedId}\" 无效，已生成新 ID");
        return GenerateId();
    }

    private void AddTypeTag()
    {
        AddTag(Type switch
        {
            MemoryType.Conversation => "对话",
            MemoryType.Action => "行动",
            MemoryType.Summarization => "总结",
            MemoryType.Event => "事件",
            MemoryType.Emotion => "情绪",
            MemoryType.Relationship => "关系",
            MemoryType.Internal => "内部上下文",
            _ => null
        });
    }

    // 标签 -> 显示用翻译键。前半段必须与 AddTypeTag 中的字面量保持一致，
    // 后半段对应 MemoryTags 中的常用标签。
    // 标签本身是存档内的匹配数据，永远保持中文原样，只有界面文本走翻译。
    // Display labels only; the stored tag itself never changes.
    private static readonly Dictionary<string, string> TagLabelKeys = new()
    {
        // 类型标签，由 AddTypeTag 自动添加
        { "对话", "RimTalk_MemoryType_Conversation" },
        { "行动", "RimTalk_MemoryType_Action" },
        { "总结", "RimTalk_MemoryType_Summarization" },
        { "事件", "RimTalk_MemoryType_Event" },
        { "情绪", "RimTalk_MemoryType_Emotion" },
        { "关系", "RimTalk_MemoryType_Relationship" },
        { "内部上下文", "RimTalk_MemoryType_Internal" },

        // 常用标签，见 MemoryTags
        { MemoryTags.开心, "RimTalk_MemoryTag_Happy" },
        { MemoryTags.悲伤, "RimTalk_MemoryTag_Sad" },
        { MemoryTags.愤怒, "RimTalk_MemoryTag_Angry" },
        { MemoryTags.焦虑, "RimTalk_MemoryTag_Anxious" },
        { MemoryTags.平静, "RimTalk_MemoryTag_Calm" },
        { MemoryTags.战斗, "RimTalk_MemoryTag_Combat" },
        { MemoryTags.袭击, "RimTalk_MemoryTag_Raid" },
        { MemoryTags.受伤, "RimTalk_MemoryTag_Injured" },
        { MemoryTags.死亡, "RimTalk_MemoryTag_Death" },
        { MemoryTags.完成任务, "RimTalk_MemoryTag_TaskComplete" },
        { MemoryTags.闲聊, "RimTalk_MemoryTag_SmallTalk" },
        { MemoryTags.深谈, "RimTalk_MemoryTag_DeepTalk" },
        { MemoryTags.争吵, "RimTalk_MemoryTag_Argument" },
        { MemoryTags.友好, "RimTalk_MemoryTag_Friendly" },
        { MemoryTags.敌对, "RimTalk_MemoryTag_Hostile" },
        { MemoryTags.烹饪, "RimTalk_MemoryTag_Cooking" },
        { MemoryTags.建造, "RimTalk_MemoryTag_Construction" },
        { MemoryTags.种植, "RimTalk_MemoryTag_Growing" },
        { MemoryTags.采矿, "RimTalk_MemoryTag_Mining" },
        { MemoryTags.研究, "RimTalk_MemoryTag_Research" },
        { MemoryTags.医疗, "RimTalk_MemoryTag_Medical" },
        { MemoryTags.重要, "RimTalk_MemoryTag_Important" },
        { MemoryTags.紧急, "RimTalk_MemoryTag_Urgent" },
        { MemoryTags.用户编辑, "RimTalk_MemoryTag_UserEdited" },
    };

    /// <summary>
    /// 取标签的显示文本：已知标签走翻译，用户自定义标签原样返回
    /// </summary>
    public static string GetTagDisplayLabel(string tag) =>
        tag is not null && TagLabelKeys.TryGetValue(tag, out var key)
            ? key.Translate().ToString()
            : tag;

    /// <summary>
    /// 添加标签（中文）
    /// </summary>
    public void AddTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tags.Contains(tag)) return;

        tags.Add(tag);
    }

    /// <summary>
    /// 移除标签
    /// </summary>
    public void RemoveTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;

        tags.Remove(tag);
    }

    /// <summary>
    /// 添加关键词
    /// </summary>
    public void AddKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword) || keywords.Contains(keyword)) return;

        keywords.Add(keyword);
    }

    /// <summary>
    /// 移除关键词
    /// </summary>
    public void RemoveKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return;

        keywords.Remove(keyword);
    }

    /// <summary>
    /// 衰减活跃度
    /// </summary>
    public void Decay(float rate)
    {
        if (IsPinned) return; // 固定的记忆不衰减

        Activity *= (1f - rate);
    }

    /// <summary>
    /// 计算检索分数（用于相关性排序）
    /// </summary>
    public float CalculateRetrievalScore(string context, List<string> contextKeywords)
    {
        float score = 0f;

        // 时间因子（越新越好）
        float timeFactor = (float)Math.Exp(-(float)(Find.TickManager.TicksGame - GameTick) / GenDate.TicksPerDay);
        score += timeFactor * 0.3f;

        // 重要性因子
        score += Importance * 0.3f;

        // 活跃度因子
        score += Activity * 0.2f;

        // 相关性因子（关键词匹配）
        if (contextKeywords != null && contextKeywords.Count > 0)
        {
            int matchCount = 0;
            foreach (var kw in keywords)
            {
                if (contextKeywords.Contains(kw)) matchCount++;
            }
            float relevance = (float)matchCount / Math.Max(keywords.Count, contextKeywords.Count);
            score += relevance * 0.2f;
        }

        // 固定/编辑过的记忆优先级更高
        if (IsPinned) score += 0.3f;
        if (IsUserEdited) score += 0.2f;

        return score;
    }

    // 私有化
    public virtual MemoryEntry Privatize() => this;
}

/// <summary>
/// 记忆查询参数
/// </summary>
public class MemoryQuery
{
    public MemoryLayer? layer;
    public MemoryType? type;
    public string relatedPawn;
    public List<string> tags;
    public List<string> keywords;
    public int maxCount = 10;
    public bool includeContext = true;

    public MemoryQuery()
    {
        tags = new List<string>();
        keywords = new List<string>();
    }
}
