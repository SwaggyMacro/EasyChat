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
using EasyChat.Common;
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
using Microsoft.Extensions.DependencyInjection;
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

    private List<string> _claudeModels;

    private List<string> _geminiModels;

    private List<string> _openaiModels;


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
        if (string.IsNullOrEmpty(GeneralConf?.UsingAiModelId) && !string.IsNullOrEmpty(GeneralConf?.UsingAiModel))
        {
            var match = AiModelConf?.ConfiguredModels.FirstOrDefault(m => m.Name == GeneralConf.UsingAiModel);
            if (match != null)
            {
                GeneralConf.UsingAiModelId = match.Id;
            }
        }
        
        // Ensure name is synced when ID changes (one-way sync for display/legacy)
        this.WhenAnyValue(x => x.GeneralConf!.UsingAiModelId)
            .Subscribe(id =>
            {
                if (!string.IsNullOrEmpty(id))
                {
                    var model = AiModelConf?.ConfiguredModels.FirstOrDefault(m => m.Id == id);
                    if (model != null && GeneralConf?.UsingAiModel != model.Name)
                    {
                        GeneralConf?.UsingAiModel = model.Name;
                    }
                }
            });

        // Migrate/Sync MachineTrans ID if missing
        if (string.IsNullOrEmpty(GeneralConf!.UsingMachineTransId) && !string.IsNullOrEmpty(GeneralConf.UsingMachineTrans))
        {
            var id = GeneralConf.UsingMachineTrans switch
            {
                Constant.MachineTranslationProviders.Baidu => MachineTransConf?.Baidu.Id,
                Constant.MachineTranslationProviders.Tencent => MachineTransConf?.Tencent.Id,
                Constant.MachineTranslationProviders.Google => MachineTransConf?.Google.Id,
                Constant.MachineTranslationProviders.DeepL => MachineTransConf?.DeepL.Id,
                _ => null
            };
            
            if (id != null)
            {
                GeneralConf.UsingMachineTransId = id;
            }
        }

        // Keep MachineTrans ID in sync when Name changes (User selection from UI)
        this.WhenAnyValue(x => x.GeneralConf!.UsingMachineTrans)
            .Subscribe(name =>
            {
                 if (!string.IsNullOrEmpty(name))
                 {
                     var id = name switch
                     {
                         Constant.MachineTranslationProviders.Baidu => MachineTransConf?.Baidu.Id,
                         Constant.MachineTranslationProviders.Tencent => MachineTransConf?.Tencent.Id,
                         Constant.MachineTranslationProviders.Google => MachineTransConf?.Google.Id,
                         Constant.MachineTranslationProviders.DeepL => MachineTransConf?.DeepL.Id,
                         _ => null
                     };
                     
                     if (id != null && GeneralConf.UsingMachineTransId != id)
                     {
                         GeneralConf.UsingMachineTransId = id;
                     }
                 }
            });

        // Watch for collection changes
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => AiModelConf?.ConfiguredModels.CollectionChanged += h,
                h => AiModelConf?.ConfiguredModels.CollectionChanged -= h)
            .Subscribe(_ => RefreshConfiguredModels());

        AddModelCommand = ReactiveCommand.Create(AddModel);
        EditModelCommand = ReactiveCommand.Create<CustomAiModel>(EditModel);
        DeleteModelCommand = ReactiveCommand.Create<CustomAiModel>(DeleteModel);

        EditModelKeysCommand = ReactiveCommand.Create<CustomAiModel>(EditModelKeys);
        EditBaiduKeysCommand = ReactiveCommand.Create(EditBaiduKeys);
        EditTencentKeysCommand = ReactiveCommand.Create(EditTencentKeys);
        EditGoogleKeysCommand = ReactiveCommand.Create(EditGoogleKeys);
        EditDeepLKeysCommand = ReactiveCommand.Create(EditDeepLKeys);
        
        TestAiModelConnectionCommand = ReactiveCommand.Create<CustomAiModel>(async void (model) => await TestAiModelConnection(model));
        TestBaiduConnectionCommand = ReactiveCommand.CreateFromTask(TestBaiduConnection);
        TestTencentConnectionCommand = ReactiveCommand.CreateFromTask(TestTencentConnection);
        TestGoogleConnectionCommand = ReactiveCommand.CreateFromTask(TestGoogleConnection);
        TestDeepLConnectionCommand = ReactiveCommand.CreateFromTask(TestDeepLConnection);
        
        ManageFixedAreasCommand = ReactiveCommand.Create(ManageFixedAreas);

        if (OperatingSystem.IsWindows())
            LoadAvailableFonts();
    }

    private void LoadAvailableFonts()
    {
        var fonts = Avalonia.Media.FontManager.Current.SystemFonts.OrderBy(x => x.Name).Select(x => x.Name);
        AvailableFonts = new ObservableCollection<string>(fonts);
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
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = ["Socks", "Http"];

    public List<string> DeepLModelTypes
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = ["quality_optimized", "prefer_quality_optimized", "latency_optimized"];

    private List<LanguageDefinition> _languages = [LanguageKeys.English, LanguageKeys.ChineseSimplified];

    public List<LanguageDefinition> Languages
    {
        get => _languages;
        set => this.RaiseAndSetIfChanged(ref _languages, value);
    }

    public LanguageDefinition? SelectedLanguage
    {
        get => Languages.FirstOrDefault(l => l.EnglishName == GeneralConf?.Language) ?? LanguageKeys.English;
        set
        {
            if (value != null && GeneralConf?.Language != value.EnglishName)
            {
                GeneralConf?.Language = value.EnglishName;
                this.RaisePropertyChanged();

                // Update Culture
                var culture = value.EnglishName switch
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

    public List<WindowClosingBehavior> ClosingBehaviors { get; } = Enum.GetValues<WindowClosingBehavior>().ToList();

    public WindowClosingBehavior SelectedClosingBehavior
    {
        get => GeneralConf!.ClosingBehavior;
        set
        {
            if (GeneralConf!.ClosingBehavior == value) return;
            GeneralConf.ClosingBehavior = value;
            this.RaisePropertyChanged();
        }
    }

    public List<string> ScreenshotModes { get; } = [Constant.ScreenshotMode.Precise, Constant.ScreenshotMode.Quick];

    public string SelectedScreenshotMode
    {
        get => ScreenshotConf?.Mode ?? Constant.ScreenshotMode.Precise;
        set
        {
            if (ScreenshotConf == null || ScreenshotConf.Mode == value) return;
            ScreenshotConf.Mode = value;
            this.RaisePropertyChanged();
        }
    }

    public List<string> AiProviders
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = ["OpenAI", "Gemini", "Claude"];

    public List<string> MachineTransProviders
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = [Constant.MachineTranslationProviders.Baidu, Constant.MachineTranslationProviders.Tencent, Constant.MachineTranslationProviders.Google, Constant.MachineTranslationProviders.DeepL];

    public General? GeneralConf => _configurationService.General;

    public AiModel? AiModelConf => _configurationService.AiModel;

    public ObservableCollection<CustomAiModel> ConfiguredModels
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = new();

    public ObservableCollection<ModelCardItem> ModelCardsWithAddButton
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = new();

    public MachineTrans? MachineTransConf => _configurationService.MachineTrans;

    public Proxy? ProxyConf => _configurationService.Proxy;

    public ResultConfig? ResultConf => _configurationService.Result;
    
    public InputConfig? InputConf => _configurationService.Input;
    
    public ScreenshotConfig? ScreenshotConf => _configurationService.Screenshot;
    
    public List<string> TransparencyLevels { get; } = ["AcrylicBlur", "Blur", "Transparent"];
    
    private ObservableCollection<string> _availableFonts = [];
    public ObservableCollection<string> AvailableFonts
    {
        get => _availableFonts;
        set => this.RaiseAndSetIfChanged(ref _availableFonts, value);
    }
    
    public List<InputDeliveryMode> InputDeliveryModes { get; } = Enum.GetValues<InputDeliveryMode>().ToList();

    public ReactiveCommand<Unit, Unit> ManageFixedAreasCommand { get; }

    private void ManageFixedAreas()
    {
        if (Global.Services == null) return;
        
        _dialogManager.CreateDialog()
            .WithTitle(Resources.FixedAreas)
            .WithViewModel(dialog => new FixedAreaEditDialogViewModel(
                _dialogManager, 
                dialog, 
                _configurationService, 
                Global.Services.GetRequiredService<IScreenCaptureService>()))
            .TryShow();
    }

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
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsTestingTencent
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsTestingGoogle
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsTestingDeepL
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
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
        if (MachineTransConf != null)
            ShowKeyEditor(Resources.Baidu, MachineTransConf.Baidu.Items, KeyListType.Baidu, items =>
            {
                MachineTransConf.Baidu.Items.Clear();
                foreach (var item in items.Cast<MachineTrans.BaiduItem>()) MachineTransConf.Baidu.Items.Add(item);
            });
    }

    private void EditTencentKeys()
    {
        if (MachineTransConf != null)
            ShowKeyEditor(Resources.Tencent, MachineTransConf.Tencent.Items, KeyListType.Tencent, items =>
            {
                MachineTransConf.Tencent.Items.Clear();
                foreach (var item in items.Cast<MachineTrans.TencentItem>()) MachineTransConf.Tencent.Items.Add(item);
            });
    }

    private void EditGoogleKeys()
    {
        if (MachineTransConf != null)
            ShowKeyEditor(Resources.Google, MachineTransConf.Google.ApiKeys, KeyListType.String, keys =>
            {
                MachineTransConf.Google.ApiKeys.Clear();
                foreach (var key in keys.Cast<string>()) MachineTransConf.Google.ApiKeys.Add(key);
            });
    }

    private void EditDeepLKeys()
    {
        if (MachineTransConf != null)
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
                var vm = new KeyListEditorViewModel(dialog, title, existing.Cast<object>(), type)
                {
                    OnSave = onSave
                };
                return vm;
            })
            .TryShow();
    }

    private void RefreshConfiguredModels()
    {
        if (AiModelConf == null) return;
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
                var vm = new AiModelEditDialogViewModel(dialog)
                {
                    OnClose = result =>
                    {
                        if (result != null) AiModelConf?.ConfiguredModels.Add(result);
                        // Refresh handled by subscription
                        // Save handled by service
                    }
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
                var vm = new AiModelEditDialogViewModel(dialog, model)
                {
                    OnClose = result =>
                    {
                        if (result != null)
                        {
                            var existing = AiModelConf?.ConfiguredModels.FirstOrDefault(m => m.Id == model.Id);
                            if (existing == null) return;
                            if (AiModelConf == null) return;
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
                var toRemove = AiModelConf?.ConfiguredModels.FirstOrDefault(m => m.Id == model.Id);
                if (toRemove != null) AiModelConf?.ConfiguredModels.Remove(toRemove);
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
            var proxy = model.UseProxy ? ProxyConf?.ProxyUrl : null;
            var loggerFactory = NullLoggerFactory.Instance;
            var logger = loggerFactory.CreateLogger<OpenAiService>();

            var prompt = _configurationService.Prompts?.ActivePromptContent ?? Prompts.DefaultPromptContent;
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
        if (MachineTransConf is { Baidu.Items.Count: 0 })
        {
            ShowConnectionResult(Resources.Baidu, false, "No API key configured");
            return;
        }
        
        IsTestingBaidu = true;
        try
        {
            if (MachineTransConf != null)
            {
                var item = MachineTransConf.Baidu.Items[0];
                var proxy = MachineTransConf.Baidu.UseProxy ? ProxyConf?.ProxyUrl : null;
                var loggerFactory = NullLoggerFactory.Instance;
                var logger = loggerFactory.CreateLogger<BaiduService>();
            
                using var service = new BaiduService(item.AppId, item.AppKey, proxy, logger);
                var source = LanguageService.GetLanguage("en");
                var target = LanguageService.GetLanguage("zh-Hans");
                await service.TranslateAsync("Hello", source, target);
            }

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
        if (MachineTransConf is { Tencent.Items.Count: 0 })
        {
            ShowConnectionResult(Resources.Tencent, false, "No API key configured");
            return;
        }
        
        IsTestingTencent = true;
        try
        {
            if (MachineTransConf != null)
            {
                var item = MachineTransConf.Tencent.Items[0];
                var proxy = MachineTransConf.Tencent.UseProxy ? ProxyConf?.ProxyUrl : null;
                var loggerFactory = NullLoggerFactory.Instance;
                var logger = loggerFactory.CreateLogger<TencentService>();
            
                using var service = new TencentService(item.SecretId, item.SecretKey, proxy, logger);
                var source = LanguageService.GetLanguage("en");
                var target = LanguageService.GetLanguage("zh-Hans");
                await service.TranslateAsync("Hello", source, target);
            }

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
        if (MachineTransConf is { Google.ApiKeys.Count: 0 })
        {
            ShowConnectionResult(Resources.Google, false, "No API key configured");
            return;
        }
        
        IsTestingGoogle = true;
        try
        {
            var proxy = MachineTransConf is { Google.UseProxy: true } ? ProxyConf?.ProxyUrl : null;
            var loggerFactory = NullLoggerFactory.Instance;
            var logger = loggerFactory.CreateLogger<GoogleService>();

            if (MachineTransConf != null)
            {
                using var service = new GoogleService(
                    MachineTransConf.Google.Model,
                    MachineTransConf.Google.ApiKeys[0], 
                    proxy, 
                    logger);
                var source = LanguageService.GetLanguage("en");
                var target = LanguageService.GetLanguage("zh-Hans");
                await service.TranslateAsync("Hello", source, target);
            }

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
        if (MachineTransConf is { DeepL.ApiKeys.Count: 0 })
        {
            ShowConnectionResult(Resources.DeepL, false, "No API key configured");
            return;
        }
        
        IsTestingDeepL = true;
        try
        {
            var proxy = MachineTransConf is { DeepL.UseProxy: true } ? ProxyConf?.ProxyUrl : null;
            var loggerFactory = NullLoggerFactory.Instance;
            var logger = loggerFactory.CreateLogger<DeepLService>();

            if (MachineTransConf != null)
            {
                var service = new DeepLService(
                    MachineTransConf.DeepL.ModelType,
                    MachineTransConf.DeepL.ApiKeys[0], 
                    proxy, 
                    logger);
                var source = LanguageService.GetLanguage("en");
                var target = LanguageService.GetLanguage("zh-Hans");
                await service.TranslateAsync("Hello", source, target);
            }

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