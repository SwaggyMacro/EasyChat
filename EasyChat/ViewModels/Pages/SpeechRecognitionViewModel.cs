using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Threading;
using EasyChat.Models;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Languages;
using System.IO;
using System.Threading;
using System.Threading.Tasks; // Added
using EasyChat.Services.Translation;
using EasyChat.Services.Speech;
using Material.Icons;
using ReactiveUI;
using System.Reactive.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace EasyChat.ViewModels.Pages;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class SpeechRecognitionViewModel : Page
{
    private readonly IConfigurationService _configurationService;    // Services
    private readonly ISpeechRecognitionService _speechRecognitionService; // Renamed
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly IProcessService _processService;
    private readonly ILogger<SpeechRecognitionViewModel> _logger;
    // Removed _snackbar (unused)

    // Translation State
    private readonly object _translationLock = new();
    private readonly Dictionary<SubtitleItem, string> _latestTextForItem = new();
    private readonly Dictionary<SubtitleItem, Task> _activeItemLoops = new();
    private readonly Dictionary<SubtitleItem, CancellationTokenSource> _activeItemCts = new();
    private readonly HashSet<SubtitleItem> _isProcessingStableForItem = new();
    
    // Config
    private bool _isRecording;
    private bool _isTranslationEnabled;
    private bool _isRealTimePreviewEnabled;
    private SubtitleItem? _currentSubtitleItem;
    private CancellationTokenSource? _recordingCts;
    // Renamed _currentlyProcessingItem to local if needed, but here we just removed it
    
    private SubtitleItem? _currentSubtitleItem_DuplicateRemover; // Placeholder to avoid error if I miss one? No.


    // Reuse helper class or define locally if internal
    public class EngineOption
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public bool IsMachine { get; set; }
        public override string ToString() => Name;
        
        public override bool Equals(object? obj) => obj is EngineOption other && Id == other.Id;
        public override int GetHashCode() => Id.GetHashCode();
    }

    // ProcessInfo Removed (using EasyChat.Models.ProcessInfo)

    public bool IsSupported => OperatingSystem.IsWindows();
    public bool IsNotSupported => !IsSupported;

    public SpeechRecognitionViewModel(
        IConfigurationService configurationService, 
        ITranslationServiceFactory translationServiceFactory,
        ISpeechRecognitionService speechRecognitionService,
        IProcessService processService,
        ILogger<SpeechRecognitionViewModel> logger) 
        : base(Lang.Resources.Page_SpeechRecognition, MaterialIconKind.Microphone, 4)
    {
        _configurationService = configurationService;
        _translationServiceFactory = translationServiceFactory;
        _speechRecognitionService = speechRecognitionService;
        _processService = processService;
        _logger = logger;
        
        // Initialize defaults
        _selectedEngineOption = null!;
        _selectedTargetLanguage = null!;

        // Subscribe to Service Events
        _speechRecognitionService.OnFinalResult += text => OnRecognitionResult(0, text);
        _speechRecognitionService.OnPartialResult += text => OnRecognitionResult(1, text);
        _speechRecognitionService.OnError += text => OnRecognitionResult(2, text);
        _speechRecognitionService.OnStarted += () => OnRecognitionResult(100, ""); // 100 is custom for started
        _speechRecognitionService.OnStopped += () => OnRecognitionResult(3, "");

        // Initialize lists
        _recognitionLanguages = new ObservableCollection<string>();
        _selectedRecognitionLanguage = string.Empty; 
        
        SubtitleItems = new ObservableCollection<SubtitleItem>();
        
        // Commands
        var canToggle = this.WhenAnyValue(x => x.IsBusy, busy => !busy).ObserveOn(RxApp.MainThreadScheduler);
        ToggleRecordingCommand = ReactiveCommand.CreateFromTask(async () => { 
            if (OperatingSystem.IsWindows()) await ToggleRecordingAsync(); 
        }, canToggle);
        
        RefreshProcessesCommand = ReactiveCommand.Create(() => { 
            if (OperatingSystem.IsWindows()) _processService.RefreshProcesses(); 
        });

        // Floating Window Commands
        ToggleFloatingWindowCommand = ReactiveCommand.Create(ToggleFloatingWindow);
        ToggleLockCommand = ReactiveCommand.Create(() => IsFloatingWindowLocked = !IsFloatingWindowLocked);
        UnlockFloatingWindowCommand = ReactiveCommand.Create(() => IsFloatingWindowLocked = false);
        
        if (OperatingSystem.IsWindows())
        {
            // Initialize Models/Engines
            LoadRecognitionModels();
            LoadEngineOptions();
            
            // Load Configuration
            var config = _configurationService.SpeechRecognition;
            IsTranslationEnabled = config.IsTranslationEnabled;
            IsRealTimePreviewEnabled = config.IsRealTimePreviewEnabled;
            
            // Initialize Selection from Config or Defaults
            if (!string.IsNullOrEmpty(config.RecognitionLanguage) && RecognitionLanguages.Contains(config.RecognitionLanguage))
            {
                _selectedRecognitionLanguage = config.RecognitionLanguage;
                this.RaisePropertyChanged(nameof(SelectedRecognitionLanguage));
            }
    
            // Engine
            if (!string.IsNullOrEmpty(config.EngineId))
            {
                var savedEngine = EngineOptions.FirstOrDefault(e => e.Id == config.EngineId && 
                                                                    ((config.EngineType == 0 && e.IsMachine) || (config.EngineType == 1 && !e.IsMachine)));
                if (savedEngine != null) _selectedEngineOption = savedEngine;
                else _selectedEngineOption = EngineOptions.FirstOrDefault(e => e.Id == "Baidu") ?? EngineOptions.FirstOrDefault()!;
            }
            else
            {
                _selectedEngineOption = EngineOptions.FirstOrDefault(e => e.Id == "Baidu") ?? EngineOptions.FirstOrDefault()!;
            }
    
            // Initialize Target Languages based on Engine
            UpdateAvailableTargetLanguages();
    
            // Target Language
            if (!string.IsNullOrEmpty(config.TargetLanguage))
            {
                var savedTarget = TargetLanguages.FirstOrDefault(l => l.Id == config.TargetLanguage);
                if (savedTarget != null) _selectedTargetLanguage = savedTarget;
                else _selectedTargetLanguage = TargetLanguages.FirstOrDefault()!;
            }
            else
            {
                 _selectedTargetLanguage = TargetLanguages.FirstOrDefault()!;
            }
            
            // Setup Persistence
            this.WhenAnyValue(x => x.IsTranslationEnabled).Subscribe(v => config.IsTranslationEnabled = v);
            this.WhenAnyValue(x => x.IsRealTimePreviewEnabled).Subscribe(v => config.IsRealTimePreviewEnabled = v);
            this.WhenAnyValue(x => x.SelectedRecognitionLanguage).Subscribe(v => config.RecognitionLanguage = v);
            this.WhenAnyValue(x => x.SelectedEngineOption).Where(x => x != null).Subscribe(v => 
            {
                config.EngineId = v!.Id;
                config.EngineType = v.IsMachine ? 0 : 1;
            });
            this.WhenAnyValue(x => x.SelectedTargetLanguage).Where(x => x != null).Subscribe(v => config.TargetLanguage = v!.Id);
            
            // Initialize Floating Config
            // Initialize Floating Config
            MainSubtitleSource = config.MainSubtitleSource;
            PrimaryFontSize = config.PrimaryFontSize;
            PrimaryFontFamily = config.PrimaryFontFamily;
            PrimaryFontColor = config.PrimaryFontColor;

            SecondarySubtitleSource = config.SecondarySubtitleSource;
            SecondaryFontSize = config.SecondaryFontSize;
            SecondaryFontFamily = config.SecondaryFontFamily;
            SecondaryFontColor = config.SecondaryFontColor;
            
            BackgroundColor = config.BackgroundColor;
            SubtitleBackgroundColor = config.SubtitleBackgroundColor;
            WindowOpacity = config.WindowOpacity;
            IsFloatingWindowLocked = config.IsFloatingWindowLocked;
            FloatingWindowOrientation = config.FloatingWindowOrientation;

            // Sync Floating Config
            this.WhenAnyValue(x => x.MainSubtitleSource).Subscribe(v => config.MainSubtitleSource = v);
            this.WhenAnyValue(x => x.PrimaryFontSize).Subscribe(v => config.PrimaryFontSize = v);
            this.WhenAnyValue(x => x.PrimaryFontFamily).Subscribe(v => config.PrimaryFontFamily = v);
            this.WhenAnyValue(x => x.PrimaryFontColor).Subscribe(v => config.PrimaryFontColor = v);

            this.WhenAnyValue(x => x.SecondarySubtitleSource).Subscribe(v => config.SecondarySubtitleSource = v);
            this.WhenAnyValue(x => x.SecondaryFontSize).Subscribe(v => config.SecondaryFontSize = v);
            this.WhenAnyValue(x => x.SecondaryFontFamily).Subscribe(v => config.SecondaryFontFamily = v);
            this.WhenAnyValue(x => x.SecondaryFontColor).Subscribe(v => config.SecondaryFontColor = v);

            this.WhenAnyValue(x => x.BackgroundColor).Subscribe(v => config.BackgroundColor = v);
            this.WhenAnyValue(x => x.SubtitleBackgroundColor).Subscribe(v => config.SubtitleBackgroundColor = v);
            this.WhenAnyValue(x => x.WindowOpacity).Subscribe(v => config.WindowOpacity = v);
            this.WhenAnyValue(x => x.IsFloatingWindowLocked).Subscribe(v => config.IsFloatingWindowLocked = v);
            this.WhenAnyValue(x => x.FloatingWindowOrientation).Subscribe(v => config.FloatingWindowOrientation = v);
            
            // Sync initial processes
            _processService.RefreshProcesses();
            
            // Subscribe to Processes changes for Summary update
            Processes.CollectionChanged += Processes_CollectionChanged;
            foreach(var p in Processes) p.PropertyChanged += Process_PropertyChanged;
        }
    }

    private void Processes_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (ProcessInfo item in e.NewItems)
                item.PropertyChanged += Process_PropertyChanged;
        }

        if (e.OldItems != null)
        {
            foreach (ProcessInfo item in e.OldItems)
                item.PropertyChanged -= Process_PropertyChanged;
        }
        
        UpdateProcessesSummary();
    }

    private void Process_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProcessInfo.IsSelected)) UpdateProcessesSummary();
    }

    


    private void LoadRecognitionModels()
    {
        try
        {
            _recognitionLanguages.Clear();
            string libPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lib");
            if (Directory.Exists(libPath))
            {
                var dirs = Directory.GetDirectories(libPath);
                foreach (var dir in dirs)
                {
                    _recognitionLanguages.Add(new DirectoryInfo(dir).Name); 
                }
            }
            
            if (_recognitionLanguages.Count > 0)
            {
                SelectedRecognitionLanguage = _recognitionLanguages.FirstOrDefault(l => l.Contains("zh")) ?? _recognitionLanguages.First();
            }
        }
        catch (Exception ex)
        {
            // Log or handle error
            _logger.LogError(ex, "Error loading models");
        }
    }

    private void LoadEngineOptions()
    {
        var options = new List<EngineOption>();
        
        // Add Machine Engines
        // Note: Assuming MachineTrans class exists or using hardcoded list if not found.
        // If MachineTrans is not found, I will use a hardcoded list of common providers for now to fix the build.
        var providers = new[] { "Baidu", "Tencent", "Google", "DeepL" }; 
        foreach (var provider in providers)
        {
            options.Add(new EngineOption { Name = provider, Id = provider, IsMachine = true });
        }
        
        // Add AI Models
        if (_configurationService.AiModel?.ConfiguredModels != null)
        {
            foreach (var model in _configurationService.AiModel.ConfiguredModels)
            {
                options.Add(new EngineOption { Name = model.Name, Id = model.Id, IsMachine = false });
            }
        }

        EngineOptions = new ObservableCollection<EngineOption>(options);
    }
    
    private void UpdateAvailableTargetLanguages()
    {
        var allLanguages = LanguageService.GetAllLanguages();

        if (SelectedEngineOption == null)
        {
            TargetLanguages = new ObservableCollection<LanguageDefinition>(allLanguages);
            return;
        }

        IEnumerable<LanguageDefinition> filtered;
        if (SelectedEngineOption.IsMachine)
        {
             filtered = allLanguages
                .Where(l => l.Id == "auto" || !string.IsNullOrEmpty(l.GetCode(SelectedEngineOption.Id)));
        }
        else
        {
            // AI Model - assume all valid (or filter if needed)
            filtered = allLanguages;
        }

        TargetLanguages = new ObservableCollection<LanguageDefinition>(filtered);

        // Restore or default selection
        var currentId = SelectedTargetLanguage?.Id ?? "zh-Hans";
        SelectedTargetLanguage = TargetLanguages.FirstOrDefault(l => l.Id == currentId) 
                                 ?? TargetLanguages.FirstOrDefault(l => l.Id == "zh-Hans") 
                                 ?? TargetLanguages.FirstOrDefault()!;
    }
    
    // Properties
    
    public bool IsTranslationEnabled
    {
         get => _isTranslationEnabled;
         set => this.RaiseAndSetIfChanged(ref _isTranslationEnabled, value);
    }

    public bool IsRealTimePreviewEnabled
    {
        get => _isRealTimePreviewEnabled;
        set => this.RaiseAndSetIfChanged(ref _isRealTimePreviewEnabled, value);
    }

    private string _selectedProcessesSummary = Lang.Resources.Speech_AllSystemAudio;
    public string SelectedProcessesSummary
    {
        get => _selectedProcessesSummary;
        set => this.RaiseAndSetIfChanged(ref _selectedProcessesSummary, value);
    }

    private string _selectedRecognitionLanguage;
    public string SelectedRecognitionLanguage
    {
        get => _selectedRecognitionLanguage;
        set => this.RaiseAndSetIfChanged(ref _selectedRecognitionLanguage, value);
    }

    private ObservableCollection<string> _recognitionLanguages;
    public ObservableCollection<string> RecognitionLanguages
    {
        get => _recognitionLanguages;
        set => this.RaiseAndSetIfChanged(ref _recognitionLanguages, value);
    }

    private ObservableCollection<EngineOption> _engineOptions = new();
    public ObservableCollection<EngineOption> EngineOptions
    {
        get => _engineOptions;
        set => this.RaiseAndSetIfChanged(ref _engineOptions, value);
    }

    private EngineOption _selectedEngineOption;
    public EngineOption SelectedEngineOption
    {
        get => _selectedEngineOption;
        set
        {
             this.RaiseAndSetIfChanged(ref _selectedEngineOption, value);
             UpdateAvailableTargetLanguages();
        }
    }

    private LanguageDefinition _selectedTargetLanguage;
    public LanguageDefinition SelectedTargetLanguage
    {
        get => _selectedTargetLanguage;
        set => this.RaiseAndSetIfChanged(ref _selectedTargetLanguage, value);
    }

    private ObservableCollection<LanguageDefinition> _targetLanguages = new();
    public ObservableCollection<LanguageDefinition> TargetLanguages
    {
        get => _targetLanguages;
        set => this.RaiseAndSetIfChanged(ref _targetLanguages, value);
    }
    
    // --- Floating Window Configuration Properties ---

    // Primary
    
    private int _mainSubtitleSource;
    public int MainSubtitleSource
    {
        get => _mainSubtitleSource;
        set => this.RaiseAndSetIfChanged(ref _mainSubtitleSource, value);
    }

    private double _primaryFontSize;
    public double PrimaryFontSize
    {
        get => _primaryFontSize;
        set => this.RaiseAndSetIfChanged(ref _primaryFontSize, value);
    }

    private string _primaryFontFamily;
    public string PrimaryFontFamily
    {
        get => _primaryFontFamily;
        set => this.RaiseAndSetIfChanged(ref _primaryFontFamily, value);
    }

    private string _primaryFontColor;
    public string PrimaryFontColor
    {
        get => _primaryFontColor;
        set => this.RaiseAndSetIfChanged(ref _primaryFontColor, value);
    }

    // Secondary

    private int _secondarySubtitleSource;
    public int SecondarySubtitleSource
    {
        get => _secondarySubtitleSource;
        set => this.RaiseAndSetIfChanged(ref _secondarySubtitleSource, value);
    }

    private double _secondaryFontSize;
    public double SecondaryFontSize
    {
        get => _secondaryFontSize;
        set => this.RaiseAndSetIfChanged(ref _secondaryFontSize, value);
    }

    private string _secondaryFontFamily;
    public string SecondaryFontFamily
    {
        get => _secondaryFontFamily;
        set => this.RaiseAndSetIfChanged(ref _secondaryFontFamily, value);
    }

    private string _secondaryFontColor;
    public string SecondaryFontColor
    {
        get => _secondaryFontColor;
        set => this.RaiseAndSetIfChanged(ref _secondaryFontColor, value);
    }

    private string _backgroundColor;
    public string BackgroundColor
    {
        get => _backgroundColor;
        set => this.RaiseAndSetIfChanged(ref _backgroundColor, value);
    }

    private string _subtitleBackgroundColor;
    public string SubtitleBackgroundColor
    {
        get => _subtitleBackgroundColor;
        set => this.RaiseAndSetIfChanged(ref _subtitleBackgroundColor, value);
    }

    private double _windowOpacity;
    public double WindowOpacity
    {
        get => _windowOpacity;
        set => this.RaiseAndSetIfChanged(ref _windowOpacity, value);
    }
    
    private bool _isFloatingWindowLocked;
    public bool IsFloatingWindowLocked
    {
        get => _isFloatingWindowLocked;
        set => this.RaiseAndSetIfChanged(ref _isFloatingWindowLocked, value);
    }

    private string _floatingWindowOrientation;
    public string FloatingWindowOrientation
    {
        get => _floatingWindowOrientation;
        set => this.RaiseAndSetIfChanged(ref _floatingWindowOrientation, value);
    }

    public ObservableCollection<string> OrientationOptions { get; } = new ObservableCollection<string> { "Horizontal", "Vertical" };
    
    // Subtitle Sources Options
    // 0=Original, 1=Translated
    public ObservableCollection<KeyValuePair<int, string>> MainSourceOptions { get; } = new ObservableCollection<KeyValuePair<int, string>> 
    { 
        new(0, "Original"), 
        new(1, "Translated") 
    };

    // 0=None, 1=Original, 2=Translated
    public ObservableCollection<KeyValuePair<int, string>> SecondarySourceOptions { get; } = new ObservableCollection<KeyValuePair<int, string>> 
    { 
        new(0, "None"), 
        new(1, "Original"), 
        new(2, "Translated") 
    };

    
    // Recording Logic
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            this.RaiseAndSetIfChanged(ref _isRecording, value);
            this.RaisePropertyChanged(nameof(RecordingText));
            this.RaisePropertyChanged(nameof(RecordingIcon));
        }
    }

    public string RecordingText => IsRecording ? Lang.Resources.Speech_Stop : Lang.Resources.Speech_Start;
    public MaterialIconKind RecordingIcon => IsRecording ? MaterialIconKind.MicrophoneOff : MaterialIconKind.Microphone;

    public ObservableCollection<SubtitleItem> SubtitleItems { get; }
    public ObservableCollection<SubtitleItem> FloatingSubtitles { get; } = new ObservableCollection<SubtitleItem>();

    public ObservableCollection<ProcessInfo> Processes => _processService.Processes;
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshProcessesCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleRecordingCommand { get; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleFloatingWindowCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, bool> ToggleLockCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, bool> UnlockFloatingWindowCommand { get; }
    
    private Avalonia.Controls.Window? _floatingWindow;
    private bool _isFloatingWindowOpen;
    public bool IsFloatingWindowOpen
    {
        get => _isFloatingWindowOpen;
        set => this.RaiseAndSetIfChanged(ref _isFloatingWindowOpen, value);
    }
    
    private void ToggleFloatingWindow()
    {
        if (IsFloatingWindowOpen)
        {
            // Close
            if (_floatingWindow != null)
            {
                _floatingWindow.Close();
                _floatingWindow = null;
            }
            IsFloatingWindowOpen = false;
        }
        else
        {
            // Open
            // We need to instantiate the View. 
            // Ideally we use a ViewLocator or DI, but for now we reflectively instantiate or use direct reference if possible.
            // Since ViewModel is in a different namespace/assembly than View typically, or circular deps.
            // But they are in the same assembly here 'EasyChat'.
            
            try
            {
                // Dynamic instantiation to avoid circular hard dependency if strict MVVM, 
                // but here we can just use the type if known.
                // Assuming EasyChat.Views.Speech.SubtitleOverlayWindow exists.
                // Use Assembly.GetType to find it within the same assembly.
                var windowType = typeof(SpeechRecognitionViewModel).Assembly.GetType("EasyChat.Views.Speech.SubtitleOverlayWindow");
                if (windowType != null)
                {
                    var window = (Avalonia.Controls.Window)Activator.CreateInstance(windowType)!;
                    window.DataContext = this;
                    
                    // Restore Size and Position
                    var config = _configurationService.SpeechRecognition;
                    if (config.WindowWidth > 0 && config.WindowHeight > 0)
                    {
                        window.Width = config.WindowWidth;
                        window.Height = config.WindowHeight;
                        window.SizeToContent = Avalonia.Controls.SizeToContent.Manual;
                    }
                    
                    if (config.WindowX >= 0 && config.WindowY >= 0)
                    {
                        window.Position = new Avalonia.PixelPoint((int)config.WindowX, (int)config.WindowY);
                        window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual;
                    }

                    // Save on Close
                    window.Closing += (s, e) => {
                         if (window.WindowState == Avalonia.Controls.WindowState.Normal)
                         {
                             config.WindowX = window.Position.X;
                             config.WindowY = window.Position.Y;
                             config.WindowWidth = window.Width;
                             config.WindowHeight = window.Height;
                         }
                    };

                    window.Closed += (s, e) => {
                         _floatingWindow = null;
                         IsFloatingWindowOpen = false;
                    };
                    window.Show();
                    _floatingWindow = window;
                    IsFloatingWindowOpen = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open floating window");
            }
        }
    }

    private void UpdateProcessesSummary()
    {
        var selected = Processes.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0 || selected.Any(p => p.Id == 0))
        {
            SelectedProcessesSummary = Lang.Resources.Speech_AllSystemAudio;
        }
        else if (selected.Count == 1)
        {
            SelectedProcessesSummary = selected[0].Name;
        }
        else
        {
            SelectedProcessesSummary = string.Format(Lang.Resources.Speech_SelectedAppsCount, selected.Count);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async System.Threading.Tasks.Task ToggleRecordingAsync()
    {
        if (IsBusy) return;

        if (IsRecording)
        {
             // Stop
            lock (_translationLock)
            {
                // Cancel all active translations
                foreach (var cts in _activeItemCts.Values)
                {
                    try { cts.Cancel(); cts.Dispose(); } catch {}
                }
                _activeItemCts.Clear();
                _activeItemLoops.Clear();
                _isProcessingStableForItem.Clear();
                
                _latestTextForItem.Clear();
            }

            await _speechRecognitionService.StopRecordingAsync();
        }
        else
        {
            // Start
            if (string.IsNullOrEmpty(SelectedRecognitionLanguage)) return;
            
            var lang = SelectedRecognitionLanguage;
            var selectedProcessIds = Processes.Where(p => p.IsSelected).Select(p => p.Id).ToList();

            var config = new SpeechRecognitionConfig
            {
                ModelPath = lang,
                ProcessIds = selectedProcessIds
            };

            await _speechRecognitionService.StartRecordingAsync(config);
        }
    }

    private void OnRecognitionResult(int type, string result)
    {
         if (!Dispatcher.UIThread.CheckAccess())
         {
             Dispatcher.UIThread.Post(() => OnRecognitionResult(type, result));
             return;
         }

         switch (type)
         {
             case 0: // Final
                 if (_currentSubtitleItem == null) CreateNewSubtitleItem();
                 _currentSubtitleItem!.OriginalText = result;
                
                 if (IsTranslationEnabled)
                 {
                     EnqueueTranslation(_currentSubtitleItem, result);
                 }
                
                 // Update Floating Subtitles (Move current to history if final)
                 UpdateFloatingSubtitles(_currentSubtitleItem, true);

                 _currentSubtitleItem = null; 
                 break;

             case 1: // Partial
                 if (_currentSubtitleItem == null) CreateNewSubtitleItem();
                 _currentSubtitleItem!.OriginalText = result;

                 if (IsTranslationEnabled)
                 {
                     EnqueueTranslation(_currentSubtitleItem, result);
                 }
                 
                 // Update Floating Subtitles (Update current)
                 UpdateFloatingSubtitles(_currentSubtitleItem, false);
                 break;

             case 2: // Error
                 break;
                
             case 3: // Stopped
                 IsRecording = false;
                 break;
                
             case 100: // Started
                 IsRecording = true;
                 FloatingSubtitles.Clear(); // Clear on start
                 break;
         }
    }

    private void UpdateFloatingSubtitles(SubtitleItem item, bool isFinal)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateFloatingSubtitles(item, isFinal));
            return;
        }

        if (!FloatingSubtitles.Contains(item))
        {
            FloatingSubtitles.Add(item);
        }

        // Rolling logic: Keep max 2 items.
        // If we have more than 2, remove the oldest.
        // Actually, the user wants: "Previous line" and "Current line".
        // When 'isFinal' is true, this item becomes "Previous" effectively for the next one.
        // So we just ensure list size <= 2.
        
        while (FloatingSubtitles.Count > 2)
        {
            FloatingSubtitles.RemoveAt(0);
        }
    }

    private void EnqueueTranslation(SubtitleItem item, string text)
    {
        lock (_translationLock)
        {
            _latestTextForItem[item] = text;
            
            // If loop is running, notify it (cancel current wait/streaming if not stable)
            if (_activeItemLoops.ContainsKey(item))
            {
                 if (!_isProcessingStableForItem.Contains(item))
                 {
                     try 
                     { 
                        _activeItemCts.GetValueOrDefault(item)?.Cancel(); 
                     }
                     catch { /* ignored */ }
                 }
                 return;
            }

            // Start new loop
            var task = Task.Run(() => ProcessItemLoopAsync(item));
            _activeItemLoops[item] = task;
        }
    }

    private async Task ProcessItemLoopAsync(SubtitleItem item)
    {
        while (IsRecording)
        {
            string text = "";
            CancellationTokenSource? cts = null;

            lock (_translationLock)
            {
                if (_latestTextForItem.TryGetValue(item, out var t))
                {
                    text = t;
                }
                else
                {
                    // Should not happen if logic is correct
                    break;
                }

                _activeItemCts.TryGetValue(item, out var oldCts);
                oldCts?.Dispose();
                
                cts = new CancellationTokenSource();
                _activeItemCts[item] = cts;
            }

            // Translate
            await TranslateSingleItemAsync(item, text, cts.Token);

            lock (_translationLock)
            {
                // Check if text changed during translation
                if (_latestTextForItem.TryGetValue(item, out var newText) && newText == text)
                {
                    // No new text, we are done with this item for now
                    _activeItemLoops.Remove(item);
                    _activeItemCts.Remove(item);
                    _latestTextForItem.Remove(item);
                    cts?.Dispose();
                    return; 
                }
                
                // If text changed, loop continues
            }
        }
        
        // Clean up if exiting while loop (stopped recording)
        lock (_translationLock)
        {
            _activeItemLoops.Remove(item);
            _activeItemCts.Remove(item);
        }
    }

    private async System.Threading.Tasks.Task TranslateSingleItemAsync(SubtitleItem item, string text, CancellationToken token)
    {
        if (SelectedEngineOption == null || SelectedTargetLanguage == null || string.IsNullOrWhiteSpace(text)) return;

        try
        {
            Dispatcher.UIThread.Post(() => item.IsTranslating = true);
            
            // Access properties on UI thread or assume safe if we use Invoke for updates? 
            // Better to read snaphost via Invoke to be 100% thread-safe regarding previous pending updates
            var confirmedOrig = "";
            Dispatcher.UIThread.Invoke(() => confirmedOrig = item.ConfirmedOriginalText);

            // Check consistency with confirmed text
            if (!text.StartsWith(confirmedOrig))
            {
                 // Reset if history doesn't match current text (ASR Correction)
                 Dispatcher.UIThread.Invoke(() => {
                     item.ConfirmedOriginalText = "";
                     item.ConfirmedTranslatedText = "";
                 });
                 confirmedOrig = "";
            }

            var delta = text.Substring(confirmedOrig.Length);
            if (string.IsNullOrEmpty(delta)) return;

            // Punctuation logic
            char[] punctuation = ['.', ',', '?', '!', ';', ':', '。', '，', '？', '！', '；', '：'];
            int lastPunctIdx = delta.LastIndexOfAny(punctuation);
            
            string stablePart = "";
            string unstablePart = delta;

            if (lastPunctIdx != -1)
            {
                stablePart = delta.Substring(0, lastPunctIdx + 1);
                unstablePart = delta.Substring(lastPunctIdx + 1);
            }

            // Services setup
            ITranslation service;
            if (SelectedEngineOption.IsMachine)
            {
                 service = _translationServiceFactory.CreateMachineService(SelectedEngineOption.Id);
            }
            else
            {
                 service = _translationServiceFactory.CreateAiServiceById(SelectedEngineOption.Id);
            }

            var sourceLang = MapModelToLanguage(SelectedRecognitionLanguage);
            var targetLang = SelectedTargetLanguage;

            // Create a timeout token to prevent indefinite hanging (e.g. network issues)
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            // 1. Handle Stable Part (if any)
            if (!string.IsNullOrEmpty(stablePart))
            {
                 lock(_translationLock) _isProcessingStableForItem.Add(item);
                 try
                 {
                     var sbStable = new StringBuilder();
                     var baseText = "";
                     Dispatcher.UIThread.Invoke(() => baseText = item.ConfirmedTranslatedText);
    
                     await foreach (var chunk in service.StreamTranslateAsync(stablePart, sourceLang, targetLang, linkedCts.Token))
                     {
                         if (linkedCts.Token.IsCancellationRequested) break;
                         sbStable.Append(chunk);
                         var currentStable = sbStable.ToString();
                         Dispatcher.UIThread.Post(() => item.TranslatedText = baseText + currentStable);
                     }
                     
                     if (token.IsCancellationRequested) return; // User cancelled/New text
                     if (timeoutCts.Token.IsCancellationRequested) 
                     {
                         _logger.LogWarning("Translation timed out for stable part.");
                         // Don't return, maybe try unstable or just finish to cleanup
                     }
    
                     var finalStable = sbStable.ToString();
                     if (!string.IsNullOrEmpty(finalStable)) // Only commit if we got something
                     {
                         Dispatcher.UIThread.Invoke(() => {
                             item.ConfirmedOriginalText += stablePart;
                             item.ConfirmedTranslatedText += finalStable;
                             item.TranslatedText = item.ConfirmedTranslatedText; 
                         });
                     }
                 }
                 finally
                 {
                     lock(_translationLock) _isProcessingStableForItem.Remove(item);
                 }
            }

            // 2. Handle Unstable Part (if any)
            if (IsRealTimePreviewEnabled && !string.IsNullOrEmpty(unstablePart))
            {
                var sb = new StringBuilder();
                string currentBase = "";
                Dispatcher.UIThread.Invoke(() => currentBase = item.ConfirmedTranslatedText);
                
                await foreach (var chunk in service.StreamTranslateAsync(unstablePart, sourceLang, targetLang, linkedCts.Token))
                {
                    if (linkedCts.Token.IsCancellationRequested) break;
                    
                    sb.Append(chunk);
                    var currentStreaming = sb.ToString();
                    Dispatcher.UIThread.Post(() => item.TranslatedText = currentBase + currentStreaming);
                }
            }
             
            _logger.LogDebug("Translation Finished for: {Text}", text.Substring(0, Math.Min(text.Length, 10)));
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Translation Canceled (New text arrived)");
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Translation Failed");
             if (!token.IsCancellationRequested)
             {
                 Dispatcher.UIThread.Post(() => 
                 {
                    // Fallback to showing what we have + error? Or just error.
                    // User wants to see partial translation usually.
                    // But if error, maybe just append error?
                    var current = item.TranslatedText;
                    item.TranslatedText = current + $" (Error: {ex.Message})";
                 });
             }
        }
        finally
        {
            Dispatcher.UIThread.Post(() => item.IsTranslating = false);
        }
    }

    private LanguageDefinition MapModelToLanguage(string modelName)
    {
        // Simple mapping based on known models in Lib
        if (string.IsNullOrEmpty(modelName)) return LanguageService.GetLanguage("auto");
        
        if (modelName.StartsWith("zh")) return LanguageService.GetLanguage(LanguageKeys.ChineseSimplifiedId);
        if (modelName.StartsWith("en")) return LanguageService.GetLanguage(LanguageKeys.EnglishId);
        if (modelName.StartsWith("ja")) return LanguageService.GetLanguage(LanguageKeys.JapaneseId);
        if (modelName.StartsWith("ko")) return LanguageService.GetLanguage(LanguageKeys.KoreanId);
        // Add more as needed or default to Auto
        return LanguageService.GetLanguage(LanguageKeys.AutoId); 
    }

    private void CreateNewSubtitleItem()
    {
        _currentSubtitleItem = new SubtitleItem
        {
            Timestamp = DateTime.Now.TimeOfDay,
            OriginalText = "..."
        };
        SubtitleItems.Add(_currentSubtitleItem);
        // Auto-scroll logic could be added here or in View
    }

    // --- P/Invoke Helpers ---
}
