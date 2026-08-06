namespace EasyChat.Presentation.Features.Settings;

/// <summary>
/// Field-level settings search. Empty query matches everything; otherwise any token
/// (space-separated keywords or the section header) must contain the query.
/// </summary>
public static class SettingsSearch
{
    public static bool Matches(string? query, string? keywords)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;
        if (string.IsNullOrWhiteSpace(keywords))
            return false;

        var needle = query.Trim();
        return keywords.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesAny(string? query, params string?[] bags)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;
        foreach (var bag in bags)
        {
            if (Matches(query, bag))
                return true;
        }

        return false;
    }

    // Section keyword bags include field tokens so section chrome stays visible
    // when a nested field hits.
    public const string GeneralFields =
        "language display native closing ocr asr proxy import delete model 语言 显示 母语 关闭 代理 模型 导入 删除";
    public const string TranslationFields =
        "model ai engine key baidu tencent google deepl api proxy 模型 翻译 密钥 引擎";
    public const string SelectionFields =
        "selection toolbar polish summary correct trigger engine prompt 划词 工具栏 润色 总结 纠错 触发 提示词";
    public const string TtsFields =
        "voice speech tts provider configure 语音 朗读 音色 提供商";
    public const string ScreenshotFields =
        "screenshot fixed area capture precise 截图 固定区域 精准";
    public const string ResultFields =
        "result font color delay window transparency background read aloud close 结果 字体 颜色 透明 背景 朗读 关闭 延迟";
    public const string InputFields =
        "input delivery paste type reverse transparency background font delay key 输入 投递 粘贴 透明 背景 字体 延迟 反转";
}
