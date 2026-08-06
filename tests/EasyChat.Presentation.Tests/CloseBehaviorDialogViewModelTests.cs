using System.Reactive.Linq;
using EasyChat.Contracts.Settings;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Features.Shell;
using EasyChat.Presentation.Foundation.UiHost;
using EasyChat.Shared.Results;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class CloseBehaviorDialogViewModelTests
{
    [TestMethod]
    public async Task MinimizeWithoutRemember_EnsuresTrayBeforeHidingWithoutChangingSetting()
    {
        var actions = new List<string>();
        var settings = CreateSettings(actions);
        var viewModel = CreateViewModel(settings, actions);

        await viewModel.MinimizeCommand.Execute();

        CollectionAssert.AreEqual(new[] { "tray", "hide" }, actions);
        Assert.AreEqual(ClosingBehavior.Ask, settings.ClosingBehavior);
    }

    [TestMethod]
    public async Task MinimizeWithRemember_PersistsChoiceBeforeEnsuringTrayAndHiding()
    {
        var actions = new List<string>();
        var settings = CreateSettings(actions);
        var viewModel = CreateViewModel(settings, actions);
        viewModel.IsRemember = true;

        await viewModel.MinimizeCommand.Execute();

        CollectionAssert.AreEqual(new[] { "persist", "tray", "hide" }, actions);
        Assert.AreEqual(ClosingBehavior.MinimizeToTray, settings.ClosingBehavior);
    }

    private static CloseBehaviorDialogViewModel CreateViewModel(
        LiveGeneralSettings settings,
        ICollection<string> actions) =>
        new(
            new NullDialogSession(),
            settings,
            () => actions.Add("tray"),
            () => actions.Add("hide"),
            () => actions.Add("exit"));

    private static LiveGeneralSettings CreateSettings(ICollection<string> actions)
    {
        var language = new LanguageSettings(
            "auto",
            "Auto",
            "Auto",
            string.Empty,
            "Auto",
            "Auto",
            new Dictionary<string, string>());
        return new LiveGeneralSettings(
            new GeneralSettings(
                language,
                language,
                null,
                language,
                ClosingBehavior.Ask,
                null,
                null,
                null,
                null,
                null,
                ThemeMode.System,
                null,
                null,
                null,
                true,
                false),
            section =>
            {
                if (section == SettingsSection.General)
                    actions.Add("persist");
                return Result.Success();
            });
    }

    private sealed class NullDialogSession : IUiDialogSession
    {
        public void Dismiss()
        {
        }
    }
}
