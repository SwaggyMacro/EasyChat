using EasyChat.Services.Languages;
using EasyChat.Services.Languages.Providers;

namespace EasyChat.Tests;

[TestClass]
public sealed class Test1
{
    [TestMethod]
    public void TestMethod1()
    {
        BaiduLanguageCodeProvider provider = new();
        foreach (var code in provider.GetSupportedLanguages())
        {
            Console.WriteLine($@"{code.Id} - {code.DisplayName}");
        }
    }
}