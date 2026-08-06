using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyChat.Application.Translation;
using EasyChat.Application.Tests.Settings;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Translation;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Tests.Translation;

[TestClass]
public sealed class TranslationUseCasesTests
{
    [TestMethod]
    public async Task TranslateAsync_ResolvesDefaultMachineProviderProxyKeyAndLanguageCodes()
    {
        var bundle = CreateMachineBundle();
        var context = CreateContext(bundle);
        var sourceCodes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MachineTranslationProviderNames.Google] = "en"
        };
        var targetCodes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MachineTranslationProviderNames.Google] = "zh-CN"
        };
        var request = new TranslationRequest(
            "hello",
            new TranslationLanguage("en-US", "English", ProviderCodes: sourceCodes),
            new TranslationLanguage("zh-Hans", "Simplified Chinese", ProviderCodes: targetCodes));

        var result = await context.UseCases.TranslateAsync(request);

        Assert.IsTrue(result.IsSuccess, result.Error.Message);
        Assert.AreEqual("machine translation", result.Value.Text);
        Assert.IsNotNull(context.Factory.MachineOptions);
        Assert.AreEqual("http://127.0.0.1:7890", context.Factory.MachineOptions.ProxyUrl);
        Assert.AreEqual("request failed", context.Factory.MachineOptions.RequestErrorMessage);
        var provider = (GoogleTranslationProviderConfiguration)context.Factory.MachineOptions.Provider;
        CollectionAssert.Contains(new[] { "key-a", "key-b" }, provider.ApiKey);
        Assert.IsNotNull(context.Factory.Machine.LastRequest);
        Assert.AreEqual("en", context.Factory.Machine.LastRequest.SourceLanguageCode);
        Assert.AreEqual("zh-CN", context.Factory.Machine.LastRequest.TargetLanguageCode);
    }

    [TestMethod]
    [DataRow("google-id", null)]
    [DataRow(MachineTranslationProviderNames.Google, null)]
    [DataRow("stale-id", MachineTranslationProviderNames.Google)]
    public async Task Prepare_ResolvesMachineProviderIdNameAndLegacyNameInId(
        string providerId,
        string? providerName)
    {
        var context = CreateContext(CreateMachineBundle());
        var session = context.UseCases.Prepare(new TranslationProviderSelection(
            TranslationEngineNames.MachineTrans,
            MachineProviderId: providerId,
            MachineProviderName: providerName));
        using var disposable = session as IDisposable;

        var response = await session.TranslateAsync(CreateRequest());

        Assert.AreEqual("machine translation", response.Text);
        Assert.IsNotNull(context.Factory.MachineOptions);
        var provider = (GoogleTranslationProviderConfiguration)
            context.Factory.MachineOptions.Provider;
        Assert.AreEqual("google-id", provider.Id);
        Assert.AreEqual(MachineTranslationProviderNames.Google, provider.Name);
    }

    [TestMethod]
    public async Task Prepare_ResolvesSelectedPromptAndExplicitOverrideInApplication()
    {
        var context = CreateContext(CreateAiBundle());
        context.Factory.Chat.CompleteResponse =
            "{\"event\":\"translation_delta\",\"text\":\"translated\"}\n{\"event\":\"done\"}\n";
        var request = CreateRequest();

        using var selectedSession = context.UseCases.Prepare() as IDisposable;
        var selectedResponse = await ((ITranslationSession)selectedSession!).TranslateAsync(request);
        Assert.AreEqual("translated", selectedResponse.Text);
        StringAssert.StartsWith(
            context.Factory.Chat.LastRequest!.SystemPrompt,
            "Selected English to Simplified Chinese");

        var explicitSession = context.UseCases.Prepare(new TranslationProviderSelection(
            TranslationEngineNames.AiModel,
            AiModelId: "ai-1",
            PromptOverride: "Override [SourceLang] => [TargetLang]"));
        using var explicitDisposable = explicitSession as IDisposable;
        await explicitSession.TranslateAsync(request);

        StringAssert.StartsWith(
            context.Factory.Chat.LastRequest!.SystemPrompt,
            "Override English => Simplified Chinese");
        Assert.IsNotNull(context.Factory.AiOptions);
        Assert.AreEqual("ai-key", context.Factory.AiOptions.Provider.ApiKey);
        Assert.AreEqual("http://127.0.0.1:7890", context.Factory.AiOptions.ProxyUrl);
    }

    [TestMethod]
    public async Task Prepare_AppendsFeaturePromptOverrideToPromptSelectedById()
    {
        var bundle = CreateAiBundle() with
        {
            Prompts = new PromptSettings(
                "selected",
                [
                    new PromptEntrySettings(
                        "selected",
                        "Selected",
                        "Selected [SourceLang] to [TargetLang]",
                        false),
                    new PromptEntrySettings(
                        "speech",
                        "Speech",
                        "Use the product term EasyChat when translating to [TargetLang].",
                        false)
                ])
        };
        var context = CreateContext(bundle);
        context.Factory.Chat.CompleteResponse =
            "{\"event\":\"translation_delta\",\"text\":\"translated\"}\n{\"event\":\"done\"}\n";
        var session = context.UseCases.Prepare(new TranslationProviderSelection(
            TranslationEngineNames.AiModel,
            AiModelId: "ai-1",
            PromptOverride: "Translate live subtitles from [SourceLang] to [TargetLang].",
            PromptId: "speech"));
        using var disposable = session as IDisposable;

        await session.TranslateAsync(CreateRequest());

        StringAssert.StartsWith(
            context.Factory.Chat.LastRequest!.SystemPrompt,
            "Use the product term EasyChat when translating to Simplified Chinese.\n\n"
            + "Translate live subtitles from English to Simplified Chinese.");
    }

    [TestMethod]
    public async Task StreamAsync_DecodesChunkedJsonLinesAndEmitsOneCompletion()
    {
        var context = CreateContext(CreateAiBundle());
        context.Factory.Chat.StreamChunks =
        [
            "{\"event\":\"start\",\"mode\":\"translation\",\"source_language\":\"English\",\"target_language\":\"Chinese\"}\n"
            + "{\"event\":\"translation_delta\",\"text\":\"hel",
            "lo\"}\n{\"event\":\"done\"}\n"
        ];

        var events = new List<TranslationEvent>();
        await foreach (var item in context.UseCases.StreamAsync(CreateRequest()))
            events.Add(item);

        Assert.IsInstanceOfType<TranslationStartedEvent>(events[0]);
        Assert.AreEqual(
            "hello",
            string.Concat(events.OfType<TranslationDeltaEvent>().Select(delta => delta.Text)));
        Assert.AreEqual(1, events.Count(item => item is TranslationCompletedEvent));
    }

    [TestMethod]
    public async Task IdentifiedStream_ParsesOnlyValidBlockDeltas()
    {
        var context = CreateContext(CreateAiBundle());
        context.Factory.Chat.StreamChunks =
        [
            "{\"event\":\"start\",\"mode\":\"identified_translation\"}\n"
            + "{\"event\":\"translation_delta\",\"id\":\"block-0\",\"text\":\"first\"}\n",
            "{\"event\":\"translation_delta\",\"id\":\"\",\"text\":\"ignored\"}\n"
            + "{\"event\":\"translation_delta\",\"id\":\"block-1\",\"text\":\"second\"}\n"
            + "{\"event\":\"done\"}\n"
        ];
        var session = context.UseCases.Prepare();
        using var disposable = session as IDisposable;

        var deltas = new List<IdentifiedTranslationDelta>();
        await foreach (var delta in session.StreamIdentifiedAsync(CreateRequest()))
            deltas.Add(delta);

        CollectionAssert.AreEqual(new[] { "block-0", "block-1" }, deltas.Select(x => x.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "first", "second" }, deltas.Select(x => x.Text).ToArray());
        StringAssert.Contains(
            context.Factory.Chat.LastRequest!.SystemPrompt,
            "Identified JSONL translation contract");
    }

    [TestMethod]
    public async Task StructuredJsonLinesStream_UsesSuppliedContractAndBuffersChunkBoundaries()
    {
        var context = CreateContext(CreateAiBundle());
        context.Factory.Chat.StreamChunks =
        [
            "{\"kind\":\"fir",
            "st\",\"text\":\"hello\"}\n{\"kind\":\"second\",\"count\":",
            "2}\n"
        ];
        var session = context.UseCases.Prepare(new TranslationProviderSelection(
            TranslationEngineNames.AiModel,
            AiModelId: "ai-1",
            PromptOverride: "Override [SourceLang] => [TargetLang]"));
        using var disposable = session as IDisposable;
        Assert.IsInstanceOfType<IStructuredJsonLinesTranslationSession>(session);
        var structured = (IStructuredJsonLinesTranslationSession)session;

        var items = new List<System.Text.Json.JsonElement>();
        await foreach (var item in structured.StreamJsonLinesAsync(
                           CreateRequest(),
                           "Emit kind for [TargetLang]."))
        {
            items.Add(item);
        }

        Assert.HasCount(2, items);
        Assert.AreEqual("first", items[0].GetProperty("kind").GetString());
        Assert.AreEqual("hello", items[0].GetProperty("text").GetString());
        Assert.AreEqual("second", items[1].GetProperty("kind").GetString());
        Assert.AreEqual(2, items[1].GetProperty("count").GetInt32());
        var systemPrompt = context.Factory.Chat.LastRequest!.SystemPrompt;
        StringAssert.StartsWith(systemPrompt, "Override English => Simplified Chinese");
        StringAssert.Contains(systemPrompt, "Runtime structured JSONL contract (highest priority)");
        StringAssert.Contains(systemPrompt, "Emit kind for Simplified Chinese.");
    }

    [TestMethod]
    [DataRow("```json\n{\"kind\":\"first\"}\n```\n")]
    [DataRow("{\"kind\":\"first\"}\nnot-json\n")]
    [DataRow("extra text\n{\"kind\":\"first\"}\n")]
    public async Task StructuredJsonLinesStream_RejectsFencesAndNonJsonText(string output)
    {
        var context = CreateContext(CreateAiBundle());
        context.Factory.Chat.StreamChunks = [output];
        var session = context.UseCases.Prepare(new TranslationProviderSelection(
            TranslationEngineNames.AiModel,
            AiModelId: "ai-1"));
        using var disposable = session as IDisposable;
        var structured = (IStructuredJsonLinesTranslationSession)session;

        await Assert.ThrowsExactlyAsync<JsonException>(async () =>
        {
            await foreach (var _ in structured.StreamJsonLinesAsync(
                               CreateRequest(),
                               "Emit one JSON object per line."))
            {
            }
        });
    }

    [TestMethod]
    public void MachineSession_DoesNotExposeStructuredJsonLinesCapability()
    {
        var context = CreateContext(CreateMachineBundle());
        var session = context.UseCases.Prepare();
        using var disposable = session as IDisposable;

        Assert.IsFalse(session is IStructuredJsonLinesTranslationSession);
    }

    [TestMethod]
    public async Task StreamAsync_ReturnsCreationFailureForMissingConfiguredProvider()
    {
        var bundle = CreateMachineBundle() with
        {
            General = CreateMachineBundle().General with
            {
                MachineTranslationId = "missing",
                MachineTranslation = null
            }
        };
        var context = CreateContext(bundle);

        var events = new List<TranslationEvent>();
        await foreach (var item in context.UseCases.StreamAsync(CreateRequest()))
            events.Add(item);

        Assert.HasCount(1, events);
        var failure = (TranslationFailedEvent)events[0];
        Assert.AreEqual("translation.create_failed", failure.Error.Code);
    }

    private static TestContext CreateContext(SettingsBundle settings)
    {
        var factory = new FakeTranslationProviderFactory();
        var failureSink = new FakeFailureSink();
        return new TestContext(
            new TranslationUseCases(
                new FakeSettingsUseCases(settings),
                factory,
                failureSink,
                new TranslationMessages("request failed")),
            factory,
            failureSink);
    }

    private static SettingsBundle CreateMachineBundle()
    {
        var bundle = SettingsTestData.CreateBundle();
        var machine = bundle.MachineTranslation with
        {
            Google = new GoogleTranslationSettings(
                true,
                "google-id",
                "nmt",
                ["key-a", "key-b"])
        };
        return bundle with
        {
            General = bundle.General with
            {
                TranslationEngine = TranslationEngineNames.MachineTrans,
                MachineTranslationId = "google-id",
                MachineTranslation = MachineTranslationProviderNames.Google
            },
            MachineTranslation = machine,
            Proxy = new ProxySettings("http://127.0.0.1:7890")
        };
    }

    private static SettingsBundle CreateAiBundle()
    {
        var bundle = SettingsTestData.CreateBundle();
        return bundle with
        {
            General = bundle.General with
            {
                TranslationEngine = TranslationEngineNames.AiModel,
                AiModelId = "ai-1",
                AiModel = "Configured AI"
            },
            AiModel = new AiModelSettings(
            [
                new CustomAiModelSettings(
                    "ai-1",
                    "Configured AI",
                    AiModelType.OpenAi,
                    ["ai-key"],
                    "https://api.example.com",
                    "model",
                    true,
                    false)
            ]),
            Prompts = new PromptSettings(
                "selected",
                [new PromptEntrySettings(
                    "selected",
                    "Selected",
                    "Selected [SourceLang] to [TargetLang]",
                    false)]),
            Proxy = new ProxySettings("http://127.0.0.1:7890")
        };
    }

    private static TranslationRequest CreateRequest() => new(
        "hello",
        new TranslationLanguage("en", "English"),
        new TranslationLanguage("zh-Hans", "Simplified Chinese"));

    private sealed record TestContext(
        TranslationUseCases UseCases,
        FakeTranslationProviderFactory Factory,
        FakeFailureSink FailureSink);

    private sealed class FakeTranslationProviderFactory : ITranslationProviderFactory
    {
        public FakeMachineProvider Machine { get; } = new();
        public FakeChatProvider Chat { get; } = new();
        public AiTranslationProviderOptions? AiOptions { get; private set; }
        public MachineTranslationProviderOptions? MachineOptions { get; private set; }

        public IChatTranslationProvider Create(AiTranslationProviderOptions options)
        {
            AiOptions = options;
            return Chat;
        }

        public ITranslationProvider Create(MachineTranslationProviderOptions options)
        {
            MachineOptions = options;
            return Machine;
        }
    }

    private sealed class FakeMachineProvider : ITranslationProvider
    {
        public TranslationProviderRequest? LastRequest { get; private set; }

        public Task<string> TranslateAsync(
            TranslationProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult("machine translation");
        }
    }

    private sealed class FakeChatProvider : IChatTranslationProvider
    {
        public string CompleteResponse { get; set; } = string.Empty;
        public IReadOnlyList<string> StreamChunks { get; set; } = [];
        public ChatTranslationProviderRequest? LastRequest { get; private set; }

        public Task<string> CompleteAsync(
            ChatTranslationProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(CompleteResponse);
        }

        public async IAsyncEnumerable<string> StreamAsync(
            ChatTranslationProviderRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            foreach (var chunk in StreamChunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class FakeFailureSink : ITranslationFailureSink
    {
        public Exception? Exception { get; private set; }
        public void Report(Exception exception) => Exception = exception;
    }

    private sealed class FakeSettingsUseCases(SettingsBundle current) : ISettingsUseCases
    {
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<SettingsSaveFailedEventArgs>? SaveFailed
        {
            add { }
            remove { }
        }

        public bool IsInitialized => true;
        public SettingsBundle Current { get; } = current;

        public ValueTask<Result<SettingsBundle>> InitializeAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<SettingsBundle>.Success(Current));

        public Result Update(SettingsSection section, SettingsBundle settings) => Result.Success();

        public ValueTask<Result> FlushAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
