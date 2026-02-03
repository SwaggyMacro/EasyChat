using System;
using System.Collections.Specialized;
using System.Reactive.Linq;
using EasyChat.Common;
using EasyChat.Constants;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace EasyChat.Services;

public class ConfigurationService : ReactiveObject, IConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;

    public ConfigurationService(ILogger<ConfigurationService> logger)
    {
        _logger = logger;
        _logger.LogInformation("Loading configurations...");
        
        // Load configurations
        General = ConfigUtil.LoadConfig<General>(Constant.General);
        AiModel = ConfigUtil.LoadConfig<AiModel>(Constant.AiModelConf);
        MachineTrans = ConfigUtil.LoadConfig<MachineTrans>(Constant.MachineTransConf);
        Proxy = ConfigUtil.LoadConfig<Proxy>(Constant.ProxyConf);
        Shortcut = ConfigUtil.LoadConfig<Shortcut>(Constant.ShortcutConf);
        Prompts = ConfigUtil.LoadConfig<Prompts>(Constant.PromptConf);
        Result = ConfigUtil.LoadConfig<ResultConfig>(Constant.ResultConf);
        Input = ConfigUtil.LoadConfig<InputConfig>(Constant.InputConf);
        Screenshot = ConfigUtil.LoadConfig<ScreenshotConfig>(Constant.ScreenshotConf);
        SpeechRecognition = ConfigUtil.LoadConfig<SpeechRecognitionConfig>(Constant.SpeechRecognitionConf);
        SelectionTranslation = ConfigUtil.LoadConfig<SelectionTranslationConfig>(Constant.SelectionTranslationConf);
        Tts = ConfigUtil.LoadConfig<TtsConfig>(Constant.TtsConf);

        // Set global access for legacy compatibility (if needed)
        Global.Config.GeneralConf = General;
        Global.Config.AiModelConf = AiModel;
        Global.Config.MachineTransConf = MachineTrans;
        Global.Config.ProxyConf = Proxy;
        Global.Config.ShortcutConf = Shortcut;
        Global.Config.SelectionTranslationConf = SelectionTranslation;
        Global.Config.TtsConf = Tts;
        
        // Ensure initial IDs are persisted (especially just after migration)
        ConfigUtil.SaveConfig(Constant.MachineTransConf, MachineTrans);

        // Setup Auto-Save Subscriptions
        SetupAutoSave();
        
        _logger.LogInformation("Configurations loaded successfully");
    }

    public General General { get; }
    public AiModel AiModel { get; }
    public MachineTrans MachineTrans { get; }
    public Proxy Proxy { get; }
    public Shortcut Shortcut { get; }
    public Prompts Prompts { get; }
    public ResultConfig Result { get; }
    public InputConfig Input { get; }
    public ScreenshotConfig Screenshot { get; }
    public SelectionTranslationConfig SelectionTranslation { get; }
    public SpeechRecognitionConfig SpeechRecognition { get; }
    public TtsConfig Tts { get; }

    private void SetupAutoSave()
    {
        // General Config - Simple Property Changes
        General.Changed
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving General configuration");
                ConfigUtil.SaveConfig(Constant.General, General);
            });

        // Proxy Config - Simple Property Changes
        Proxy.Changed
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving Proxy configuration");
                ConfigUtil.SaveConfig(Constant.ProxyConf, Proxy);
            });

        // Result Config - Simple Property Changes
        Result.Changed
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving Result configuration");
                ConfigUtil.SaveConfig(Constant.ResultConf, Result);
            });
        
        // Input Config - Simple Property Changes
        Input.Changed
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving Input configuration");
                ConfigUtil.SaveConfig(Constant.InputConf, Input);
            });

        // Screenshot Config - Simple Property Changes
        Screenshot.Changed
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving Screenshot configuration");
                ConfigUtil.SaveConfig(Constant.ScreenshotConf, Screenshot);
            });

        // SelectionTranslation Config
        SelectionTranslation.Changed
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving SelectionTranslation configuration");
                ConfigUtil.SaveConfig(Constant.SelectionTranslationConf, SelectionTranslation);
            });

        // Screenshot FixedAreas
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => Screenshot.FixedAreas.CollectionChanged += h,
                h => Screenshot.FixedAreas.CollectionChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(e =>
            {
                 _logger.LogDebug("Auto-saving Screenshot configuration (FixedAreas changed)");
                 ConfigUtil.SaveConfig(Constant.ScreenshotConf, Screenshot);
                 
                 // Attach listeners to new items if needed (for property changes inside items)
                 if (e.EventArgs.NewItems != null)
                 {
                     foreach (FixedArea item in e.EventArgs.NewItems)
                     {
                         item.Changed
                             .Throttle(TimeSpan.FromMilliseconds(500))
                             .Subscribe(_ => ConfigUtil.SaveConfig(Constant.ScreenshotConf, Screenshot));
                     }
                 }
            });
            
        // Attach to existing FixedAreas
        foreach (var item in Screenshot.FixedAreas)
        {
            item.Changed
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Subscribe(_ => ConfigUtil.SaveConfig(Constant.ScreenshotConf, Screenshot));
        }

        // SpeechRecognition Config - Simple Property Changes
        SpeechRecognition.Changed
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving SpeechRecognition configuration");
                ConfigUtil.SaveConfig(Constant.SpeechRecognitionConf, SpeechRecognition);
            });

        // AI Model Config - Deep changes (List modifications + Item changes + Custom Key List changes)

        // Helper to attach listeners
        void AttachCustomModelListeners(CustomAiModel model)
        {
            // Property Changes
            model.Changed
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Subscribe(_ =>
                {
                    _logger.LogDebug("Auto-saving AiModel configuration (custom model changed)");
                    ConfigUtil.SaveConfig(Constant.AiModelConf, AiModel);
                });

            // ApiKeys List Changes
            Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                    h => model.ApiKeys.CollectionChanged += h,
                    h => model.ApiKeys.CollectionChanged -= h)
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Subscribe(_ =>
                {
                    _logger.LogDebug("Auto-saving AiModel configuration (custom model API keys changed)");
                    ConfigUtil.SaveConfig(Constant.AiModelConf, AiModel);
                });
        }

        // Attach to existing
        foreach (var item in AiModel.ConfiguredModels) AttachCustomModelListeners(item);

        // Watch for collection changes (Add/Remove)
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => AiModel.ConfiguredModels.CollectionChanged += h,
                h => AiModel.ConfiguredModels.CollectionChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(e =>
            {
                _logger.LogDebug("Auto-saving AiModel configuration (configured models changed)");
                ConfigUtil.SaveConfig(Constant.AiModelConf, AiModel);

                if (e.EventArgs.NewItems != null)
                    foreach (CustomAiModel newItem in e.EventArgs.NewItems)
                        AttachCustomModelListeners(newItem);
            });

        // MachineTrans - Nested objects
        var baiduChanges = MachineTrans.Baidu.Changed;
        var tencentChanges = MachineTrans.Tencent.Changed;
        var googleChanges = MachineTrans.Google.Changed;
        var deepLChanges = MachineTrans.DeepL.Changed;
        var mtChanges = MachineTrans.Changed;

        Observable.Merge(baiduChanges, tencentChanges, googleChanges, deepLChanges, mtChanges)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving MachineTrans configuration");
                ConfigUtil.SaveConfig(Constant.MachineTransConf, MachineTrans);
            });

        // Shortcut - List changes
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => Shortcut.Entries.CollectionChanged += h,
                h => Shortcut.Entries.CollectionChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving Shortcut configuration");
                ConfigUtil.SaveConfig(Constant.ShortcutConf, Shortcut);
            });

        // Watch existing shortcut items
        foreach (var item in Shortcut.Entries)
            item.PropertyChanged += (_, _) => ConfigUtil.SaveConfig(Constant.ShortcutConf, Shortcut);
        Shortcut.Entries.CollectionChanged += (_, e) =>
        {
            if (e.NewItems == null) return;
            foreach (ShortcutEntry item in e.NewItems)
                item.PropertyChanged += (_, _) => ConfigUtil.SaveConfig(Constant.ShortcutConf, Shortcut);
        };

        // --- Prompts Auto-Save ---
        
        // Collection changes
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => Prompts.Entries.CollectionChanged += h,
                h => Prompts.Entries.CollectionChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving Prompts configuration");
                ConfigUtil.SaveConfig(Constant.PromptConf, Prompts);
            });

        // Prompts property changes (e.g. SelectedPromptId)
        Prompts.Changed
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving Prompts configuration (property changed)");
                ConfigUtil.SaveConfig(Constant.PromptConf, Prompts);
            });

        // Watch existing prompt items
        foreach (var item in Prompts.Entries)
            item.PropertyChanged += (_, _) => ConfigUtil.SaveConfig(Constant.PromptConf, Prompts);
        Prompts.Entries.CollectionChanged += (_, e) =>
        {
            if (e.NewItems == null) return;
            foreach (PromptEntry item in e.NewItems)
                item.PropertyChanged += (_, _) => ConfigUtil.SaveConfig(Constant.PromptConf, Prompts);
        };

        // --- API Keys Auto-Save (Fix for list persistence) ---



        // Google Keys
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => MachineTrans.Google.ApiKeys.CollectionChanged += h,
                h => MachineTrans.Google.ApiKeys.CollectionChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving MachineTrans configuration (Google keys changed)");
                ConfigUtil.SaveConfig(Constant.MachineTransConf, MachineTrans);
            });

        // DeepL Keys
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => MachineTrans.DeepL.ApiKeys.CollectionChanged += h,
                h => MachineTrans.DeepL.ApiKeys.CollectionChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving MachineTrans configuration (DeepL keys changed)");
                ConfigUtil.SaveConfig(Constant.MachineTransConf, MachineTrans);
            });

        // Baidu Items
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => MachineTrans.Baidu.Items.CollectionChanged += h,
                h => MachineTrans.Baidu.Items.CollectionChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving MachineTrans configuration (Baidu items changed)");
                ConfigUtil.SaveConfig(Constant.MachineTransConf, MachineTrans);
            });

        // Tencent Items
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => MachineTrans.Tencent.Items.CollectionChanged += h,
                h => MachineTrans.Tencent.Items.CollectionChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving MachineTrans configuration (Tencent items changed)");
                ConfigUtil.SaveConfig(Constant.MachineTransConf, MachineTrans);
            });


        // Tts Config
        Tts.Changed
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
            {
                _logger.LogDebug("Auto-saving Tts configuration");
                ConfigUtil.SaveConfig(Constant.TtsConf, Tts);
            });
    }
}