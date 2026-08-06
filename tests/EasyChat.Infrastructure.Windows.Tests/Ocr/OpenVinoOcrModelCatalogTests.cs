using EasyChat.Contracts.Ocr;
using EasyChat.Infrastructure.Windows.Ocr;

namespace EasyChat.Infrastructure.Windows.Tests.Ocr;

[TestClass]
public sealed class OpenVinoOcrModelCatalogTests
{
    [TestMethod]
    public void Catalog_ContainsEightPackagesAndEverySupportedLanguageExactlyOnce()
    {
        Assert.HasCount(8, OpenVinoOcrModelCatalog.Packages);
        Assert.HasCount(87, OcrLanguages.Supported);
        Assert.HasCount(87, OcrLanguages.Supported.Select(language => language.Id).Distinct().ToArray());

        var packageLanguages = OpenVinoOcrModelCatalog.Packages
            .SelectMany(package => package.SupportedLanguages)
            .ToArray();
        Assert.HasCount(87, packageLanguages);
        Assert.HasCount(87, packageLanguages.Select(language => language.Id).Distinct().ToArray());
    }

    [TestMethod]
    public void UniversalPackage_ContainsTheFiftyV6Languages()
    {
        var package = OpenVinoOcrModelCatalog.Packages.Single(candidate =>
            candidate.Id == OpenVinoOcrModelCatalog.UniversalV6SmallId);

        Assert.HasCount(50, package.SupportedLanguages);
        foreach (var id in new[] { "zh-Hans", "zh-Hant", "en", "ja", "ku", "sr-Latn", "qu" })
            Assert.IsTrue(package.SupportedLanguages.Any(language => language.Id == id), id);
    }

    [TestMethod]
    public void EveryLanguage_ResolvesToItsDeclaringPackageByStableId()
    {
        foreach (var package in OpenVinoOcrModelCatalog.Packages)
        {
            foreach (var language in package.SupportedLanguages)
            {
                var copy = new OcrLanguage(language.Id, "changed display metadata");
                var resolved = OpenVinoOcrModelCatalog.ResolveLanguage(copy);
                Assert.AreEqual(package.Id, resolved.Package.Package.Id, language.Id);
                Assert.AreEqual(language.Id, resolved.Language.Id, language.Id);
            }
        }
    }

    [TestMethod]
    public void LegacySerbianId_ResolvesToCyrillicV3()
    {
        var resolved = OpenVinoOcrModelCatalog.ResolveLanguage(new OcrLanguage("sr", "Serbian"));

        Assert.AreEqual(OpenVinoOcrModelCatalog.CyrillicV3Id, resolved.Package.Package.Id);
        Assert.AreEqual("sr-Cyrl", resolved.Language.Id);
    }

    [TestMethod]
    public void Catalog_DoesNotExposeV5Models()
    {
        Assert.IsFalse(OpenVinoOcrModelCatalog.Packages.Any(package =>
            package.Id.Contains("v5", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void CompletenessChecks_RequireAllModelArtifacts()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"easychat-ocr-\u6A21\u578B-\U0001F680-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "inference.pdiparams"), [1]);
            Assert.IsFalse(OpenVinoWindowsOcrBackend.IsPaddleModelComplete(root));
            File.WriteAllBytes(Path.Combine(root, "inference.pdmodel"), []);
            Assert.IsFalse(OpenVinoWindowsOcrBackend.IsPaddleModelComplete(root));
            File.WriteAllBytes(Path.Combine(root, "inference.pdmodel"), [1]);
            Assert.IsTrue(OpenVinoWindowsOcrBackend.IsPaddleModelComplete(root));

            File.WriteAllBytes(Path.Combine(root, "inference.onnx"), [1]);
            Assert.IsTrue(OpenVinoWindowsOcrBackend.IsOnnxModelComplete(root));
            Assert.IsFalse(OpenVinoWindowsOcrBackend.IsOnnxModelComplete(root, requireYaml: true));
            File.WriteAllText(Path.Combine(root, "inference.yml"), "model: v6");
            Assert.IsTrue(OpenVinoWindowsOcrBackend.IsOnnxModelComplete(root, requireYaml: true));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
