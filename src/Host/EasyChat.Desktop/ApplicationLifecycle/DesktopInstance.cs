namespace EasyChat.Desktop.ApplicationLifecycle;

public interface IDesktopInstanceLease : IDisposable
{
    void SetActivationHandler(Action handler);
}

public interface IDesktopInstanceCoordinator
{
    IDesktopInstanceLease? AcquireOrSignal(bool waitForExistingRelease = false);
}
