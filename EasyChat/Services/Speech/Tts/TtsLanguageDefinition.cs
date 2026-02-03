namespace EasyChat.Services.Speech.Tts;

public class TtsLanguageDefinition
{
    public string Locale { get; }
    public string Language { get; }
    public string Region { get; }
    public string EnglishName { get; }
    public string ChineseName { get; }
    public string Flag { get; }

    public TtsLanguageDefinition(string locale, string language, string region, string englishName, string chineseName, string flag)
    {
        Locale = locale;
        Language = language;
        Region = region;
        EnglishName = englishName;
        ChineseName = chineseName;
        Flag = flag;
    }
    
    public override string ToString() => EnglishName;
}
