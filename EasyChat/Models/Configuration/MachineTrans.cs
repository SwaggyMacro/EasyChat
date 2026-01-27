using System;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class MachineTrans : ReactiveObject
{
    private CBaidu _baidu = new();

    private CDeepL _deepL = new();

    private CGoogle _google = new();

    private CTencent _tencent = new();

    [JsonProperty]
    public CBaidu Baidu
    {
        get => _baidu;
        set => this.RaiseAndSetIfChanged(ref _baidu, value);
    }

    [JsonProperty]
    public CTencent Tencent
    {
        get => _tencent;
        set => this.RaiseAndSetIfChanged(ref _tencent, value);
    }

    [JsonProperty]
    public CGoogle Google
    {
        get => _google;
        set => this.RaiseAndSetIfChanged(ref _google, value);
    }

    [JsonProperty]
    public CDeepL DeepL
    {
        get => _deepL;
        set => this.RaiseAndSetIfChanged(ref _deepL, value);
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class BaiduItem : ReactiveObject
    {
        private string _appId = string.Empty;

        private string _appKey = string.Empty;

        [JsonProperty]
        public string AppId
        {
            get => _appId;
            set => this.RaiseAndSetIfChanged(ref _appId, value);
        }

        [JsonProperty]
        public string AppKey
        {
            get => _appKey;
            set => this.RaiseAndSetIfChanged(ref _appKey, value);
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class TencentItem : ReactiveObject
    {
        private string _secretId = string.Empty;

        private string _secretKey = string.Empty;

        [JsonProperty]
        public string SecretId
        {
            get => _secretId;
            set => this.RaiseAndSetIfChanged(ref _secretId, value);
        }

        [JsonProperty]
        public string SecretKey
        {
            get => _secretKey;
            set => this.RaiseAndSetIfChanged(ref _secretKey, value);
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class CBaidu : ReactiveObject
    {
        private bool _useProxy;

        [JsonProperty]
        public bool UseProxy
        {
            get => _useProxy;
            set => this.RaiseAndSetIfChanged(ref _useProxy, value);
        }

        [JsonProperty]
        public string Id
        {
            get => _id;
            set => this.RaiseAndSetIfChanged(ref _id, value);
        }

        private string _id = Guid.NewGuid().ToString();

        [JsonProperty] public ObservableCollection<BaiduItem> Items { get; set; } = new();

        public string AppId
        {
            get => Items.FirstOrDefault()?.AppId ?? string.Empty;
            set
            {
                var first = Items.FirstOrDefault();
                if (first != null) first.AppId = value;
                else Items.Add(new BaiduItem { AppId = value, AppKey = string.Empty });
                this.RaisePropertyChanged();
            }
        }


        public string AppKey
        {
            get => Items.FirstOrDefault()?.AppKey ?? string.Empty;
            set
            {
                var first = Items.FirstOrDefault();
                if (first != null) first.AppKey = value;
                else Items.Add(new BaiduItem { AppId = string.Empty, AppKey = value });
                this.RaisePropertyChanged();
            }
        }

        public BaiduItem GetRandomItem()
        {
            if (Items.Count == 0) return new BaiduItem();
            return Items[Random.Shared.Next(Items.Count)];
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class CTencent : ReactiveObject
    {
        private bool _useProxy;

        [JsonProperty]
        public bool UseProxy
        {
            get => _useProxy;
            set => this.RaiseAndSetIfChanged(ref _useProxy, value);
        }

        [JsonProperty]
        public string Id
        {
            get => _id;
            set => this.RaiseAndSetIfChanged(ref _id, value);
        }

        private string _id = Guid.NewGuid().ToString();

        [JsonProperty] public ObservableCollection<TencentItem> Items { get; set; } = new();

        public string SecretId
        {
            get => Items.FirstOrDefault()?.SecretId ?? string.Empty;
            set
            {
                var first = Items.FirstOrDefault();
                if (first != null) first.SecretId = value;
                else Items.Add(new TencentItem { SecretId = value, SecretKey = string.Empty });
                this.RaisePropertyChanged();
            }
        }

        public string SecretKey
        {
            get => Items.FirstOrDefault()?.SecretKey ?? string.Empty;
            set
            {
                var first = Items.FirstOrDefault();
                if (first != null) first.SecretKey = value;
                else Items.Add(new TencentItem { SecretId = string.Empty, SecretKey = value });
                this.RaisePropertyChanged();
            }
        }

        public TencentItem GetRandomItem()
        {
            if (Items.Count == 0) return new TencentItem();
            return Items[Random.Shared.Next(Items.Count)];
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class CGoogle : ReactiveObject
    {
        private string _model = "nmt";
        private bool _useProxy;

        [JsonProperty]
        public bool UseProxy
        {
            get => _useProxy;
            set => this.RaiseAndSetIfChanged(ref _useProxy, value);
        }

        [JsonProperty]
        public string Id
        {
            get => _id;
            set => this.RaiseAndSetIfChanged(ref _id, value);
        }

        private string _id = Guid.NewGuid().ToString();

        [JsonProperty]
        public string Model
        {
            get => _model;
            set => this.RaiseAndSetIfChanged(ref _model, value);
        }

        [JsonProperty] public ObservableCollection<string> ApiKeys { get; set; } = new();

        public string Key
        {
            get
            {
                if (ApiKeys.Count == 0) return string.Empty;
                return ApiKeys[Random.Shared.Next(ApiKeys.Count)];
            }
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (!ApiKeys.Contains(value))
                {
                    ApiKeys.Add(value);
                    this.RaisePropertyChanged();
                }
            }
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class CDeepL : ReactiveObject
    {
        private string _modelType = "latency_optimized";
        private bool _useProxy;

        [JsonProperty]
        public bool UseProxy
        {
            get => _useProxy;
            set => this.RaiseAndSetIfChanged(ref _useProxy, value);
        }

        [JsonProperty]
        public string Id
        {
            get => _id;
            set => this.RaiseAndSetIfChanged(ref _id, value);
        }

        private string _id = Guid.NewGuid().ToString();

        [JsonProperty]
        public string ModelType
        {
            get => _modelType;
            set => this.RaiseAndSetIfChanged(ref _modelType, value);
        }

        [JsonProperty] public ObservableCollection<string> ApiKeys { get; set; } = new();

        public string ApiKey
        {
            get
            {
                if (ApiKeys.Count == 0) return string.Empty;
                return ApiKeys[Random.Shared.Next(ApiKeys.Count)];
            }
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (!ApiKeys.Contains(value))
                {
                    ApiKeys.Add(value);
                    this.RaisePropertyChanged();
                }
            }
        }


    }

    public static readonly string[] SupportedProviders = { "Baidu", "Tencent", "Google", "DeepL" };
}