using EasyChat.Presentation.Features.Shell;
using EasyChat.Presentation.Shared.Controls;
using Material.Icons;
using ReactiveUI;
using System.Reactive;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class HomeHealthItemTests
{
    [TestMethod]
    public void DoneItem_ReportsSuccessStatus()
    {
        var command = ReactiveCommand.Create(() => { });
        var item = new HomeHealthItem(
            "Engine",
            "Ready",
            isDone: true,
            MaterialIconKind.Robot,
            "Open",
            command);

        Assert.IsTrue(item.IsDone);
        Assert.IsFalse(item.NeedsAction);
        Assert.AreEqual(EcStatusKind.Success, item.StatusKind);
    }

    [TestMethod]
    public void IncompleteItem_ReportsWarningAndNeedsAction()
    {
        var command = ReactiveCommand.Create(() => { });
        var item = new HomeHealthItem(
            "Shortcuts",
            "Add hotkeys",
            isDone: false,
            MaterialIconKind.Keyboard,
            "Open shortcuts",
            command);

        Assert.IsFalse(item.IsDone);
        Assert.IsTrue(item.NeedsAction);
        Assert.AreEqual(EcStatusKind.Warning, item.StatusKind);
        Assert.AreEqual("Open shortcuts", item.ActionText);
        Assert.AreSame(command, item.ActionCommand);
    }
}
