using System.Collections.ObjectModel;
using System.Reactive;
using EasyChat.Contracts.Settings;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.Features.Capture;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Foundation.Navigation;
using EasyChat.Presentation.Foundation.UiHost;
using ReactiveUI;

namespace EasyChat.Presentation.Features.Capture;

public sealed class FixedAreaEditDialogViewModel : ConventionViewModelBase
{
    private readonly IUiDialogHost _dialogs;
    private readonly IUiDialogSession _dialog;
    private readonly SettingsSession _settings;
    private readonly IScreenRegionPicker _regionPicker;

    public FixedAreaEditDialogViewModel(
        IUiDialogHost dialogs,
        IUiDialogSession dialog,
        SettingsSession settings,
        IScreenRegionPicker regionPicker)
    {
        _dialogs = dialogs;
        _dialog = dialog;
        _settings = settings;
        _regionPicker = regionPicker;
        FixedAreas.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasAreas));
        AddAreaCommand = ReactiveCommand.CreateFromTask(AddAreaAsync);
        DeleteAreaCommand = ReactiveCommand.Create<FixedAreaState>(DeleteArea);
        EditAreaCommand = ReactiveCommand.Create<FixedAreaState>(EditArea);
        CloseCommand = ReactiveCommand.Create(dialog.Dismiss);
    }

    public ObservableCollection<FixedAreaState> FixedAreas => _settings.Screenshot.FixedAreas;
    public bool HasAreas => FixedAreas.Count > 0;
    public ReactiveCommand<Unit, Unit> AddAreaCommand { get; }
    public ReactiveCommand<FixedAreaState, Unit> DeleteAreaCommand { get; }
    public ReactiveCommand<FixedAreaState, Unit> EditAreaCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public void DeleteArea(FixedAreaState area) => FixedAreas.Remove(area);

    public void EditArea(FixedAreaState area)
    {
        _dialog.Dismiss();
        _dialogs.ShowContent(new UiContentDialogOptions
        {
            Title = Resources.Edit,
            CreateContent = session => new FixedAreaFormDialogViewModel(
                session,
                _dialogs,
                _regionPicker,
                area,
                Reopen)
        });
    }

    private async Task AddAreaAsync()
    {
        var selected = await _regionPicker.PickAsync();
        if (selected is not { IsEmpty: false } region)
            return;
        FixedAreas.Add(new FixedAreaState(
            new FixedAreaSettings(
                Guid.NewGuid().ToString(),
                $"Area {FixedAreas.Count + 1}",
                region.X,
                region.Y,
                region.Width,
                region.Height,
                true),
            _settings.FlushSection));
    }

    private void Reopen() => _dialogs.ShowContent(new UiContentDialogOptions
    {
        Title = Resources.FixedAreas,
        CreateContent = session => new FixedAreaEditDialogViewModel(
            _dialogs, session, _settings, _regionPicker)
    });
}

public sealed class FixedAreaFormDialogViewModel : ConventionViewModelBase
{
    private readonly IUiDialogSession _dialog;
    private readonly IUiDialogHost _dialogs;
    private readonly IScreenRegionPicker _regionPicker;
    private readonly FixedAreaState _area;
    private readonly Action _onFinished;
    private string _name;
    private int _x;
    private int _y;
    private int _width;
    private int _height;

    public FixedAreaFormDialogViewModel(
        IUiDialogSession dialog,
        IUiDialogHost dialogs,
        IScreenRegionPicker regionPicker,
        FixedAreaState area,
        Action onFinished)
    {
        _dialog = dialog;
        _dialogs = dialogs;
        _regionPicker = regionPicker;
        _area = area;
        _onFinished = onFinished;
        _name = area.Name;
        _x = area.X;
        _y = area.Y;
        _width = area.Width;
        _height = area.Height;
        ReselectAreaCommand = ReactiveCommand.CreateFromTask(ReselectAsync);
        ConfirmCommand = ReactiveCommand.Create(Confirm);
        CancelCommand = ReactiveCommand.Create(Cancel);
    }

    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }
    public int X { get => _x; set => SetCoordinate(ref _x, value); }
    public int Y { get => _y; set => SetCoordinate(ref _y, value); }
    public int Width { get => _width; set => SetCoordinate(ref _width, value); }
    public int Height { get => _height; set => SetCoordinate(ref _height, value); }
    public string DisplayInfo => $"X: {X}, Y: {Y}, W: {Width}, H: {Height}";
    public ReactiveCommand<Unit, Unit> ReselectAreaCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    private void SetCoordinate(ref int field, int value)
    {
        if (field == value)
            return;
        this.RaiseAndSetIfChanged(ref field, value);
        this.RaisePropertyChanged(nameof(DisplayInfo));
    }

    private async Task ReselectAsync()
    {
        _dialog.Dismiss();
        var selected = await _regionPicker.PickAsync();
        if (selected is { IsEmpty: false } region)
        {
            X = region.X;
            Y = region.Y;
            Width = region.Width;
            Height = region.Height;
        }
        Reopen();
    }

    private void Reopen() => _dialogs.ShowContent(new UiContentDialogOptions
    {
        Title = Resources.Edit,
        CreateContent = session => new FixedAreaFormDialogViewModel(
            session, _dialogs, _regionPicker, _area, _onFinished)
        {
            Name = Name,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height
        }
    });

    private void Confirm()
    {
        _area.Name = Name;
        _area.X = X;
        _area.Y = Y;
        _area.Width = Width;
        _area.Height = Height;
        _dialog.Dismiss();
        _onFinished();
    }

    private void Cancel()
    {
        _dialog.Dismiss();
        _onFinished();
    }
}
