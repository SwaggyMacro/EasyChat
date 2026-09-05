using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EasyChat.ArchitectureTests;

[TestClass]
public sealed class ArchitectureRulesTests
{
    [TestMethod]
    public void ProjectLayout_MatchesTheApprovedArchitecture()
    {
        var root = FindRepositoryRoot();
        string[] expected =
        [
            "src/EasyChat.Shared/EasyChat.Shared.csproj",
            "src/EasyChat.Domain/EasyChat.Domain.csproj",
            "src/EasyChat.Contracts/EasyChat.Contracts.csproj",
            "src/EasyChat.Application/EasyChat.Application.csproj",
            "src/Infrastructure/EasyChat.Infrastructure/EasyChat.Infrastructure.csproj",
            "src/Infrastructure/EasyChat.Infrastructure.Windows/EasyChat.Infrastructure.Windows.csproj",
            "src/Infrastructure/EasyChat.Infrastructure.MacOS/EasyChat.Infrastructure.MacOS.csproj",
            "src/Infrastructure/MicroASR/MicroASR.csproj",
            "src/Presentation/EasyChat.Presentation.Shared/EasyChat.Presentation.Shared.csproj",
            "src/Presentation/EasyChat.Presentation/EasyChat.Presentation.csproj",
            "src/Host/EasyChat.Desktop/EasyChat.Desktop.csproj",
            "src/Host/EasyChat.Desktop.Windows/EasyChat.Desktop.Windows.csproj",
            "src/Host/EasyChat.Desktop.MacOS/EasyChat.Desktop.MacOS.csproj"
        ];

        foreach (var relative in expected)
            Assert.IsTrue(File.Exists(Path.Combine(root, relative)), relative);

        const string macOSTestProject =
            "tests/EasyChat.Infrastructure.MacOS.Tests/EasyChat.Infrastructure.MacOS.Tests.csproj";
        Assert.IsTrue(File.Exists(Path.Combine(root, macOSTestProject)), macOSTestProject);

        var actual = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEquivalent(expected, actual);
    }

    [TestMethod]
    public void SourceNamespaces_MatchTheirPhysicalLocations()
    {
        var root = FindRepositoryRoot();

        var projects = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories));

        foreach (var project in projects)
        {
            var projectRoot = Path.GetDirectoryName(project)!;
            var document = XDocument.Load(project);
            var rootNamespace = document.Descendants()
                .Where(node => node.Name.LocalName == "RootNamespace")
                .Select(node => node.Value)
                .LastOrDefault() ?? Path.GetFileNameWithoutExtension(project);

            foreach (var file in Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
                         .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                                        || path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
                         .Where(path => !HasPathSegment(path, "bin") && !HasPathSegment(path, "obj")))
            {
                var relative = Path.GetRelativePath(projectRoot, file);
                var relativeDirectory = Path.GetDirectoryName(relative);
                var expectedNamespace = string.IsNullOrEmpty(relativeDirectory)
                    ? rootNamespace
                    : $"{rootNamespace}.{relativeDirectory.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.')}";
                var source = File.ReadAllText(file);

                if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    var namespaces = Regex.Matches(
                        source,
                        @"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]",
                        RegexOptions.Multiline);
                    foreach (Match match in namespaces)
                        Assert.AreEqual(expectedNamespace, match.Groups[1].Value, Path.GetRelativePath(root, file));
                }
                else
                {
                    var classMatch = Regex.Match(source, "x:Class=\"([^\"]+)\"");
                    if (!classMatch.Success)
                        continue;

                    var expectedClass = $"{expectedNamespace}.{Path.GetFileNameWithoutExtension(file)}";
                    Assert.AreEqual(expectedClass, classMatch.Groups[1].Value, Path.GetRelativePath(root, file));
                }
            }
        }
    }

    [TestMethod]
    public void ProductionProjects_FollowTheDependencyGraph()
    {
        var root = FindRepositoryRoot();
        var allowed = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["EasyChat.Shared"] = Set(),
            ["EasyChat.Domain"] = Set("EasyChat.Shared"),
            ["EasyChat.Contracts"] = Set("EasyChat.Shared"),
            ["EasyChat.Application"] = Set("EasyChat.Contracts", "EasyChat.Domain", "EasyChat.Shared"),
            ["EasyChat.Infrastructure"] = Set("EasyChat.Contracts", "EasyChat.Shared", "MicroASR"),
            ["EasyChat.Infrastructure.Windows"] = Set("EasyChat.Contracts", "EasyChat.Shared"),
            ["EasyChat.Infrastructure.MacOS"] = Set("EasyChat.Contracts", "EasyChat.Shared"),
            ["MicroASR"] = Set(),
            ["EasyChat.Presentation.Shared"] = Set(),
            ["EasyChat.Presentation"] = Set("EasyChat.Contracts", "EasyChat.Presentation.Shared"),
            ["EasyChat.Desktop"] = Set("EasyChat.Application", "EasyChat.Contracts", "EasyChat.Infrastructure", "EasyChat.Presentation"),
            ["EasyChat.Desktop.Windows"] = Set(
                "EasyChat.Desktop",
                "EasyChat.Infrastructure.Windows",
                "EasyChat.Presentation"),
            ["EasyChat.Desktop.MacOS"] = Set(
                "EasyChat.Desktop",
                "EasyChat.Infrastructure.MacOS",
                "EasyChat.Presentation")
        };

        foreach (var project in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(project);
            var actual = XDocument.Load(project)
                .Descendants("ProjectReference")
                .Select(node => node.Attribute("Include")!.Value.Replace('\\', '/'))
                .Select(path => Path.GetFileNameWithoutExtension(path)!)
                .ToHashSet(StringComparer.Ordinal);
            Assert.IsTrue(allowed[name].SetEquals(actual), $"{name}: [{string.Join(", ", actual)}]");
        }
    }

    [TestMethod]
    public void CoreAndContracts_DoNotDependOnFrameworksOrPlatforms()
    {
        var root = FindRepositoryRoot();
        string[] layers = ["Shared", "Domain", "Contracts", "Application"];
        string[] forbidden =
        [
            "Avalonia", "ReactiveUI", "DllImport", "LibraryImport", "Microsoft.Win32",
            "OpenCv", "OpenVINO", "Paddle", "SoundFlow", "Velopack", "user32", "kernel32", "HWND", "AXUIElement",
            "NSWindow", "SCDisplay", "AudioDeviceID"
        ];

        foreach (var layer in layers)
            foreach (var file in SourceFiles(Path.Combine(root, "src", $"EasyChat.{layer}")))
            {
                var source = File.ReadAllText(file);
                foreach (var token in forbidden)
                    Assert.DoesNotContain(token, source, Path.GetRelativePath(root, file));
            }
    }

    [TestMethod]
    public void RetiredArchitecture_CannotReturn()
    {
        var root = FindRepositoryRoot();
        var retiredFolder = "Compat" + "ibility";
        var retiredTypePrefix = "Leg" + "acy";
        var globalServices = "Global" + ".Services";
        var globalConfig = "Global" + ".Config";
        var platformGodInterface = "IPlatform" + "Service";
        foreach (var file in SourceFiles(Path.Combine(root, "src")))
        {
            var relative = Path.GetRelativePath(root, file);
            var source = File.ReadAllText(file);
            Assert.IsFalse(relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment.Equals(retiredFolder, StringComparison.OrdinalIgnoreCase)), relative);
            Assert.DoesNotContain(globalServices, source, relative);
            Assert.DoesNotContain(globalConfig, source, relative);
            Assert.DoesNotContain(platformGodInterface, source, relative);
            Assert.IsFalse(Regex.IsMatch(source, $@"\b{retiredTypePrefix}[A-Za-z0-9_]*\b"), relative);
        }
    }

    [TestMethod]
    public void Presentation_UsesFeatureFirstFolders()
    {
        var root = FindRepositoryRoot();
        var presentation = Path.Combine(root, "src", "Presentation", "EasyChat.Presentation");
        string[] forbidden = ["Services", "Models", "Helpers", "Controls", "Converters", "Presentation"];

        foreach (var folder in forbidden)
            Assert.IsFalse(Directory.Exists(Path.Combine(presentation, folder)), folder);
    }

    [TestMethod]
    public void PlatformIndependentProjects_DoNotReferenceNativePackages()
    {
        var root = FindRepositoryRoot();
        string[] forbiddenPackages =
        [
            "GlobalHotKeys.Windows", "OpenCvSharp4.Windows", "Sdcb.OpenVINO",
            "Sdcb.PaddleInference", "Sdcb.PaddleOCR", "SoundFlow", "Xamarin.Mac",
            "Microsoft.macOS", "Microsoft.MacCatalyst"
        ];

        foreach (var project in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
                     .Where(path => !path.Contains("Infrastructure.Windows", StringComparison.Ordinal)
                                    && !path.Contains("Infrastructure.MacOS", StringComparison.Ordinal)))
        {
            var document = XDocument.Load(project);
            foreach (var package in forbiddenPackages)
                Assert.IsFalse(document.Descendants("PackageReference").Any(node =>
                    string.Equals(node.Attribute("Include")?.Value, package, StringComparison.OrdinalIgnoreCase)),
                    $"{Path.GetFileName(project)} -> {package}");
        }
    }

    [TestMethod]
    public void PresentationStack_DoesNotUseTheAvalonia11ReactiveUiAdapter()
    {
        var root = FindRepositoryRoot();

        foreach (var project in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(project);
            Assert.IsFalse(document.Descendants("PackageReference").Any(node =>
                string.Equals(node.Attribute("Include")?.Value, "Avalonia.ReactiveUI", StringComparison.OrdinalIgnoreCase)),
                Path.GetRelativePath(root, project));
        }

        foreach (var file in SourceFiles(Path.Combine(root, "src")))
            Assert.DoesNotContain(".UseReactiveUI(", File.ReadAllText(file), Path.GetRelativePath(root, file));
    }

    [TestMethod]
    public void PlatformExtensionBoundaries_RemainIsolated()
    {
        var root = FindRepositoryRoot();
        var windowsInfrastructure = Path.Combine(
            root,
            "src",
            "Infrastructure",
            "EasyChat.Infrastructure.Windows");
        foreach (var file in SourceFiles(windowsInfrastructure))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("Avalonia", source, Path.GetRelativePath(root, file));
            Assert.DoesNotContain("EasyChat.Presentation", source, Path.GetRelativePath(root, file));
            Assert.DoesNotContain("Infrastructure.MacOS", source, Path.GetRelativePath(root, file));
        }

        var macInfrastructure = Path.Combine(
            root,
            "src",
            "Infrastructure",
            "EasyChat.Infrastructure.MacOS");
        foreach (var file in SourceFiles(macInfrastructure))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("Avalonia", source, Path.GetRelativePath(root, file));
            Assert.DoesNotContain("EasyChat.Presentation", source, Path.GetRelativePath(root, file));
            Assert.DoesNotContain("Infrastructure.Windows", source, Path.GetRelativePath(root, file));
        }

        var sharedDesktop = Path.Combine(root, "src", "Host", "EasyChat.Desktop");
        foreach (var file in SourceFiles(sharedDesktop))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("Infrastructure.Windows", source, Path.GetRelativePath(root, file));
            Assert.DoesNotContain("OperatingSystem.IsWindows", source, Path.GetRelativePath(root, file));
        }

        var appSource = File.ReadAllText(Path.Combine(sharedDesktop, "App.axaml.cs"));
        Assert.DoesNotContain("IServiceProvider", appSource);
        Assert.DoesNotContain("GetRequiredService", appSource);
        Assert.DoesNotContain("GetService(", appSource);

        var windowsProgram = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Host",
            "EasyChat.Desktop.Windows",
            "Program.cs"));
        StringAssert.Contains(windowsProgram, "DesktopApplication.Run");
        Assert.DoesNotContain("AddEasyChatInfrastructure", windowsProgram);
        Assert.DoesNotContain("AddEasyChatApplication", windowsProgram);
        Assert.DoesNotContain("AddEasyChatPresentation", windowsProgram);

        var macProgram = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Host",
            "EasyChat.Desktop.MacOS",
            "Program.cs"));
        StringAssert.Contains(macProgram, "DesktopApplication.Run");
        Assert.DoesNotContain("AddEasyChatInfrastructure", macProgram);
        Assert.DoesNotContain("AddEasyChatApplication", macProgram);
        Assert.DoesNotContain("AddEasyChatPresentation", macProgram);

        foreach (var projectRoot in new[]
                 {
                     Path.Combine(root, "src", "EasyChat.Application"),
                     Path.Combine(root, "src", "Presentation", "EasyChat.Presentation")
                 })
        {
            foreach (var file in SourceFiles(projectRoot))
                Assert.DoesNotContain(
                    "OperatingSystem.IsMacOS",
                    File.ReadAllText(file),
                    Path.GetRelativePath(root, file));
        }

        var presentation = Path.Combine(root, "src", "Presentation", "EasyChat.Presentation");
        foreach (var file in Directory.EnumerateFiles(presentation, "*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                                    || path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("EasyChat.ViewModels.Windows", source, Path.GetRelativePath(root, file));
            Assert.DoesNotContain("EasyChat.Views.Windows", source, Path.GetRelativePath(root, file));
        }

        var speechContract = File.ReadAllText(Path.Combine(
            root,
            "src",
            "EasyChat.Contracts",
            "Platform",
            "SpeechRecognition.cs"));
        StringAssert.Contains(speechContract, "AudioCaptureSourceToken");
        Assert.DoesNotContain("ProcessId", speechContract);

        var audioSourceContract = File.ReadAllText(Path.Combine(
            root,
            "src",
            "EasyChat.Contracts",
            "Platform",
            "AudioCaptureSources.cs"));
        Assert.DoesNotContain("ProcessId", audioSourceContract);
        StringAssert.Contains(audioSourceContract, "opaque");
    }

    [TestMethod]
    public void WindowsHost_PreservesProductIdentity()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "Host", "EasyChat.Desktop.Windows", "EasyChat.Desktop.Windows.csproj"));
        var properties = document.Descendants()
            .Where(node => node.Parent?.Name.LocalName == "PropertyGroup")
            .GroupBy(node => node.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

        Assert.AreEqual("EasyChat", properties["AssemblyName"]);
        Assert.AreEqual("WinExe", properties["OutputType"]);
        Assert.IsTrue(
            Regex.IsMatch(
                properties["Version"],
                @"^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$"),
            "Windows host Version must be an explicit semantic version.");
    }

    [TestMethod]
    public void WindowsHost_DeclaresPerMonitorDpiAwareness()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            root,
            "src",
            "Host",
            "EasyChat.Desktop.Windows",
            "app.manifest"));
        XNamespace assemblyV3 = "urn:schemas-microsoft-com:asm.v3";
        XNamespace windowsSettings2005 = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";
        XNamespace windowsSettings2016 = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";

        var windowsSettings = AssertExactlyOne(document.Descendants(assemblyV3 + "windowsSettings"));
        Assert.AreEqual(assemblyV3 + "application", windowsSettings.Parent?.Name);
        Assert.AreEqual(
            "true/pm",
            AssertExactlyOne(windowsSettings.Elements(windowsSettings2005 + "dpiAware")).Value);
        Assert.AreEqual(
            "PerMonitorV2,PerMonitor",
            AssertExactlyOne(windowsSettings.Elements(windowsSettings2016 + "dpiAwareness")).Value);
    }

    private static IReadOnlySet<string> Set(params string[] values) => values.ToHashSet(StringComparer.Ordinal);

    private static T AssertExactlyOne<T>(IEnumerable<T> values)
    {
        var matches = values.ToArray();
        Assert.HasCount(1, matches);
        return matches[0];
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj" or ".verification"));

    private static bool HasPathSegment(string path, string segment) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(candidate => candidate.Equals(segment, StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "EasyChat.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
