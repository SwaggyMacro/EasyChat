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
    private readonly IConfigurationService _configurationService;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly ISpeechRecognitionService _speechRecognitionService;
    private readonly IProcessService _processService;
    private readonly ILogger<SpeechRecognitionViewModel> _logger;
    private SubtitleItem? _currentSubtitleItem;
    
    // Translation Queue
    private readonly object _translationLock = new();
    private readonly Dictionary<SubtitleItem, string> _latestTextForItem = new();
    private readonly Queue<SubtitleItem> _itemsToProcess = new();
    private bool _isProcessingQueue;
    
    // Logic for cancelling active translation if updated
    private SubtitleItem? _currentlyProcessingItem;
    private CancellationTokenSource? _currentTranslationCts;
    private bool _isProcessingStable;

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
                var savedEngine = EngineOptions.FirstOrDefault(e => e.Id == config.EngineId);
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
            this.WhenAnyValue(x => x.SelectedEngineOption).Where(x => x != null).Subscribe(v => config.EngineId = v!.Id);
            this.WhenAnyValue(x => x.SelectedTargetLanguage).Where(x => x != null).Subscribe(v => config.TargetLanguage = v!.Id);
            
            // Sync initial processes

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
    
    private bool _isTranslationEnabled = true;
    public bool IsTranslationEnabled
    {
         get => _isTranslationEnabled;
         set => this.RaiseAndSetIfChanged(ref _isTranslationEnabled, value);
    }

    private bool _isRealTimePreviewEnabled;
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
    
    // Recording Logic
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    private bool _isRecording;
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

    public ObservableCollection<ProcessInfo> Processes => _processService.Processes;
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshProcessesCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleRecordingCommand { get; }

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
                _itemsToProcess.Clear();
                _latestTextForItem.Clear();
                try { _currentTranslationCts?.Cancel(); _currentTranslationCts?.Dispose(); }
                catch
                {
                    // ignored
                }

                _currentTranslationCts = null;
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
                
                 _currentSubtitleItem = null; 
                 break;

             case 1: // Partial
                 if (_currentSubtitleItem == null) CreateNewSubtitleItem();
                 _currentSubtitleItem!.OriginalText = result;

                 if (IsTranslationEnabled)
                 {
                     EnqueueTranslation(_currentSubtitleItem, result);
                 }
                 break;

             case 2: // Error
                 break;
                
             case 3: // Stopped
                 IsRecording = false;
                 break;
                
             case 100: // Started
                 IsRecording = true;
                 break;
         }
    }

    private void EnqueueTranslation(SubtitleItem item, string text)
    {
        lock (_translationLock)
        {
            _latestTextForItem[item] = text;
            if (!_itemsToProcess.Contains(item))
            {
                _itemsToProcess.Enqueue(item);
            }
            
            // If currently processing THIS item, cancel it so we can start over with new text immediately
            // BUT: If processing a "Stable" (locked) part, do NOT cancel. Let it finish to avoid starvation.
            if (_currentlyProcessingItem == item && !_isProcessingStable)
            {
                try { _currentTranslationCts?.Cancel(); }
                catch
                {
                    // ignored
                }
            }
            
            if (!_isProcessingQueue)
            {
                _isProcessingQueue = true;
                _ = ProcessQueueAsync();
            }
        }
    }

    private async System.Threading.Tasks.Task ProcessQueueAsync()
    {
        try
        {
            while (IsRecording) 
            {
                SubtitleItem? item;
                string text;
                CancellationToken token;

                lock (_translationLock)
                {
                    if (_itemsToProcess.Count == 0)
                    {
                        _isProcessingQueue = false;
                        return;
                    }

                    item = _itemsToProcess.Peek();
                    if (_latestTextForItem.ContainsKey(item))
                    {
                        text = _latestTextForItem[item];
                    }
                    else
                    {
                        _itemsToProcess.Dequeue();
                        continue;
                    }

                    // Setup for processing
                    _currentlyProcessingItem = item;
                    _currentTranslationCts?.Dispose();
                    _currentTranslationCts = new CancellationTokenSource();
                    token = _currentTranslationCts.Token;
                }

                // Perform Translation (Outside Lock)
                // We pass the token to allow cancellation if EnqueueTranslation fires for this item
                await TranslateSingleItemAsync(item, text, token);

                lock (_translationLock)
                {
                    // Clean up current
                    _currentlyProcessingItem = null;
                    
                    // Check if text is still the same as what we just translated
                    // If cancellation happened, we might have new text waiting in _latestTextForItem
                    // We don't remove from queue if it changed.
                    
                    if (_latestTextForItem.ContainsKey(item) && _latestTextForItem[item] == text)
                    {
                        // Done with this version of text
                         _itemsToProcess.Dequeue();
                         _latestTextForItem.Remove(item);
                    }
                    
                    // If text changed (and we cancelled), the item is still in queue (Peek).
                    // Next loop will pick it up with new text.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation Queue Error");
        }
        finally
        {
            lock (_translationLock) _isProcessingQueue = false;
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

            // 1. Handle Stable Part (if any)
            if (!string.IsNullOrEmpty(stablePart))
            {
                 lock(_translationLock) _isProcessingStable = true;
                 try
                 {
                     var sbStable = new StringBuilder();
                     var baseText = "";
                     Dispatcher.UIThread.Invoke(() => baseText = item.ConfirmedTranslatedText);
    
                     await foreach (var chunk in service.StreamTranslateAsync(stablePart, sourceLang, targetLang, token))
                     {
                         if (token.IsCancellationRequested) break;
                         sbStable.Append(chunk);
                         var currentStable = sbStable.ToString();
                         Dispatcher.UIThread.Post(() => item.TranslatedText = baseText + currentStable);
                     }
                     
                     if (token.IsCancellationRequested) return;
    
                     var finalStable = sbStable.ToString();
                     Dispatcher.UIThread.Invoke(() => {
                         item.ConfirmedOriginalText += stablePart;
                         item.ConfirmedTranslatedText += finalStable;
                         item.TranslatedText = item.ConfirmedTranslatedText; 
                     });
                 }
                 finally
                 {
                     lock(_translationLock) _isProcessingStable = false;
                 }
            }

            // 2. Handle Unstable Part (if any)
            if (IsRealTimePreviewEnabled && !string.IsNullOrEmpty(unstablePart))
            {
                var sb = new StringBuilder();
                string currentBase = "";
                Dispatcher.UIThread.Invoke(() => currentBase = item.ConfirmedTranslatedText);
                
                await foreach (var chunk in service.StreamTranslateAsync(unstablePart, sourceLang, targetLang, token))
                {
                    if (token.IsCancellationRequested) break;
                    
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
