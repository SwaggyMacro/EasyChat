using System.Runtime.Versioning;
using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;
using Microsoft.Extensions.Logging;

namespace EasyChat.Infrastructure.Windows.Input;

[SupportedOSPlatform("windows")]
public sealed class WindowsTextDelivery : ITextDelivery
{
    private const uint WindowMessageCharacter = 0x0102;
    private readonly IClipboardSnapshots _clipboardSnapshots;
    private readonly IClipboardText _clipboardText;
    private readonly ILogger<WindowsTextDelivery> _logger;
    private readonly WindowsNativeInputBackend _native = new();

    public WindowsTextDelivery(
        IClipboardSnapshots clipboardSnapshots,
        IClipboardText clipboardText,
        ILogger<WindowsTextDelivery> logger)
    {
        _clipboardSnapshots = clipboardSnapshots ??
                              throw new ArgumentNullException(nameof(clipboardSnapshots));
        _clipboardText = clipboardText ?? throw new ArgumentNullException(nameof(clipboardText));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<Result> DeliverAsync(
        TextDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var delay = TimeSpan.FromMilliseconds(Math.Clamp(
                request.KeyDelay.TotalMilliseconds,
                0,
                int.MaxValue));
            switch (request.Mode)
            {
                case TextDeliveryMode.Paste:
                    return await PasteAsync(request.Text, cancellationToken);
                case TextDeliveryMode.Type:
                    await TypeAsync(request.Text, delay, cancellationToken);
                    return Result.Success();
                case TextDeliveryMode.Message:
                    await SendAsync(request.Target, request.Text, delay, cancellationToken);
                    return Result.Success();
                default:
                    return Result.Failure(new Error(
                        "text-delivery.mode-unsupported",
                        $"Unsupported text delivery mode: {request.Mode}."));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Text delivery failed.");
            return Result.Failure(new Error("text-delivery.failed", exception.Message));
        }
    }

    public ValueTask<Result> SendKeyCombinationAsync(
        string combination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(combination);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var inputs = WindowsKeyCombination.Parse(combination);
            var sent = _native.SendInputs(inputs);
            return ValueTask.FromResult(sent == inputs.Count
                ? Result.Success()
                : Result.Failure(new Error(
                    "text-delivery.key-combination-failed",
                    $"Only {sent} of {inputs.Count} key events were sent.")));
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(Result.Failure(new Error(
                "text-delivery.key-combination-failed",
                exception.Message)));
        }
    }

    public ValueTask<Result> SendCommandAsync(
        StandardTextCommand command,
        CancellationToken cancellationToken = default) =>
        SendKeyCombinationAsync(command switch
        {
            StandardTextCommand.SelectAll => "Ctrl + A",
            StandardTextCommand.Delete => "Delete",
            StandardTextCommand.Copy => "Ctrl + C",
            StandardTextCommand.Paste => "Ctrl + V",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
        }, cancellationToken);

    private async ValueTask<Result> PasteAsync(
        string text,
        CancellationToken cancellationToken)
    {
        IClipboardSnapshot? snapshot = null;
        try
        {
            var capture = await Task.Run(
                async () => await _clipboardSnapshots.CaptureAsync(cancellationToken),
                cancellationToken);
            if (capture.IsSuccess)
                snapshot = capture.Value;

            var setText = await Task.Run(
                async () => await _clipboardText.WriteAsync(text, cancellationToken),
                cancellationToken);
            if (setText.IsFailure)
                return setText;

            await Task.Delay(50, cancellationToken);
            var paste = await SendCommandAsync(StandardTextCommand.Paste, cancellationToken);
            if (paste.IsFailure)
                return paste;

            await Task.Delay(200, cancellationToken);
            return Result.Success();
        }
        finally
        {
            if (snapshot is not null)
            {
                await Task.Run(
                    async () => await _clipboardSnapshots.RestoreAsync(
                        snapshot,
                        CancellationToken.None));
                await snapshot.DisposeAsync();
            }
        }
    }

    private async Task TypeAsync(
        string text,
        TimeSpan keyDelay,
        CancellationToken cancellationToken)
    {
        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (character == '\r')
                continue;

            IReadOnlyList<WindowsKeyboardInput> inputs = character == '\n'
                ?
                [
                    new WindowsKeyboardInput(0x0D, 0, 0),
                    new WindowsKeyboardInput(0x0D, 0, WindowsKeyboardInput.KeyUp)
                ]
                :
                [
                    new WindowsKeyboardInput(0, character, WindowsKeyboardInput.Unicode),
                    new WindowsKeyboardInput(
                        0,
                        character,
                        WindowsKeyboardInput.Unicode | WindowsKeyboardInput.KeyUp)
                ];

            _native.SendInputs(inputs);
            if (keyDelay > TimeSpan.Zero)
                await Task.Delay(keyDelay, cancellationToken);
        }
    }

    private async Task SendAsync(
        ExternalTargetToken target,
        string text,
        TimeSpan keyDelay,
        CancellationToken cancellationToken)
    {
        var handle = WindowsTargetTokens.GetHandle(target);
        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _native.PostMessage(handle, WindowMessageCharacter, character, IntPtr.Zero);
            if (keyDelay > TimeSpan.Zero)
                await Task.Delay(keyDelay, cancellationToken);
        }
    }
}
internal static class WindowsKeyCombination
{
    private static readonly IReadOnlyDictionary<string, ushort> NamedKeys =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["Back"] = 0x08,
            ["Backspace"] = 0x08,
            ["Tab"] = 0x09,
            ["Enter"] = 0x0D,
            ["Return"] = 0x0D,
            ["Escape"] = 0x1B,
            ["Esc"] = 0x1B,
            ["Space"] = 0x20,
            ["PageUp"] = 0x21,
            ["PageDown"] = 0x22,
            ["End"] = 0x23,
            ["Home"] = 0x24,
            ["Left"] = 0x25,
            ["Up"] = 0x26,
            ["Right"] = 0x27,
            ["Down"] = 0x28,
            ["Insert"] = 0x2D,
            ["Delete"] = 0x2E,
            ["Del"] = 0x2E
        };

    public static IReadOnlyList<WindowsKeyboardInput> Parse(string combination)
    {
        var parts = combination.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            throw new ArgumentException("The key combination is empty.", nameof(combination));

        var modifiers = new List<ushort>();
        ushort? key = null;
        foreach (var part in parts)
        {
            if (TryMapModifier(part, out var modifier))
            {
                modifiers.Add(modifier);
                continue;
            }

            if (key.HasValue)
                throw new ArgumentException($"Invalid key combination: {combination}", nameof(combination));
            key = MapKey(part);
        }

        if (!key.HasValue)
            throw new ArgumentException($"Invalid key combination: {combination}", nameof(combination));

        var inputs = new List<WindowsKeyboardInput>();
        inputs.AddRange(modifiers.Select(value => new WindowsKeyboardInput(value, 0, 0)));
        inputs.Add(new WindowsKeyboardInput(key.Value, 0, 0));
        inputs.Add(new WindowsKeyboardInput(key.Value, 0, WindowsKeyboardInput.KeyUp));
        for (var index = modifiers.Count - 1; index >= 0; index--)
            inputs.Add(new WindowsKeyboardInput(modifiers[index], 0, WindowsKeyboardInput.KeyUp));
        return inputs;
    }

    private static bool TryMapModifier(string value, out ushort key)
    {
        key = value.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => 0x11,
            "ALT" => 0x12,
            "SHIFT" => 0x10,
            "WIN" or "WINDOWS" or "META" => 0x5B,
            _ => 0
        };
        return key != 0;
    }

    private static ushort MapKey(string value)
    {
        if (value.Length == 1)
        {
            var character = char.ToUpperInvariant(value[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return character;
        }

        if (value.Length is 2 or 3
            && value[0] is 'F' or 'f'
            && int.TryParse(value[1..], out var functionKey)
            && functionKey is >= 1 and <= 24)
        {
            return (ushort)(0x70 + functionKey - 1);
        }

        if (NamedKeys.TryGetValue(value.Replace(" ", string.Empty), out var key))
            return key;

        throw new ArgumentException($"Unsupported key: {value}", nameof(value));
    }
}
