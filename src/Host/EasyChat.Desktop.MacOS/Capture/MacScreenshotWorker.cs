using System.Net.Sockets;
using System.Text;
using Avalonia;
using Avalonia.Styling;
using EasyChat.Contracts.Capture;
using EasyChat.Infrastructure.Capture;
using EasyChat.Infrastructure.MacOS.ApplicationStartup;
using EasyChat.Infrastructure.MacOS.Capture;
using EasyChat.Infrastructure.MacOS.Input;
using EasyChat.Presentation.Features.Capture;
using EasyChat.Presentation.Foundation.Platform;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Desktop.MacOS.Capture;

/// <summary>
/// The screenshot worker entry point: the same executable started with
/// <c>--screenshot-worker</c>, which draws the selection overlay, captures, answers, and exits.
/// </summary>
internal static class MacScreenshotWorker
{
    internal static void Run(string socketPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);

        using var socket = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified);
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        using var stream = new NetworkStream(socket, ownsSocket: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        Task<ScreenshotCaptureCommand?> ReceiveAsync() => Task.Run(() =>
        {
            try
            {
                var request = ScreenshotWorkerProtocol.ReadRequest(reader);
                var theme = request.Theme switch
                {
                    "Dark" => ThemeVariant.Dark,
                    "Light" => ThemeVariant.Light,
                    _ => ThemeVariant.Default
                };
                return new ScreenshotCaptureCommand(
                    request.Precise,
                    theme,
                    request.PrimaryColor,
                    request.CultureName,
                    request.DefaultAction,
                    request.ToolbarMode);
            }
            catch (Exception exception) when (exception is EndOfStreamException
                                              or IOException
                                              or ObjectDisposedException)
            {
                return null;
            }
        });

        void Complete(ScreenshotSelection? selection)
        {
            if (selection is null)
                ScreenshotWorkerProtocol.WriteCancelled(writer);
            else
                ScreenshotWorkerProtocol.WriteSuccess(writer, selection);
        }

        try
        {
            // Without this the helper would appear in the Dock and the application switcher as a
            // second EasyChat, because the activation policy comes from the shared Info.plist.
            MacApplicationPresentation.TryHideFromDock();

            var overlays = new CaptureOverlayCoordinator(
                new MacScreenCatalog(),
                new MacScreenCapture(NullLogger<MacScreenCapture>.Instance),
                new MacPointerPosition(),
                new MacWindowFocus(),
                new MacKeyboardState(),
                new ManagedLongScreenshotStitcher(),
                CreateWindowBehavior());
            AppBuilder.Configure(() => new ScreenshotCaptureWorkerApp(
                    overlays,
                    ReceiveAsync,
                    () => ScreenshotWorkerProtocol.WriteReady(writer),
                    Complete,
                    exception => ScreenshotWorkerProtocol.WriteFailure(writer, exception)))
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime([]);
        }
        catch (Exception exception)
        {
            try
            {
                ScreenshotWorkerProtocol.WriteFailure(writer, exception);
            }
            catch
            {
                // The owner has already closed the connection.
            }
        }
    }

    private static IPlatformWindowBehavior CreateWindowBehavior() =>
        new AvaloniaMacWindowBehavior(
            new MacOwnedWindowBehavior(NullLogger<MacOwnedWindowBehavior>.Instance));
}
