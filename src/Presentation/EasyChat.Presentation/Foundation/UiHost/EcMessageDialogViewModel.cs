using System.Reactive;
using EasyChat.Presentation.Foundation.Navigation;
using EasyChat.Presentation.Lang;
using Material.Icons;
using ReactiveUI;

namespace EasyChat.Presentation.Foundation.UiHost;

/// <summary>Ec-styled message / confirm dialog body (replaces Suki badge + bare action row).</summary>
public sealed class EcMessageDialogViewModel : ConventionViewModelBase
{
    private readonly IUiDialogSession _session;
    private readonly Action? _onPrimary;
    private readonly Action? _onSecondary;

    public EcMessageDialogViewModel(IUiDialogSession session, UiMessageDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);

        _session = session;
        _onPrimary = options.OnPrimary;
        _onSecondary = options.OnSecondary;

        Title = options.Title;
        Message = options.Message;
        Severity = options.Severity;
        PrimaryText = options.PrimaryText;
        SecondaryText = options.SecondaryText;
        PrimaryIsDanger = options.PrimaryIsDanger;
        HasPrimary = !string.IsNullOrWhiteSpace(options.PrimaryText);
        HasSecondary = !string.IsNullOrWhiteSpace(options.SecondaryText);
        HasActions = HasPrimary || HasSecondary;
        ShowSafePrimary = HasPrimary && !PrimaryIsDanger;
        ShowDangerPrimary = HasPrimary && PrimaryIsDanger;

        (IconKind, IconBrushKey, IconWellBrushKey) = MapVisual(options.Severity, options.PrimaryIsDanger);

        PrimaryCommand = ReactiveCommand.Create(OnPrimary);
        SecondaryCommand = ReactiveCommand.Create(OnSecondary);
        DismissCommand = ReactiveCommand.Create(() => _session.Dismiss());
    }

    public string Title { get; }
    public string Message { get; }
    public UiMessageSeverity Severity { get; }
    public MaterialIconKind IconKind { get; }
    /// <summary>DynamicResource key for icon foreground (resolved via <see cref="EcDynamicBrushConverter"/>).</summary>
    public string IconBrushKey { get; }
    /// <summary>Soft well behind the severity disc.</summary>
    public string IconWellBrushKey { get; }
    public string? PrimaryText { get; }
    public string? SecondaryText { get; }
    public bool PrimaryIsDanger { get; }
    public bool HasPrimary { get; }
    public bool HasSecondary { get; }
    public bool HasActions { get; }
    public bool ShowSafePrimary { get; }
    public bool ShowDangerPrimary { get; }
    public string DismissText => Resources.Close;

    public ReactiveCommand<Unit, Unit> PrimaryCommand { get; }
    public ReactiveCommand<Unit, Unit> SecondaryCommand { get; }
    public ReactiveCommand<Unit, Unit> DismissCommand { get; }

    private void OnPrimary()
    {
        _onPrimary?.Invoke();
        _session.Dismiss();
    }

    private void OnSecondary()
    {
        _onSecondary?.Invoke();
        _session.Dismiss();
    }

    private static (MaterialIconKind Kind, string IconBrush, string WellBrush) MapVisual(
        UiMessageSeverity severity,
        bool primaryIsDanger)
    {
        if (primaryIsDanger)
            return (MaterialIconKind.TrashCanOutline, "EcDanger", "EcDangerSoft");

        return severity switch
        {
            UiMessageSeverity.Success => (MaterialIconKind.CheckCircleOutline, "EcSuccess", "EcSuccessSoft"),
            UiMessageSeverity.Warning => (MaterialIconKind.AlertOutline, "EcWarning", "EcWarningSoft"),
            UiMessageSeverity.Error => (MaterialIconKind.CloseCircleOutline, "EcDanger", "EcDangerSoft"),
            _ => (MaterialIconKind.InformationOutline, "EcAccent", "EcAccentSoft")
        };
    }
}
