using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using EasyChat.Constants;
using EasyChat.Lang;
using EasyChat.Services.Languages;
using EasyChat.Models;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Translation.Ai;
using EasyChat.Services.Translation.Machine;
using EasyChat.ViewModels.AiModels;
using EasyChat.ViewModels.Dialogs;
using Material.Icons;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using SukiUI.Dialogs;
using SukiUI.Toasts;

// Added for Global

namespace EasyChat.ViewModels.Pages;

[SuppressMessage("ReSharper", "NotAccessedField.Local")]
public class SettingViewModel : Page
{
    private readonly IConfigurationService _configurationService;
    private readonly ISukiDialogManager _dialogManager;

    private List<string> _aiProviders = ["OpenAI", "Gemini", "Claude"];

    private List<string> _claudeModels;

    private ObservableCollection<CustomAiModel> _configuredModels = new();

    private List<string> _deepLModelTypes = ["quality_optimized", "prefer_quality_optimized", "latency_optimized"];

    private List<string> _geminiModels;

    private List<string> _languages = ["English", "Simplified Chinese"];

    private List<string> _machineTransProviders = ["Baidu", "Tencent", "Google", "DeepL"];

    // Combined list with models + Add button placeholder using wrapper
    private ObservableCollection<ModelCardItem> _modelCardsWithAddButton = new();

    private List<string> _openaiModels;

    private List<string> _proxyTypes = ["Socks", "Http"];
    
    // Testing state for each service
    private bool _isTestingBaidu;
    private bool _isTestingTencent;
    private bool _isTestingGoogle;
    private bool _isTestingDeepL;


    public SettingViewModel(ISukiDialogManager dialogManager, IConfigurationService configurationService) : base(
        Resources.Settings, MaterialIconKind.Settings, 1)
    {
        _dialogManager = dialogManager;
        _configurationService = configurationService;
        _openaiModels = ModelList.OpenAiModels;
        _geminiModels = ModelList.GeminiModels;
        _claudeModels = ModelList.ClaudeModels;

        // Initialize ConfiguredModels wrapper
        RefreshConfiguredModels();

        // Migrate/Sync ID if missing
        if (string.IsNullOrEmpty(GeneralConf.UsingAiModelId) && !string.IsNullOrEmpty(GeneralConf.UsingAiModel))
        {
            var match = AiModelConf.ConfiguredModels.FirstOrDefault(m => m.Name == GeneralConf.UsingAiModel);
            if (match != null)
            {
                GeneralConf.UsingAiModelId = match.Id;
            }
        }
        
        // Ensure name is synced when ID changes (one-way sync for display/legacy)
        this.WhenAnyValue(x => x.GeneralConf.UsingAiModelId)
            .Subscribe(id =>
            {
                if (!string.IsNullOrEmpty(id))
                {
                    var model = AiModelConf.ConfiguredModels.FirstOrDefault(m => m.Id == id);
                    if (model != null && GeneralConf.UsingAiModel != model.Name)
                    {
                        GeneralConf.UsingAiModel = model.Name;
                    }
                }
            });

        // Watch for collection changes
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => AiModelConf.ConfiguredModels.CollectionChanged += h,
                h => AiModelConf.ConfiguredModels.CollectionChanged -= h)
            .Subscribe(_ => RefreshConfiguredModels());

        AddModelCommand = ReactiveCommand.Create(AddModel);
        EditModelCommand = ReactiveCommand.Create<CustomAiModel>(EditModel);
        DeleteModelCommand = ReactiveCommand.Create<CustomAiModel>(DeleteModel);

        EditModelKeysCommand = ReactiveCommand.Create<CustomAiModel>(EditModelKeys);
        EditBaiduKeysCommand = ReactiveCommand.Create(EditBaiduKeys);
        EditTencentKeysCommand = ReactiveCommand.Create(EditTencentKeys);
        EditGoogleKeysCommand = ReactiveCommand.Create(EditGoogleKeys);
        EditDeepLKeysCommand = ReactiveCommand.Create(EditDeepLKeys);
        
        TestAiModelConnectionCommand = ReactiveCommand.Create<CustomAiModel>(async model => await TestAiModelConnection(model));
        TestBaiduConnectionCommand = ReactiveCommand.CreateFromTask(TestBaiduConnection);
        TestTencentConnectionCommand = ReactiveCommand.CreateFromTask(TestTencentConnection);
        TestGoogleConnectionCommand = ReactiveCommand.CreateFromTask(TestGoogleConnection);
        TestDeepLConnectionCommand = ReactiveCommand.CreateFromTask(TestDeepLConnection);

    }

    public List<string> OpenaiModels
    {
        get => _openaiModels;
        set => this.RaiseAndSetIfChanged(ref _openaiModels, value);
    }

    public List<string> GeminiModels
    {
        get => _geminiModels;
        set => this.RaiseAndSetIfChanged(ref _geminiModels, value);
    }

    public List<string> ClaudeModels
    {
        get => _claudeModels;
        set => this.RaiseAndSetIfChanged(ref _claudeModels, value);
    }

    public List<string> ProxyTypes
    {
        get => _proxyTypes;
        set => this.RaiseAndSetIfChanged(ref _proxyTypes, value);
    }

    public List<string> DeepLModelTypes
    {
        get => _deepLModelTypes;
        set => this.RaiseAndSetIfChanged(ref _deepLModelTypes, value);
    }

    public List<string> Languages
    {
        get => _languages;
        set => this.RaiseAndSetIfChanged(ref _languages, value);
    }

    public string SelectedLanguage
    {
        get => GeneralConf.Language;
        set
        {
            if (GeneralConf.Language != value)
            {
                GeneralConf.Language = value;
                this.RaisePropertyChanged();

                // Update Culture
                var culture = value switch
                {
                    "Simplified Chinese" => new CultureInfo("zh-CN"),
                    _ => new CultureInfo("en-US")
                };
                Resources.Culture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;

                // Save Config handled by service automatically

                // Notify User
                var title = Resources.LanguageChanged;
                var content = Resources.RestartToTakeEffect;

                Global.ToastManager.CreateSimpleInfoToast()
                    .OfType(NotificationType.Success)
                    .WithTitle(title)
                    .WithContent(content)
                    .Queue();
            }
        }
    }

    public List<string> AiProviders
    {
        get => _aiProviders;
        set => this.RaiseAndSetIfChanged(ref _aiProviders, value);
    }

    public List<string> MachineTransProviders
    {
        get => _machineTransProviders;
        set => this.RaiseAndSetIfChanged(ref _machineTransProviders, value);
    }

    public General GeneralConf => _configurationService.General;

    public AiModel AiModelConf => _configurationService.AiModel;

    public ObservableCollection<CustomAiModel> ConfiguredModels
    {
        get => _configuredModels;
        set => this.RaiseAndSetIfChanged(ref _configuredModels, value);
    }

    public ObservableCollection<ModelCardItem> ModelCardsWithAddButton
    {
        get => _modelCardsWithAddButton;
        set => this.RaiseAndSetIfChanged(ref _modelCardsWithAddButton, value);
    }

    public MachineTrans MachineTransConf => _configurationService.MachineTrans;

    public Proxy ProxyConf => _configurationService.Proxy;

    public ResultConfig ResultConf => _configurationService.Result;
    
    public InputConfig InputConf => _configurationService.Input;
    
    public List<string> TransparencyLevels { get; } = ["AcrylicBlur", "Blur", "Transparent"];
    
    public List<InputDeliveryMode> InputDeliveryModes { get; } = Enum.GetValues<InputDeliveryMode>().ToList();

    public ReactiveCommand<Unit, Unit> AddModelCommand { get; }
    public ReactiveCommand<CustomAiModel, Unit> EditModelCommand { get; }
    public ReactiveCommand<CustomAiModel, Unit> DeleteModelCommand { get; }

    public ReactiveCommand<CustomAiModel, Unit> EditModelKeysCommand { get; }
    public ReactiveCommand<Unit, Unit> EditBaiduKeysCommand { get; }
    public ReactiveCommand<Unit, Unit> EditTencentKeysCommand { get; }
    public ReactiveCommand<Unit, Unit> EditGoogleKeysCommand { get; }
    public ReactiveCommand<Unit, Unit> EditDeepLKeysCommand { get; }
    
    public ReactiveCommand<CustomAiModel, Unit> TestAiModelConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> TestBaiduConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> TestTencentConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> TestGoogleConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> TestDeepLConnectionCommand { get; }
    
    // Testing state properties for loading indicators
    public bool IsTestingBaidu
    {
        get => _isTestingBaidu;
        set => this.RaiseAndSetIfChanged(ref _isTestingBaidu, value);
    }
    
    public bool IsTestingTencent
    {
        get => _isTestingTencent;
        set => this.RaiseAndSetIfChanged(ref _isTestingTencent, value);
    }
    
    public bool IsTestingGoogle
    {
        get => _isTestingGoogle;
        set => this.RaiseAndSetIfChanged(ref _isTestingGoogle, value);
    }
    
    public bool IsTestingDeepL
    {
        get => _isTestingDeepL;
        set => this.RaiseAndSetIfChanged(ref _isTestingDeepL, value);
    }
    


    private void EditModelKeys(CustomAiModel model)
    {
        ShowKeyEditor(model.Name + " API Keys", model.ApiKeys, KeyListType.String, keys =>
        {
            model.ApiKeys.Clear();
            foreach (var key in keys.Cast<string>()) model.ApiKeys.Add(key);
        });
    }

    private void EditBaiduKeys()
    {
        ShowKeyEditor(Resources.Baidu, MachineTransConf.Baidu.Items, KeyListType.Baidu, items =>
        {
            MachineTransConf.Baidu.Items.Clear();
            foreach (var item in items.Cast<MachineTrans.BaiduItem>()) MachineTransConf.Baidu.Items.Add(item);
        });
    }

    private void EditTencentKeys()
    {
        ShowKeyEditor(Resources.Tencent, MachineTransConf.Tencent.Items, KeyListType.Tencent, items =>
        {
            MachineTransConf.Tencent.Items.Clear();
            foreach (var item in items.Cast<MachineTrans.TencentItem>()) MachineTransConf.Tencent.Items.Add(item);
        });
    }

    private void EditGoogleKeys()
    {
        ShowKeyEditor(Resources.Google, MachineTransConf.Google.ApiKeys, KeyListType.String, keys =>
        {
            MachineTransConf.Google.ApiKeys.Clear();
            foreach (var key in keys.Cast<string>()) MachineTransConf.Google.ApiKeys.Add(key);
        });
    }

    private void EditDeepLKeys()
    {
        ShowKeyEditor(Resources.DeepL, MachineTransConf.DeepL.ApiKeys, KeyListType.String, keys =>
        {
            MachineTransConf.DeepL.ApiKeys.Clear();
            foreach (var key in keys.Cast<string>()) MachineTransConf.DeepL.ApiKeys.Add(key);
        });
    }

    private void ShowKeyEditor(string title, IEnumerable existing, KeyListType type, Action<IEnumerable<object>> onSave)
    {
        _dialogManager.CreateDialog()
            .WithTitle(title)
            .WithViewModel(dialog =>
            {
                var vm = new KeyListEditorViewModel(dialog, title, existing.Cast<object>(), type);
                vm.OnSave = onSave;
                return vm;
            })
            .TryShow();
    }

    private void RefreshConfiguredModels()
    {
        ConfiguredModels = new ObservableCollection<CustomAiModel>(AiModelConf.ConfiguredModels);
        // Update AiProviders list based on configured models
        AiProviders = ConfiguredModels.Select(m => m.Name).ToList();
        // Create combined list with ModelCardItem wrapper for proper template switching
        var combined = AiModelConf.ConfiguredModels
            .Select(m => new ModelCardItem { Model = m })
            .ToList();
        combined.Add(new ModelCardItem { Model = null }); // Add button placeholder
        ModelCardsWithAddButton = new ObservableCollection<ModelCardItem>(combined);
    }

    private void AddModel()
    {
        _dialogManager.CreateDialog()
            .WithTitle(Resources.AddModel)
            .WithViewModel(dialog =>
            {
                var vm = new AiModelEditDialogViewModel(dialog);
                vm.OnClose = result =>
                {
                    if (result != null) AiModelConf.ConfiguredModels.Add(result);
                    // Refresh handled by subscription
                    // Save handled by service
                };
                return vm;
            })
            .TryShow();
    }

    private void EditModel(CustomAiModel model)
    {
        _dialogManager.CreateDialog()
            .WithTitle(Resources.EditModel)
            .WithViewModel(dialog =>
            {
                var vm = new AiModelEditDialogViewModel(dialog, model);
                vm.OnClose = result =>
                {
                    if (result != null)
                    {
                        var existing = AiModelConf.ConfiguredModels.FirstOrDefault(m => m.Id == model.Id);
                        if (existing != null)
                        {
                            var index = AiModelConf.ConfiguredModels.IndexOf(existing);
                            AiModelConf.ConfiguredModels[index] = result;
                            // Refresh handled by subscription if collection changes or if we manually trigger 
                            // Since we replace the item, CollectionChanged (Replace) should fire.
                            // Save handled by service
                        }
                    }
                };
                return vm;
            })
            .TryShow();
    }

    private void DeleteModel(CustomAiModel model)
    {
        _dialogManager.CreateDialog()
            .WithTitle(Resources.ConfirmDeletion)
            .WithContent(Resources.ConfirmDeleteModel)
            .OfType(NotificationType.Warning)
            .WithActionButton(Resources.Delete, _ =>
            {
                // RemoveAll works on List<T>, but ObservableCollection doesn't have it directly.
                var toRemove = AiModelConf.ConfiguredModels.FirstOrDefault(m => m.Id == model.Id);
                if (toRemove != null) AiModelConf.ConfiguredModels.Remove(toRemove);
                // Refresh handled by subscription
                // Save handled by service
            }, true, "Flat", "Danger")
            .WithActionButton(Resources.Cancel, _ => { }, true, "")
            .TryShow();
    }
    
    private async Task TestAiModelConnection(CustomAiModel model)
    {
        if (model.ApiKeys.Count == 0)
        {
            ShowConnectionResult(model.Name, false, "No API key configured");
            return;
        }
        
        model.IsTesting = true;
        try
        {
            var proxy = model.UseProxy ? ProxyConf.ProxyUrl : null;
            var loggerFactory = NullLoggerFactory.Instance;
            var logger = loggerFactory.CreateLogger<OpenAiService>();
            
            var prompt = _configurationService.Prompts.ActivePromptContent;
            var service = new OpenAiService(model.ApiUrl, model.ApiKey, model.Model, proxy, prompt, logger);
            var source = LanguageService.GetLanguage("en");
            var target = LanguageService.GetLanguage("zh-Hans");
            await service.TranslateAsync("Hello", source, target);
            ShowConnectionResult(model.Name, true);
        }
        catch (Exception ex)
        {
            ShowConnectionResult(model.Name, false, ex.Message);
        }
        finally
        {
            model.IsTesting = false;
        }
    }
    
    private async Task TestBaiduConnection()
    {
        if (MachineTransConf.Baidu.Items.Count == 0)
        {
            ShowConnectionResult(Resources.Baidu, false, "No API key configured");
            return;
        }
        
        IsTestingBaidu = true;
        try
        {
            var item = MachineTransConf.Baidu.Items[0];
            var proxy = MachineTransConf.Baidu.UseProxy ? ProxyConf.ProxyUrl : null;
            var loggerFactory = NullLoggerFactory.Instance;
            var logger = loggerFactory.CreateLogger<BaiduService>();
            
            using var service = new BaiduService(item.AppId, item.AppKey, proxy, logger);
            var source = LanguageService.GetLanguage("en");
            var target = LanguageService.GetLanguage("zh-Hans");
            await service.TranslateAsync("Hello", source, target);
            ShowConnectionResult(Resources.Baidu, true);
        }
        catch (Exception ex)
        {
            ShowConnectionResult(Resources.Baidu, false, ex.Message);
        }
        finally
        {
            IsTestingBaidu = false;
        }
    }
    
    private async Task TestTencentConnection()
    {
        if (MachineTransConf.Tencent.Items.Count == 0)
        {
            ShowConnectionResult(Resources.Tencent, false, "No API key configured");
            return;
        }
        
        IsTestingTencent = true;
        try
        {
            var item = MachineTransConf.Tencent.Items[0];
            var proxy = MachineTransConf.Tencent.UseProxy ? ProxyConf.ProxyUrl : null;
            var loggerFactory = NullLoggerFactory.Instance;
            var logger = loggerFactory.CreateLogger<TencentService>();
            
            using var service = new TencentService(item.SecretId, item.SecretKey, proxy, logger);
            var source = LanguageService.GetLanguage("en");
            var target = LanguageService.GetLanguage("zh-Hans");
            await service.TranslateAsync("Hello", source, target);
            ShowConnectionResult(Resources.Tencent, true);
        }
        catch (Exception ex)
        {
            ShowConnectionResult(Resources.Tencent, false, ex.Message);
        }
        finally
        {
            IsTestingTencent = false;
        }
    }
    
    private async Task TestGoogleConnection()
    {
        if (MachineTransConf.Google.ApiKeys.Count == 0)
        {
            ShowConnectionResult(Resources.Google, false, "No API key configured");
            return;
        }
        
        IsTestingGoogle = true;
        try
        {
            var proxy = MachineTransConf.Google.UseProxy ? ProxyConf.ProxyUrl : null;
            var loggerFactory = NullLoggerFactory.Instance;
            var logger = loggerFactory.CreateLogger<GoogleService>();
            
            using var service = new GoogleService(
                MachineTransConf.Google.Model,
                MachineTransConf.Google.ApiKeys[0], 
                proxy, 
                logger);
            var source = LanguageService.GetLanguage("en");
            var target = LanguageService.GetLanguage("zh-Hans");
            await service.TranslateAsync("Hello", source, target);
            ShowConnectionResult(Resources.Google, true);
        }
        catch (Exception ex)
        {
            ShowConnectionResult(Resources.Google, false, ex.Message);
        }
        finally
        {
            IsTestingGoogle = false;
        }
    }
    
    private async Task TestDeepLConnection()
    {
        if (MachineTransConf.DeepL.ApiKeys.Count == 0)
        {
            ShowConnectionResult(Resources.DeepL, false, "No API key configured");
            return;
        }
        
        IsTestingDeepL = true;
        try
        {
            var proxy = MachineTransConf.DeepL.UseProxy ? ProxyConf.ProxyUrl : null;
            var loggerFactory = NullLoggerFactory.Instance;
            var logger = loggerFactory.CreateLogger<DeepLService>();
            
            var service = new DeepLService(
                MachineTransConf.DeepL.ModelType,
                MachineTransConf.DeepL.ApiKeys[0], 
                proxy, 
                logger);
            var source = LanguageService.GetLanguage("en");
            var target = LanguageService.GetLanguage("zh-Hans");
            await service.TranslateAsync("Hello", source, target);
            ShowConnectionResult(Resources.DeepL, true);
        }
        catch (Exception ex)
        {
            ShowConnectionResult(Resources.DeepL, false, ex.Message);
        }
        finally
        {
            IsTestingDeepL = false;
        }
    }
    
    private void ShowConnectionResult(string serviceName, bool success, string? errorMessage = null)
    {
        if (success)
        {
            Global.ToastManager.CreateSimpleInfoToast()
                .OfType(NotificationType.Success)
                .WithTitle(serviceName)
                .WithContent(Resources.ConnectionSuccess)
                .Queue();
        }
        else
        {
            Global.ToastManager.CreateSimpleInfoToast()
                .OfType(NotificationType.Error)
                .WithTitle(Resources.ConnectionFailed)
                .WithContent($"{serviceName}: {errorMessage}")
                .Queue();
        }
    }
}