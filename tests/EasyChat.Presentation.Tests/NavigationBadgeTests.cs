using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Updates;
using EasyChat.Presentation.Features.Shell;
using EasyChat.Presentation.Foundation.Navigation;
using EasyChat.Shared.Results;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class NavigationBadgeTests
{
    [TestMethod]
    public void NavigationPage_AttentionBadge_DefaultsOff()
    {
        NavigationPageViewModel page = CreateAboutPage();
        Assert.IsFalse(page.ShowAttentionBadge);
        Assert.IsNull(page.AttentionBadgeText);
    }

    [TestMethod]
    public void NavigationPage_AttentionBadge_CanBeSet()
    {
        NavigationPageViewModel page = CreateAboutPage();
        page.ShowAttentionBadge = true;
        page.AttentionBadgeText = "Needs setup";

        Assert.IsTrue(page.ShowAttentionBadge);
        Assert.AreEqual("Needs setup", page.AttentionBadgeText);
    }

    private static AboutViewModel CreateAboutPage() =>
        new(new StubUpdates(), new StubUriLauncher());

    private sealed class StubUpdates : IApplicationUpdateService
    {
        public string CurrentVersion => "0.0.0-test";

        public ValueTask<Result<ApplicationUpdateStatus>> CheckAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<ApplicationUpdateStatus>.Success(
                new ApplicationUpdateStatus("0.0.0-test", "0.0.0-test", false)));

        public ValueTask<Result> DownloadAndRestartAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());
    }

    private sealed class StubUriLauncher : IExternalUriLauncher
    {
        public Result Open(Uri uri) => Result.Success();
    }
}
