using EasyChat.Presentation.Foundation.UiHost;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class UiHostRecordingTests
{
    [TestMethod]
    public void RecordingToastHost_CapturesShowAndStickyDismiss()
    {
        var host = new RecordingToastHost();
        host.Show("t", "c", UiMessageSeverity.Warning, TimeSpan.FromSeconds(1));
        var sticky = host.ShowSticky("progress", 42);
        sticky.Dismiss();
        host.ShowWithActions("update", "body", new UiToastAction("Go", () => { }));

        Assert.AreEqual(3, host.Calls.Count);
        Assert.AreEqual("show", host.Calls[0].Kind);
        Assert.AreEqual(UiMessageSeverity.Warning, host.Calls[0].Severity);
        Assert.AreEqual("sticky", host.Calls[1].Kind);
        Assert.IsTrue(host.Calls[1].Dismissed);
        Assert.AreEqual("actions", host.Calls[2].Kind);
        Assert.AreEqual(1, host.Calls[2].ActionCount);
    }

    [TestMethod]
    public void RecordingDialogHost_CapturesMessageAndContent()
    {
        var host = new RecordingDialogHost();
        object? created = null;
        host.ShowMessage(new UiMessageDialogOptions
        {
            Title = "Confirm",
            Message = "Delete?",
            Severity = UiMessageSeverity.Warning,
            PrimaryText = "Delete",
            PrimaryIsDanger = true,
            SecondaryText = "Cancel"
        });
        host.ShowContent(new UiContentDialogOptions
        {
            Title = "Edit",
            CreateContent = session =>
            {
                created = session;
                return "vm";
            }
        });

        Assert.AreEqual(2, host.Calls.Count);
        Assert.AreEqual("message", host.Calls[0].Kind);
        Assert.IsTrue(host.Calls[0].PrimaryIsDanger);
        Assert.AreEqual("content", host.Calls[1].Kind);
        Assert.IsNotNull(created);
        Assert.AreEqual("vm", host.Calls[1].Content);
    }

    private sealed class RecordingToastHost : IUiToastHost
    {
        public List<ToastCall> Calls { get; } = [];

        public void Show(
            string title,
            string? content = null,
            UiMessageSeverity severity = UiMessageSeverity.Information,
            TimeSpan? autoDismiss = null) =>
            Calls.Add(new ToastCall("show", severity, ActionCount: 0, Dismissed: false));

        public IUiToastSession ShowSticky(string title, object? content = null)
        {
            var call = new ToastCall("sticky", UiMessageSeverity.Information, 0, false);
            Calls.Add(call);
            return new Session(call);
        }

        public void ShowWithActions(string title, string content, params UiToastAction[] actions) =>
            Calls.Add(new ToastCall("actions", UiMessageSeverity.Information, actions.Length, false));

        private sealed class Session(ToastCall call) : IUiToastSession
        {
            public void Dismiss() => call.Dismissed = true;
        }
    }

    private sealed class RecordingDialogHost : IUiDialogHost
    {
        public List<DialogCall> Calls { get; } = [];

        public void ShowMessage(UiMessageDialogOptions options) =>
            Calls.Add(new DialogCall(
                "message",
                options.PrimaryIsDanger,
                Content: null));

        public void ShowContent(UiContentDialogOptions options)
        {
            var session = new NullSession();
            var content = options.CreateContent(session);
            Calls.Add(new DialogCall("content", false, content));
        }

        private sealed class NullSession : IUiDialogSession
        {
            public void Dismiss()
            {
            }
        }
    }

    private sealed class ToastCall(
        string Kind,
        UiMessageSeverity Severity,
        int ActionCount,
        bool Dismissed)
    {
        public string Kind { get; } = Kind;
        public UiMessageSeverity Severity { get; } = Severity;
        public int ActionCount { get; } = ActionCount;
        public bool Dismissed { get; set; } = Dismissed;
    }

    private sealed record DialogCall(string Kind, bool PrimaryIsDanger, object? Content);
}
