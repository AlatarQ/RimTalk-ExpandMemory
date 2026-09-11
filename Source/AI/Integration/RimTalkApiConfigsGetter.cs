using System.Collections.Generic;
using System.Linq;

namespace RimTalk.Memory.AI.Integration;

// 感谢 RimTalk.Memory 这个狗屎命名空间，
// 如果说记忆拓展是一座 vibecoding 出来的屎山
// 那么在记忆拓展里声明和 rimtalk 一样的类名的时候，就是在猛吃这座屎山的屎

/// <summary>
/// 专一负责将 rimtalk 的 ApiConfig 列表转换为记忆拓展的 ApiConfig（二者其实是完全一致的）列表的静态类
/// 至少会返回一个空列表
/// </summary>
public static class RimTalkApiConfigGetter
{
    public static List<ApiConfig> GetRimTalkApiConfigs()
    {
        if (Settings.Get() is not { } settings)
            return [];

        // 如果使用了简单配置，则直接返回一个包含简单配置的 ApiConfig 列表
        if (settings.UseSimpleConfig) return [ new ApiConfig
        {
            Provider = settings.SimpleProvider.Convert() ,
            ApiKey = settings.SimpleApiKey,
            CustomModelName = settings.GetCurrentModel(),
            CustomUrl = GetRimTalkEndpoint(settings.SimpleProvider),
        }];

        // 获取 rimtalk 的 ApiConfig 列表
        if (settings.CloudConfigs is not { Count: > 0 } rimTalkApiConfigs)
            return [];

        // 映射为记忆拓展的 ApiConfig 列表
        return rimTalkApiConfigs.Where(c => c is not null).Select(rimTalkApiConfig => new ApiConfig
        {
            IsEnabled = rimTalkApiConfig.IsEnabled,
            Provider = rimTalkApiConfig.Provider.Convert(),
            ApiKey = rimTalkApiConfig.ApiKey,
            // 继承 RimTalk 实际使用的端点：优先自定义 BaseUrl，否则取 RimTalk 注册表中的端点。
            // 这样即使 RimTalk 新增了记忆拓展枚举里没有的 provider（例如 OpenRouter），配置依然有效。
            CustomUrl = !string.IsNullOrWhiteSpace(rimTalkApiConfig.BaseUrl)
                ? rimTalkApiConfig.BaseUrl
                : GetRimTalkEndpoint(rimTalkApiConfig.Provider),
            CustomModelName = string.IsNullOrWhiteSpace(rimTalkApiConfig.CustomModelName)
                ? rimTalkApiConfig.SelectedModel
                : rimTalkApiConfig.CustomModelName,
        }).ToList();
    }

    /// <summary>
    /// 从 RimTalk 的 provider 注册表取端点 URL（记忆拓展自身不认识的 provider 也能拿到）
    /// </summary>
    private static string GetRimTalkEndpoint(global::RimTalk.AIProvider provider)
    {
        try
        {
            return global::RimTalk.AIProviderRegistry.GetEndpointUrl(provider);
        }
        catch
        {
            return null;
        }
    }

    private static AIProvider Convert(this global::RimTalk.AIProvider provider) => provider switch
    {
        global::RimTalk.AIProvider.Google => AIProvider.Google,
        global::RimTalk.AIProvider.OpenAI => AIProvider.OpenAI,
        global::RimTalk.AIProvider.DeepSeek => AIProvider.DeepSeek,
        global::RimTalk.AIProvider.Grok => AIProvider.Grok,
        global::RimTalk.AIProvider.GLM => AIProvider.GLM,
        global::RimTalk.AIProvider.GLMCoding => AIProvider.GLMCoding,
        global::RimTalk.AIProvider.AlibabaIntl => AIProvider.AlibabaIntl,
        global::RimTalk.AIProvider.AlibabaCN => AIProvider.AlibabaCN,
        global::RimTalk.AIProvider.OpenRouter => AIProvider.OpenRouter,
        global::RimTalk.AIProvider.Player2 => AIProvider.Player2,
        _ => AIProvider.Custom
    };
}
