using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using EasyChat.Application.Speech;
using EasyChat.Application.Tests.Settings;
using EasyChat.Application.Translation;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Speech;
using EasyChat.Contracts.Translation;
using EasyChat.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Application.Tests.Speech;

[TestClass]
public sealed class SubtitleSessionCoordinatorTests
{
    [TestMethod]
    public async Task FinalRevisionRebuildsPublishedRangesWithoutDuplicateOrMissingText()
    {
        var settings = CreateSettings(translationEnabled: false);
        await using var harness = new CoordinatorHarness(settings);
        const string partial =
            "one two three four wrong six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty";
        const string final =
            "one two three four right six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty.";

        await harness.SendAsync(SpeechRecognitionEventKind.Started);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, partial);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, partial);
        await harness.WaitForAsync(events => LatestLines(events).Count >= 2);
        var partialLineCount = LatestLines(harness.Events).Count;
        await harness.SendAsync(SpeechRecognitionEventKind.Final, final);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var latest = LatestLines(harness.Events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .OrderBy(line => line.Id)
            .ToArray();
        Assert.HasCount(partialLineCount, latest);
        Assert.AreEqual(final, string.Join(" ", latest.Select(line => line.OriginalText)));
        Assert.IsTrue(latest.All(line => !line.IsTemporary));
    }

    [TestMethod]
    public async Task DuplicateFinalDoesNotCreateAnotherHistoryLine()
    {
        await using var harness = new CoordinatorHarness(CreateSettings(translationEnabled: false));

        await harness.SendAsync(SpeechRecognitionEventKind.Started);
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Hello world.");
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Hello world.");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var latest = LatestLines(harness.Events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .ToArray();
        Assert.HasCount(1, latest);
        Assert.AreEqual("Hello world.", latest[0].OriginalText);
    }

    [TestMethod]
    public async Task IdenticalFinalAfterTheDuplicateWindowStartsANewUtterance()
    {
        await using var harness = new CoordinatorHarness(CreateSettings(translationEnabled: false));

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Yes.");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Yes.");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var latest = LatestLines(harness.Events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .OrderBy(line => line.Id)
            .ToArray();
        Assert.HasCount(2, latest);
        CollectionAssert.AreEqual(new[] { "Yes.", "Yes." }, latest.Select(line => line.OriginalText).ToArray());
    }

    [TestMethod]
    public async Task RepeatedDuplicateFinalsRefreshTheSuppressionWindow()
    {
        await using var harness = new CoordinatorHarness(CreateSettings(translationEnabled: false));

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Yes.");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(TimeSpan.FromMilliseconds(400));
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Yes.");
        await harness.DrainAsync();
        harness.Time.Advance(TimeSpan.FromMilliseconds(400));
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Yes.");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual("Yes.", AssertExactlyOneLatestLine(harness.Events).OriginalText);
    }

    [TestMethod]
    public async Task PunctuationOnlyFinalCompletesTheCurrentDraft()
    {
        await using var harness = new CoordinatorHarness(CreateSettings(translationEnabled: false));

        await harness.SendAsync(SpeechRecognitionEventKind.Started);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "Hello world");
        await harness.SendAsync(SpeechRecognitionEventKind.Final, ".");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var line = AssertExactlyOneLatestLine(harness.Events);
        Assert.AreEqual("Hello world.", line.OriginalText);
        Assert.IsFalse(line.IsTemporary);
    }

    [TestMethod]
    public async Task PunctuationOnlyFinalKeepsClosingQuotesWithTheDraft()
    {
        await using var harness = new CoordinatorHarness(CreateSettings(translationEnabled: false));

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "他说“你好");
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "。”");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual("他说“你好。”", AssertExactlyOneLatestLine(harness.Events).OriginalText);
    }

    [TestMethod]
    public async Task CumulativeFinalAfterQuietAppendsPunctuationToTheSameLine()
    {
        await using var harness = new CoordinatorHarness(CreateSettings(translationEnabled: false));
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "Hello world");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(IncrementalSubtitleSegmenter.QuietPeriod + TimeSpan.FromMilliseconds(100));
        await harness.WaitForAsync(events => LatestLines(events)
            .Any(line => line.OriginalText == "Hello world" && !line.IsTemporary));

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Hello world.");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var line = AssertExactlyOneLatestLine(harness.Events);
        Assert.AreEqual("Hello world.", line.OriginalText);
    }

    [TestMethod]
    public async Task AiQuietWindowWaitsForDelayedAsrPunctuation()
    {
        var translations = new RecordingTranslationUseCases("preview translation");
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(
            SpeechRecognitionEventKind.Partial,
            "Wait for delayed punctuation");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(
            IncrementalSubtitleSegmenter.QuietPeriod + TimeSpan.FromMilliseconds(100));
        await harness.DrainAsync();

        var beforePunctuation = AssertExactlyOneLatestLine(harness.Events);
        Assert.IsTrue(beforePunctuation.IsTemporary);
        Assert.AreEqual("Wait for delayed punctuation", beforePunctuation.OriginalText);

        harness.Time.Advance(TimeSpan.FromMilliseconds(500));
        await harness.SendAsync(
            SpeechRecognitionEventKind.Final,
            "Wait for delayed punctuation.");
        await harness.WaitForAsync(events =>
        {
            var line = AssertExactlyOneLatestLine(events);
            return !line.IsTemporary
                   && !line.IsTranslating
                   && line.OriginalText == "Wait for delayed punctuation.";
        });
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.HasCount(1, LatestLines(harness.Events));
        Assert.IsGreaterThan(
            IncrementalSubtitleSegmenter.QuietPeriod,
            SubtitleSessionCoordinator.AiQuietPeriod);
    }

    [TestMethod]
    public async Task ResetPartialAfterQuietStartsANewUtterance()
    {
        await using var harness = new CoordinatorHarness(CreateSettings(translationEnabled: false));
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "first thought");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(IncrementalSubtitleSegmenter.QuietPeriod + TimeSpan.FromMilliseconds(100));
        await harness.WaitForAsync(events => LatestLines(events)
            .Any(line => line.OriginalText == "first thought" && !line.IsTemporary));

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "second thought");
        await harness.WaitForAsync(events => LatestLines(events)
            .Any(line => line.OriginalText == "second thought"));
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "second thought.");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var lines = LatestLines(harness.Events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .OrderBy(line => line.Id)
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { "first thought", "second thought." },
            lines.Select(line => line.OriginalText).ToArray());
    }

    [TestMethod]
    public async Task FinalWithOnlyAnAddedTerminalSuffixUpdatesPriorFinal()
    {
        await using var harness = new CoordinatorHarness(CreateSettings(translationEnabled: false));
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Hello world");
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Hello world.");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var line = AssertExactlyOneLatestLine(harness.Events);
        Assert.AreEqual("Hello world.", line.OriginalText);
    }

    [TestMethod]
    public async Task TerminalExtensionAfterTheDuplicateWindowStartsANewUtterance()
    {
        await using var harness = new CoordinatorHarness(CreateSettings(translationEnabled: false));

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Hello world");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Hello world.");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var latest = LatestLines(harness.Events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .OrderBy(line => line.Id)
            .ToArray();
        Assert.HasCount(2, latest);
        CollectionAssert.AreEqual(
            new[] { "Hello world", "Hello world." },
            latest.Select(line => line.OriginalText).ToArray());
    }

    [TestMethod]
    public async Task CumulativeFinalAppendsOnlyTheMissingTerminalCluster()
    {
        await using var harness = new CoordinatorHarness(CreateSettings(translationEnabled: false));

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Really?");
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Really?!");
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "!");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual("Really?!", AssertExactlyOneLatestLine(harness.Events).OriginalText);
    }

    [TestMethod]
    public async Task FinalCaseRevisionUpdatesAPreviouslyCommittedRange()
    {
        var settings = CreateSettings(translationEnabled: false) with { MaxSentencesPerLine = 1 };
        await using var harness = new CoordinatorHarness(settings);
        const string partial = "hello world. next sentence";
        const string final = "Hello world. next sentence.";

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, partial);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, partial);
        await harness.WaitForAsync(events => LatestLines(events).Count >= 2);
        var firstId = LatestLines(harness.Events).OrderBy(line => line.Id).First().Id;
        await harness.SendAsync(SpeechRecognitionEventKind.Final, final);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var latest = LatestLines(harness.Events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .OrderBy(line => line.Id)
            .ToArray();
        Assert.AreEqual(firstId, latest[0].Id);
        Assert.AreEqual(final, string.Join(" ", latest.Select(line => line.OriginalText)));
    }

    [TestMethod]
    public async Task FinalReconcilesARevisionInsideAnAlreadyCommittedRange()
    {
        var settings = CreateSettings(translationEnabled: false) with { MaxSentencesPerLine = 1 };
        await using var harness = new CoordinatorHarness(settings);
        const string original = "I like cats. and dogs";
        const string revised = "I like bats. and dogs.";

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, original);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, original);
        await harness.WaitForAsync(events => LatestLines(events).Count >= 2);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "I like bats. and dogs");
        await harness.SendAsync(SpeechRecognitionEventKind.Final, revised);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var text = string.Join(" ", LatestLines(harness.Events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .OrderBy(line => line.Id)
            .Select(line => line.OriginalText));
        Assert.AreEqual(revised, text);
    }

    [TestMethod]
    public async Task StopReconcilesARevisionInsideAnAlreadyCommittedRange()
    {
        var settings = CreateSettings(translationEnabled: false) with { MaxSentencesPerLine = 1 };
        await using var harness = new CoordinatorHarness(settings);
        const string original = "I like cats. and dogs";
        const string revised = "I like bats. and dogs";

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, original);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, original);
        await harness.WaitForAsync(events => LatestLines(events).Count >= 2);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, revised);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var text = string.Join(" ", LatestLines(harness.Events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .OrderBy(line => line.Id)
            .Select(line => line.OriginalText));
        Assert.AreEqual(revised, text);
    }

    [TestMethod]
    public async Task ShortFinalRetractsObsoleteRangesInsteadOfSplittingIntoSingleCharacters()
    {
        await using var harness = new CoordinatorHarness(CreateSettings(translationEnabled: false));
        const string partial =
            "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty";

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, partial);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, partial);
        await harness.WaitForAsync(events => LatestLines(events).Count >= 2);
        var oldIds = LatestLines(harness.Events).OrderBy(line => line.Id).Select(line => line.Id).ToArray();
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "OK.");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var remaining = LatestLines(harness.Events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .ToArray();
        var retractedIds = harness.Events.OfType<SpeechFloatingSubtitleRemovedEvent>()
            .Select(item => item.SubtitleId)
            .ToHashSet();
        Assert.HasCount(1, remaining);
        Assert.AreEqual(oldIds[0], remaining[0].Id);
        Assert.AreEqual("OK.", remaining[0].OriginalText);
        Assert.IsTrue(oldIds.Skip(1).All(retractedIds.Contains));
    }

    [TestMethod]
    public async Task StopFlushesAnUnfinishedHypothesis()
    {
        await using var harness = new CoordinatorHarness(CreateSettings(translationEnabled: false));

        await harness.SendAsync(SpeechRecognitionEventKind.Started);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "unfinished speech without final");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var line = AssertExactlyOneLatestLine(harness.Events);
        Assert.AreEqual("unfinished speech without final", line.OriginalText);
        Assert.IsFalse(line.IsTemporary);
    }

    [TestMethod]
    public async Task MaxSentencesPerLineRemainsAnAdditionalFinalLimit()
    {
        var settings = CreateSettings(translationEnabled: false) with { MaxSentencesPerLine = 2 };
        await using var harness = new CoordinatorHarness(settings);

        await harness.SendAsync(SpeechRecognitionEventKind.Started);
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "One. Two. Three.");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var lines = LatestLines(harness.Events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .OrderBy(line => line.Id)
            .ToArray();
        Assert.HasCount(2, lines);
        Assert.AreEqual("One. Two.", lines[0].OriginalText);
        Assert.AreEqual("Three.", lines[1].OriginalText);
    }

    [TestMethod]
    public async Task AiTranslationBuffersTwoStableSentencesIntoOneRequest()
    {
        const string source = "First complete sentence. Second complete sentence.";
        var translations = new RecordingTranslationUseCases("combined translation");
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false,
            MaxSentencesPerLine = 1
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, source);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, source);
        await harness.WaitForAsync(events =>
        {
            var lines = LatestLines(events);
            return lines.Count == 1 && lines[0].OriginalText == source;
        });
        await harness.DrainAsync();
        Assert.AreEqual(0, translations.RequestCount);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, source);
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        await harness.WaitForAsync(events => LatestLines(events)
            .Single().DisplayTranslatedText == "combined translation");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, translations.RequestCount);
        using var json = JsonDocument.Parse(translations.Invocations.Single().Request.Text);
        Assert.AreEqual(source, json.RootElement.GetProperty("current").GetString());
        Assert.AreEqual(source, LatestLines(harness.Events).Single().OriginalText);
    }

    [TestMethod]
    public async Task StructuredAiFinalMaterializesSemanticSentencesWithoutChangingSourceCoverage()
    {
        const string source = "Hello. Next.";
        var stream = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(stream);
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false,
            MaxSentencesPerLine = 1
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, source);
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        var aggregate = AssertExactlyOneLatestLine(harness.Events);

        stream.Emit(StructuredSegment(0, "Hello. ", "你好。", isFinal: true));
        stream.Emit(StructuredSegment(1, "Next.", "下一句。", isFinal: true));
        stream.Complete();
        await harness.WaitForAsync(events => LatestLines(events).Count == 2);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var lines = LatestLines(harness.Events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .OrderBy(line => line.Id)
            .ToArray();
        Assert.HasCount(2, lines);
        Assert.AreEqual(source, string.Concat(lines.Select(line => line.OriginalText)));
        CollectionAssert.AreEqual(
            new[] { "你好。", "下一句。" },
            lines.Select(line => line.DisplayTranslatedText).ToArray());
        Assert.AreEqual(aggregate.Timestamp, lines[0].Timestamp);
        Assert.AreEqual(lines[0].Timestamp, lines[1].Timestamp);
        Assert.IsLessThan(lines[1].Id, lines[0].Id);
        Assert.IsTrue(lines.All(line => !line.IsTemporary && !line.IsTranslating));
        Assert.AreEqual(1, translations.RequestCount);
    }

    [TestMethod]
    [DataRow("incomplete-coverage")]
    [DataRow("wrong-sequence")]
    [DataRow("wrong-source")]
    public async Task InvalidStructuredAiPlanRetriesBeforeLeavingSubtitleBlank(string invalidPlan)
    {
        const string source = "Hello. Next.";
        const string acceptedTranslation = "First translated. ";
        var invalid = new ControlledStructuredTranslationStream();
        var retry = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(invalid, retry);
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false,
            MaxSentencesPerLine = 1
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, source);
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        var lineId = AssertExactlyOneLatestLine(harness.Events).Id;
        var updatesBeforeFailure = harness.Events
            .OfType<SpeechSubtitleChangedEvent>()
            .Count(item => item.Subtitle.Id == lineId);
        invalid.Emit(StructuredSegment(0, "Hello. ", acceptedTranslation, isFinal: true));
        if (invalidPlan == "wrong-sequence")
            invalid.Emit(StructuredSegment(2, "Next.", "Second translated.", isFinal: true));
        else if (invalidPlan == "wrong-source")
            invalid.Emit(StructuredSegment(1, "Different.", "Second translated.", isFinal: true));
        invalid.Complete();
        await harness.WaitForAsync(events => events
            .OfType<SpeechSubtitleChangedEvent>()
            .Count(item => item.Subtitle.Id == lineId) > updatesBeforeFailure);
        var waitingForRetry = AssertExactlyOneLatestLine(harness.Events);
        Assert.IsTrue(waitingForRetry.IsTranslating);
        Assert.AreEqual(acceptedTranslation, waitingForRetry.DisplayTranslatedText);

        harness.Time.Advance(SubtitleSessionCoordinator.FinalTranslationRetryDelay);
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        retry.Emit(StructuredSegment(0, "Hello. ", "First translated. ", isFinal: true));
        retry.Emit(StructuredSegment(1, "Next.", "Second translated.", isFinal: true));
        retry.Complete();
        await harness.WaitForAsync(events => LatestLines(events).Count == 2
                                             && LatestLines(events)
                                                 .All(line => !line.IsTranslating));
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var lines = LatestLines(harness.Events).OrderBy(line => line.Id).ToArray();
        CollectionAssert.AreEqual(
            new[] { "Hello. ", "Next." },
            lines.Select(line => line.OriginalText).ToArray());
        CollectionAssert.AreEqual(
            new[] { "First translated. ", "Second translated." },
            lines.Select(line => line.DisplayTranslatedText).ToArray());
        Assert.AreEqual(2, translations.RequestCount);
        Assert.AreEqual(0, translations.UnstructuredRequestCount);
    }

    [TestMethod]
    public async Task CombinedStructuredAiRecordRemainsReadableInsteadOfBeingDiscarded()
    {
        const string source = "Hello. Next.";
        var stream = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(stream);
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, source);
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        stream.Emit(StructuredSegment(
            0,
            source,
            "Combined readable translation.",
            isFinal: true));
        stream.Complete();
        await harness.WaitForAsync(events =>
        {
            var line = AssertExactlyOneLatestLine(events);
            return !line.IsTranslating
                   && line.DisplayTranslatedText == "Combined readable translation.";
        });
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, translations.RequestCount);
    }

    [TestMethod]
    public async Task StructuredTimeoutKeepsReadablePartialUntilRetryCompletes()
    {
        const string source = "Hello. Next.";
        var timedOut = new ControlledStructuredTranslationStream();
        var retry = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(timedOut, retry);
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false,
            AutoClearInterval = 0
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, source);
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        timedOut.Emit(StructuredSegment(
            0,
            "Hello. ",
            "Readable partial translation.",
            isFinal: true));
        await harness.WaitForAsync(events => LatestLines(events)
            .Single().DisplayTranslatedText == "Readable partial translation.");

        harness.Time.Advance(TimeSpan.FromSeconds(30.1));
        await harness.DrainAsync();
        var waitingForRetry = AssertExactlyOneLatestLine(harness.Events);
        Assert.IsTrue(waitingForRetry.IsTranslating);
        Assert.AreEqual(
            "Readable partial translation.",
            waitingForRetry.DisplayTranslatedText);
        harness.Time.Advance(
            SubtitleSessionCoordinator.FinalTranslationRetryDelay
            + TimeSpan.FromMilliseconds(100));
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        retry.Emit(StructuredSegment(0, "Hello. ", "Complete first. ", isFinal: true));
        retry.Emit(StructuredSegment(1, "Next.", "Complete second.", isFinal: true));
        retry.Complete();
        await harness.WaitForAsync(events => LatestLines(events).Count == 2
                                             && LatestLines(events).All(line =>
                                                 !line.IsTranslating));

        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        CollectionAssert.AreEqual(
            new[] { "Complete first. ", "Complete second." },
            LatestLines(harness.Events)
                .OrderBy(line => line.Id)
                .Select(line => line.DisplayTranslatedText)
                .ToArray());
    }

    [TestMethod]
    public async Task CancelingStructuredTranslationRetractsTheJobPartialTranslation()
    {
        const string source = "Hello. Next.";
        var stream = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(stream);
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, source);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, source);
        await harness.WaitForAsync(events => LatestLines(events).Count == 1);
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        stream.Emit(StructuredSegment(0, "Hello. ", "Readable partial translation.", isFinal: true));
        await harness.WaitForAsync(events => LatestLines(events)
            .Single().DisplayTranslatedText == "Readable partial translation.");

        harness.Settings = harness.Settings with { IsTranslationEnabled = false };
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, source);
        await harness.WaitForAsync(events =>
        {
            var line = LatestLines(events).Single();
            return !line.IsTranslating && line.DisplayTranslatedText.Length == 0;
        });

        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task StructuredProviderFailureRetractsTheJobPartialTranslation()
    {
        const string source = "Hello. Next.";
        var stream = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(stream);
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, source);
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        stream.Emit(StructuredSegment(0, "Hello. ", "Readable partial translation.", isFinal: true));
        await harness.WaitForAsync(events => LatestLines(events)
            .Single().DisplayTranslatedText == "Readable partial translation.");
        stream.Fail(new InvalidOperationException("provider stream failed"));

        await harness.WaitForAsync(events =>
        {
            var line = LatestLines(events).Single();
            return !line.IsTranslating && line.DisplayTranslatedText.Length == 0;
        });
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task FailedStructuredRetryRestoresAnExactSourceTranslationSnapshot()
    {
        const string source = "Hello. Next.";
        const string stableTranslation = "Stable prior translation.";
        var first = new ControlledStructuredTranslationStream();
        var retry = new ControlledStructuredTranslationStream();
        var exhaustedRetry = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(
            first,
            retry,
            exhaustedRetry);
        var settings = CreateSettings(translationEnabled: true);
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, source);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, source);
        await harness.WaitForAsync(events => LatestLines(events).Count == 1);
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        first.Emit(StructuredSegment(0, "Hello. ", "Stable prior", isFinal: true));
        first.Emit(StructuredSegment(1, "Next.", "translation.", isFinal: true));
        first.Complete();
        await harness.WaitForAsync(events =>
        {
            var line = LatestLines(events).Single();
            return !line.IsTranslating
                   && line.DisplayTranslatedText == stableTranslation;
        });

        harness.Settings = settings with { EngineId = "replacement" };
        await harness.SendAsync(SpeechRecognitionEventKind.Final, source);
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        retry.Emit(StructuredSegment(
            0,
            "Hello. ",
            "Replacement partial translation.",
            isFinal: true));
        await harness.DrainAsync();
        var translating = LatestLines(harness.Events).Single();
        Assert.IsTrue(translating.IsTranslating);
        Assert.AreEqual(stableTranslation, translating.DisplayTranslatedText);
        retry.Emit(StructuredSegment(2, "Next.", "Invalid tail.", isFinal: true));
        retry.Complete();

        await harness.WaitForAsync(events =>
        {
            var line = LatestLines(events).Single();
            return line.IsTranslating
                   && line.DisplayTranslatedText == stableTranslation;
        });
        harness.Time.Advance(SubtitleSessionCoordinator.FinalTranslationRetryDelay);
        await harness.WaitForAsync(_ => translations.RequestCount == 3);
        exhaustedRetry.Emit(StructuredSegment(
            0,
            "Hello. ",
            "Another replacement partial.",
            isFinal: true));
        exhaustedRetry.Emit(StructuredSegment(2, "Next.", "Invalid tail.", isFinal: true));
        exhaustedRetry.Complete();

        await harness.WaitForAsync(events =>
        {
            var line = LatestLines(events).Single();
            return !line.IsTranslating
                   && line.DisplayTranslatedText == stableTranslation;
        });
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task StructuredAiPartialOnlyUpdatesAggregatePreviewUntilAsrFinal()
    {
        const string source = "Hello. Next.";
        const string aggregateTranslation = "第一句。下一句。";
        var stream = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(stream);
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, source);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, source);
        await harness.WaitForAsync(events =>
        {
            var lines = LatestLines(events);
            return lines.Count == 1 && lines[0].OriginalText == source;
        });
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(_ => translations.RequestCount == 1);

        stream.Emit(StructuredSegment(0, "Hello. ", "第一句。", isFinal: true));
        stream.Emit(StructuredSegment(1, "Next.", "下一句。", isFinal: true));
        await harness.WaitForAsync(events =>
        {
            var lines = LatestLines(events);
            return lines.Count == 1
                   && lines[0].DisplayTranslatedText == aggregateTranslation;
        });

        var preview = AssertExactlyOneLatestLine(harness.Events);
        Assert.AreEqual(source, preview.OriginalText);
        Assert.AreEqual(aggregateTranslation, preview.DisplayTranslatedText);
        Assert.IsTrue(preview.IsTemporary);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, source);
        await harness.WaitForAsync(events =>
        {
            var lines = LatestLines(events);
            return lines.Count == 1 && !lines.Single().IsTemporary;
        });
        Assert.AreEqual(1, translations.RequestCount);
        stream.Complete();
        await harness.WaitForAsync(events => LatestLines(events).Count == 2);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task StructuredAiSegmentsStartTheirSharedTtlOnlyAfterTheStreamCompletes()
    {
        const string source = "Hello. Next.";
        var stream = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(stream);
        var settings = CreateSettings(translationEnabled: true) with
        {
            AutoClearInterval = 1,
            MaxFloatingHistory = 10
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, source);
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        stream.Emit(StructuredSegment(0, "Hello. ", "First translated.", isFinal: true));
        harness.Time.Advance(TimeSpan.FromSeconds(2));
        await harness.DrainAsync();

        Assert.IsFalse(harness.Events.OfType<SpeechFloatingSubtitleRemovedEvent>().Any());
        Assert.IsTrue(AssertExactlyOneLatestLine(harness.Events).IsTranslating);

        stream.Emit(StructuredSegment(1, "Next.", "Second translated.", isFinal: true));
        stream.Complete();
        await harness.WaitForAsync(events => LatestLines(events).Count == 2
                                             && LatestLines(events).All(line => !line.IsTranslating));
        harness.Time.Advance(TimeSpan.FromMilliseconds(900));
        await harness.DrainAsync();
        Assert.IsFalse(harness.Events.OfType<SpeechFloatingSubtitleRemovedEvent>().Any());

        harness.Time.Advance(TimeSpan.FromMilliseconds(200));
        await harness.WaitForAsync(events => events
            .OfType<SpeechFloatingSubtitleRemovedEvent>()
            .Select(item => item.SubtitleId)
            .Distinct()
            .Count() == 2);
        Assert.HasCount(2, LatestLines(harness.Events));

        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task AiForcedDisplayCutWaitsForFinalBeforeCreatingOneRequest()
    {
        const string source =
            "one two six ten red blue sun moon day night east west calm warm soft clear";
        var translations = new RecordingTranslationUseCases("single buffered result");
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false,
            MaxSentencesPerLine = 1
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, source);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, source);
        await harness.DrainAsync();
        Assert.AreEqual(0, translations.RequestCount);
        Assert.IsTrue(LatestLines(harness.Events).Single().IsTemporary);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, source);
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, translations.RequestCount);
        Assert.AreEqual(source, LatestLines(harness.Events).Single().OriginalText);
        using var json = JsonDocument.Parse(translations.Invocations.Single().Request.Text);
        Assert.AreEqual(source, json.RootElement.GetProperty("current").GetString());
    }

    [TestMethod]
    public async Task ActiveDraftDoesNotExpireButSealedLineExpiresAfterTerminalState()
    {
        var settings = CreateSettings(translationEnabled: false) with { AutoClearInterval = 1 };
        await using var harness = new CoordinatorHarness(settings);

        await harness.SendAsync(SpeechRecognitionEventKind.Started);
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "active draft");
        await harness.WaitForAsync(events => events.OfType<SpeechSubtitleChangedEvent>().Any());
        for (var index = 1; index <= 3; index++)
        {
            harness.Time.Advance(TimeSpan.FromMilliseconds(600));
            var expected = $"active draft {index}";
            await harness.SendAsync(SpeechRecognitionEventKind.Partial, expected);
            await harness.WaitForAsync(events => LatestLines(events)
                .Any(line => line.OriginalText == expected));
        }
        Assert.IsFalse(harness.Events.OfType<SpeechFloatingSubtitleRemovedEvent>().Any());

        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.WaitForAsync(events => events.OfType<SpeechSessionStoppedEvent>().Any());
        Assert.IsFalse(harness.Completion.IsCompleted);
        harness.Time.Advance(TimeSpan.FromSeconds(1.1));
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.HasCount(1, harness.Events.OfType<SpeechFloatingSubtitleRemovedEvent>());
        Assert.IsTrue(LatestLines(harness.Events).Any(line => line.OriginalText == "active draft 3"));
    }

    [TestMethod]
    public async Task AutoClearZeroCompletesWithoutTimeBasedRemoval()
    {
        var settings = CreateSettings(translationEnabled: false) with { AutoClearInterval = 0 };
        await using var harness = new CoordinatorHarness(settings);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "persistent history.");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await harness.DrainAsync();

        Assert.IsFalse(harness.Events.OfType<SpeechFloatingSubtitleRemovedEvent>().Any());
    }

    [TestMethod]
    public async Task FloatingHistoryLimitIsSharedAcrossRecognitionSessions()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var lifecycle = new SubtitleFloatingLifecycleRegistry(time);
        var settings = CreateSettings(translationEnabled: false) with
        {
            AutoClearInterval = 0,
            FloatingDisplayMode = FloatingDisplayMode.Segmented,
            MaxFloatingHistory = 1
        };
        long nextId = 0;
        long NextId() => Interlocked.Increment(ref nextId);

        await using var first = new CoordinatorHarness(
            settings,
            timeProvider: time,
            floatingLifecycle: lifecycle,
            nextSubtitleId: NextId);
        await first.SendAsync(SpeechRecognitionEventKind.Final, "First session.");
        await first.SendAsync(SpeechRecognitionEventKind.Stopped);
        await first.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        var firstId = AssertExactlyOneLatestLine(first.Events).Id;

        await using var second = new CoordinatorHarness(
            settings,
            timeProvider: time,
            floatingLifecycle: lifecycle,
            nextSubtitleId: NextId);
        await second.SendAsync(SpeechRecognitionEventKind.Final, "Second session.");
        await second.WaitForAsync(events => events
            .OfType<SpeechFloatingSubtitleRemovedEvent>()
            .Any(item => item.SubtitleId == firstId));
        await second.SendAsync(SpeechRecognitionEventKind.Stopped);
        await second.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var secondId = AssertExactlyOneLatestLine(second.Events).Id;
        Assert.IsFalse(lifecycle.IsVisible(firstId));
        Assert.IsTrue(lifecycle.IsVisible(secondId));
        Assert.HasCount(1, second.Events
            .OfType<SpeechFloatingSubtitleRemovedEvent>()
            .Where(item => item.SubtitleId == firstId));

        await using var takeover = new CoordinatorHarness(
            settings,
            timeProvider: time,
            floatingLifecycle: lifecycle,
            nextSubtitleId: NextId);
        await takeover.WaitForAsync(events => events
            .OfType<SpeechFloatingSubtitleRemovedEvent>()
            .Any(item => item.SubtitleId == firstId));
        await takeover.SendAsync(SpeechRecognitionEventKind.Stopped);
        await takeover.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.HasCount(1, takeover.Events
            .OfType<SpeechFloatingSubtitleRemovedEvent>()
            .Where(item => item.SubtitleId == firstId));
    }

    [TestMethod]
    public async Task NewSessionExpiresTtlOwnedByCanceledPreviousSession()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var lifecycle = new SubtitleFloatingLifecycleRegistry(time);
        var expiringSettings = CreateSettings(translationEnabled: false) with
        {
            AutoClearInterval = 1,
            FloatingDisplayMode = FloatingDisplayMode.Segmented,
            MaxFloatingHistory = 10
        };
        long nextId = 0;
        long NextId() => Interlocked.Increment(ref nextId);

        var first = new CoordinatorHarness(
            expiringSettings,
            timeProvider: time,
            floatingLifecycle: lifecycle,
            nextSubtitleId: NextId);
        await first.SendAsync(SpeechRecognitionEventKind.Final, "Expires after cancellation.");
        await first.WaitForAsync(events => LatestLines(events)
            .Any(line => line.OriginalText == "Expires after cancellation."));
        var firstId = AssertExactlyOneLatestLine(first.Events).Id;
        await first.DisposeAsync();

        var persistentSettings = expiringSettings with { AutoClearInterval = 0 };
        await using var second = new CoordinatorHarness(
            persistentSettings,
            timeProvider: time,
            floatingLifecycle: lifecycle,
            nextSubtitleId: NextId);
        await second.SendAsync(SpeechRecognitionEventKind.Partial, "new active draft");
        await second.SendAsync(SpeechRecognitionEventKind.Stopped);
        await second.WaitForAsync(events => events.OfType<SpeechSessionStoppedEvent>().Any());
        Assert.IsFalse(second.Completion.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(1.1));
        await second.WaitForAsync(events => events
            .OfType<SpeechFloatingSubtitleRemovedEvent>()
            .Any(item => item.SubtitleId == firstId));
        await second.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsFalse(lifecycle.IsVisible(firstId));
        Assert.HasCount(1, second.Events
            .OfType<SpeechFloatingSubtitleRemovedEvent>()
            .Where(item => item.SubtitleId == firstId));
    }

    [TestMethod]
    public async Task LateStructuredMaterializationCannotReviveGloballyRemovedAnchorOrChildren()
    {
        const string source = "Hello. Next.";
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var lifecycle = new SubtitleFloatingLifecycleRegistry(time);
        var stream = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(stream);
        var aiSettings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false,
            AutoClearInterval = 0,
            FloatingDisplayMode = FloatingDisplayMode.Segmented,
            MaxFloatingHistory = 1
        };
        long nextId = 0;
        long NextId() => Interlocked.Increment(ref nextId);
        await using var first = new CoordinatorHarness(
            aiSettings,
            translations,
            timeProvider: time,
            floatingLifecycle: lifecycle,
            nextSubtitleId: NextId);

        await first.SendAsync(SpeechRecognitionEventKind.Partial, source);
        await first.WaitForAsync(events => LatestLines(events)
            .Any(line => line.OriginalText == source));
        time.Advance(SubtitleSessionCoordinator.AiQuietPeriod + TimeSpan.FromMilliseconds(100));
        await first.WaitForAsync(_ => translations.RequestCount == 1);
        stream.Emit(StructuredSegment(0, "Hello. ", "First translated.", isFinal: true));
        stream.Emit(StructuredSegment(1, "Next.", "Second translated.", isFinal: true));
        stream.Complete();
        await first.WaitForAsync(events =>
        {
            var line = LatestLines(events).Single();
            return !line.IsTranslating && line.DisplayTranslatedText.Length > 0;
        });
        var anchorId = AssertExactlyOneLatestLine(first.Events).Id;

        var secondSettings = CreateSettings(translationEnabled: false) with
        {
            AutoClearInterval = 0,
            FloatingDisplayMode = FloatingDisplayMode.Segmented,
            MaxFloatingHistory = 1
        };
        await using var second = new CoordinatorHarness(
            secondSettings,
            timeProvider: time,
            floatingLifecycle: lifecycle,
            nextSubtitleId: NextId);
        await second.SendAsync(SpeechRecognitionEventKind.Final, "Newer subtitle.");
        await second.WaitForAsync(events => events
            .OfType<SpeechFloatingSubtitleRemovedEvent>()
            .Any(item => item.SubtitleId == anchorId));

        await first.SendAsync(SpeechRecognitionEventKind.Final, source);
        await first.WaitForAsync(events => LatestLines(events).Count == 2);
        var childId = LatestLines(first.Events).Single(line => line.Id != anchorId).Id;
        var firstEvents = first.Events.ToArray();
        var childRemovalIndex = Array.FindIndex(firstEvents, item =>
            item is SpeechFloatingSubtitleRemovedEvent removed && removed.SubtitleId == childId);
        var childChangedIndex = Array.FindIndex(firstEvents, item =>
            item is SpeechSubtitleChangedEvent changed && changed.Subtitle.Id == childId);

        Assert.IsFalse(lifecycle.IsVisible(anchorId));
        Assert.IsFalse(lifecycle.IsVisible(childId));
        Assert.IsGreaterThanOrEqualTo(0, childRemovalIndex);
        Assert.IsGreaterThan(childRemovalIndex, childChangedIndex);
        await first.SendAsync(SpeechRecognitionEventKind.Stopped);
        await first.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        await second.SendAsync(SpeechRecognitionEventKind.Stopped);
        await second.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task SlowLlmLinesStayFloatingUntilCompletedHistoryExceedsTheLimit()
    {
        var first = new ControlledTranslationStream();
        var second = new ControlledTranslationStream();
        var third = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((index, _, token) => index switch
        {
            1 => first.ReadAsync(token),
            2 => second.ReadAsync(token),
            _ => third.ReadAsync(token)
        });
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false,
            FloatingDisplayMode = FloatingDisplayMode.Segmented,
            MaxFloatingHistory = 2,
            AutoClearInterval = 0
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "First source line.");
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Second source line.");
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Third source line.");
        await harness.WaitForAsync(events => LatestLines(events).Count == 3);
        Assert.IsFalse(harness.Events.OfType<SpeechFloatingSubtitleRemovedEvent>().Any());

        first.Emit(new TranslationDeltaEvent("first complete translation"));
        first.Complete();
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        Assert.IsFalse(harness.Events.OfType<SpeechFloatingSubtitleRemovedEvent>().Any());

        second.Emit(new TranslationDeltaEvent("second complete translation"));
        second.Complete();
        await harness.WaitForAsync(_ => translations.RequestCount == 3);
        Assert.IsFalse(harness.Events.OfType<SpeechFloatingSubtitleRemovedEvent>().Any());

        third.Emit(new TranslationDeltaEvent("third complete translation"));
        third.Complete();
        await harness.WaitForAsync(events =>
            events.OfType<SpeechFloatingSubtitleRemovedEvent>().Count() == 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var events = harness.Events.ToArray();
        var firstId = LatestLines(events)
            .Single(line => line.OriginalText == "First source line.").Id;
        var completedIndex = Array.FindLastIndex(events, item =>
            item is SpeechSubtitleChangedEvent changed
            && changed.Subtitle.Id == firstId
            && !changed.Subtitle.IsTranslating
            && changed.Subtitle.DisplayTranslatedText == "first complete translation");
        var removedIndex = Array.FindIndex(events, item =>
            item is SpeechFloatingSubtitleRemovedEvent removed
            && removed.SubtitleId == firstId);
        Assert.IsGreaterThanOrEqualTo(0, completedIndex);
        Assert.IsGreaterThan(completedIndex, removedIndex);
    }

    [TestMethod]
    public async Task DisabledRealtimePreviewWaitsForFinalBeforeCallingLlm()
    {
        var translations = new RecordingTranslationUseCases("最终翻译");
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "one two three four");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(TimeSpan.FromMilliseconds(400));
        await harness.DrainAsync();
        Assert.AreEqual(0, translations.RequestCount);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "one two three four.");
        await harness.WaitForAsync(_ => translations.RequestCount >= 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual("最终翻译", LatestLines(harness.Events).Single().DisplayTranslatedText);
    }

    [TestMethod]
    public async Task SlowLlmPreviewUsesDebounceShadowThresholdAndCoalescedRefresh()
    {
        var stream = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases(
            (_, _, token) => stream.ReadAsync(token));
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "one two three four");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce - TimeSpan.FromMilliseconds(50));
        await harness.DrainAsync();
        Assert.AreEqual(0, translations.RequestCount);

        harness.Time.Advance(TimeSpan.FromMilliseconds(100));
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        stream.Emit(new TranslationDeltaEvent("你"));
        await harness.DrainAsync();
        Assert.AreEqual(string.Empty, LatestLines(harness.Events).Single().DisplayTranslatedText);

        stream.Emit(new TranslationDeltaEvent("好世界翻译"));
        await harness.WaitForAsync(events => LatestLines(events).Single().DisplayTranslatedText == "你好世界翻译");
        stream.Emit(new TranslationDeltaEvent("继续"));
        await harness.DrainAsync();
        Assert.AreEqual("你好世界翻译", LatestLines(harness.Events).Single().DisplayTranslatedText);

        harness.Time.Advance(TimeSpan.FromMilliseconds(100));
        await harness.WaitForAsync(events =>
            LatestLines(events).Single().DisplayTranslatedText == "你好世界翻译继续");
        stream.Complete();
        await harness.WaitForAsync(events => !LatestLines(events).Single().IsTranslating);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "one two three four");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, translations.RequestCount, "Exact final input should promote the preview result.");
    }

    [TestMethod]
    public async Task SlowStructuredLlmPreviewStaysVisibleUntilExtendedFinalReplacement()
    {
        const string previewSource = "one two three four";
        const string finalSource = "one two three four five. Next sentence.";
        var preview = new ControlledStructuredTranslationStream();
        var final = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(preview, final);
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, previewSource);
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        preview.Emit(StructuredSegment(
            0,
            previewSource,
            "Readable prefix translation.",
            isFinal: false));
        await harness.WaitForAsync(events => AssertExactlyOneLatestLine(events)
            .DisplayTranslatedText == "Readable prefix translation.");

        await harness.SendAsync(SpeechRecognitionEventKind.Final, finalSource);
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        var replacing = AssertExactlyOneLatestLine(harness.Events);
        Assert.IsTrue(replacing.IsTranslating);
        Assert.AreEqual("Readable prefix translation.", replacing.DisplayTranslatedText);
        using (var request = JsonDocument.Parse(translations.Invocations[1].Request.Text))
        {
            Assert.AreEqual(
                finalSource,
                request.RootElement.GetProperty("current").GetString());
        }

        final.Emit(StructuredSegment(
            0,
            "one two three four five. ",
            "Complete first translation. ",
            isFinal: true));
        await harness.DrainAsync();
        var firstFinalSegment = AssertExactlyOneLatestLine(harness.Events);
        Assert.IsTrue(firstFinalSegment.IsTranslating);
        Assert.AreEqual(
            "Readable prefix translation.",
            firstFinalSegment.DisplayTranslatedText);
        final.Emit(StructuredSegment(
            1,
            "Next sentence.",
            "Complete second translation.",
            isFinal: true));
        final.Complete();
        await harness.WaitForAsync(events => LatestLines(events).Count == 2
                                             && LatestLines(events).All(line =>
                                                 !line.IsTranslating));
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        CollectionAssert.AreEqual(
            new[] { "Complete first translation. ", "Complete second translation." },
            LatestLines(harness.Events)
                .OrderBy(line => line.Id)
                .Select(line => line.DisplayTranslatedText)
                .ToArray());
    }

    [TestMethod]
    public async Task ContinuousLlmPartialStartsAtMaximumPreviewWait()
    {
        var translations = new RecordingTranslationUseCases("预览翻译");
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "one two three four");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        for (var index = 1; index <= 2; index++)
        {
            harness.Time.Advance(TimeSpan.FromMilliseconds(400));
            var text = $"one two three four extension{index}";
            await harness.SendAsync(SpeechRecognitionEventKind.Partial, text);
            await harness.WaitForAsync(events => LatestLines(events)
                .Any(line => line.OriginalText == text));
            Assert.AreEqual(0, translations.RequestCount);
        }

        harness.Time.Advance(TimeSpan.FromMilliseconds(400));
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task WallClockRollbackDoesNotDelayMonotonicPreviewScheduling()
    {
        var translations = new RecordingTranslationUseCases("monotonic result");
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "one two three four");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce - TimeSpan.FromMilliseconds(50));
        harness.Time.JumpWallClock(TimeSpan.FromHours(-1));
        harness.Time.Advance(TimeSpan.FromMilliseconds(100));

        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task ReadableActivePreviewIsPromotedAndRemainsLoadingUntilStreamCompletes()
    {
        var stream = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases(
            (_, _, token) => stream.ReadAsync(token));
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "one two three four");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        stream.Emit(new TranslationDeltaEvent("可读取的预览"));
        await harness.WaitForAsync(events => LatestLines(events)
            .Any(line => line.DisplayTranslatedText == "可读取的预览"));

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "one two three four");
        await harness.WaitForAsync(events => LatestLines(events).Single().IsTranslating);
        Assert.AreEqual(1, translations.RequestCount);

        stream.Emit(new TranslationDeltaEvent("完成"));
        stream.Complete();
        await harness.WaitForAsync(events => !LatestLines(events).Single().IsTranslating);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual("可读取的预览完成", LatestLines(harness.Events).Single().DisplayTranslatedText);
    }

    [TestMethod]
    public async Task FastMachineTranslationSendsOnlyCurrentTextAndCompletesImmediately()
    {
        var translations = new RecordingTranslationUseCases("机器译文");
        var settings = CreateSettings(translationEnabled: true) with
        {
            EngineType = 0,
            IsRealTimePreviewEnabled = false
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Fast machine source.");
        await harness.WaitForAsync(events => LatestLines(events)
            .Any(line => line.DisplayTranslatedText == "机器译文"));
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var invocation = translations.Invocations.Single();
        Assert.AreEqual(TranslationEngineNames.MachineTrans, invocation.Selection!.Engine);
        Assert.AreEqual(settings.EngineId, invocation.Selection.MachineProviderId);
        Assert.IsNull(invocation.Selection.MachineProviderName);
        Assert.AreEqual("Fast machine source.", invocation.Request.Text);
        Assert.AreEqual(1, translations.MaximumActiveStreams);
    }

    [TestMethod]
    public async Task AiTranslationCarriesOnlyTwoPreviousLinesAsReadonlyContext()
    {
        var translations = new RecordingTranslationUseCases("上下文译文");
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        var expected = 0;
        foreach (var text in new[] { "First line.", "Second line.", "Current line." })
        {
            expected++;
            await harness.SendAsync(SpeechRecognitionEventKind.Final, text);
            await harness.WaitForAsync(_ => translations.RequestCount >= expected);
            await harness.WaitForAsync(events => LatestLines(events).Count >= expected
                && LatestLines(events).All(line => !line.IsTranslating));
        }
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var currentRequest = translations.Invocations.Last().Request;
        using var json = JsonDocument.Parse(currentRequest.Text);
        Assert.AreEqual("Current line.", json.RootElement.GetProperty("current").GetString());
        var context = json.RootElement.GetProperty("context");
        Assert.AreEqual(2, context.GetArrayLength());
        Assert.AreEqual("First line.", context[0].GetProperty("Original").GetString());
        Assert.AreEqual("Second line.", context[1].GetProperty("Original").GetString());
    }

    [TestMethod]
    public async Task AiTranslationCarriesConfiguredPromptIdAndSubtitleOverride()
    {
        var translations = new RecordingTranslationUseCases("translated");
        var settings = CreateSettings(translationEnabled: true) with { PromptId = "speech-prompt" };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Prompted line.");
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var selection = translations.Invocations.Single().Selection!;
        Assert.AreEqual("speech-prompt", selection.PromptId);
        StringAssert.Contains(selection.PromptOverride!, "Translate live subtitles");
    }

    [TestMethod]
    public async Task PreservedStaleTranslationIsNotUsedAsAiContext()
    {
        var replacement = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((index, _, token) => index switch
        {
            1 => YieldTranslationAsync("old left translation", token),
            2 => replacement.ReadAsync(token),
            _ => YieldTranslationAsync("next translation", token)
        });
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "turn left now please");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(events => LatestLines(events)
            .Single().DisplayTranslatedText == "old left translation");

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "turn right now please.");
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "A new sentence.");
        await harness.WaitForAsync(events => LatestLines(events).Count == 2);

        replacement.Emit(new TranslationFailedEvent(new Error("test.failure", "replacement failed")));
        replacement.Complete();
        await harness.WaitForAsync(_ => translations.RequestCount == 3);

        using var json = JsonDocument.Parse(translations.Invocations[2].Request.Text);
        var context = json.RootElement.GetProperty("context");
        Assert.AreEqual(1, context.GetArrayLength());
        Assert.AreEqual("turn right now please.", context[0].GetProperty("Original").GetString());
        Assert.AreEqual(string.Empty, context[0].GetProperty("Translation").GetString());

        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task SwitchingFromCanceledSlowLlmStartsMachineTranslationWithoutWaitingOrAcceptingLateOutput()
    {
        var oldLlmStream = new ControlledTranslationStream(ignoreCancellation: true);
        var machineStream = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((index, _, token) =>
            index == 1 ? oldLlmStream.ReadAsync(token) : machineStream.ReadAsync(token));
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "one two three four");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        oldLlmStream.Emit(new TranslationDeltaEvent("old llm preview"));
        await harness.WaitForAsync(events => LatestLines(events)
            .Single().DisplayTranslatedText == "old llm preview");

        harness.Settings = harness.Settings with
        {
            EngineType = 0,
            EngineId = "machine-test"
        };
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "one two three four");
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        var machineInvocation = translations.Invocations.Last();
        Assert.AreEqual(TranslationEngineNames.MachineTrans, machineInvocation.Selection!.Engine);
        Assert.AreEqual("one two three four", machineInvocation.Request.Text);
        Assert.AreEqual(2, translations.MaximumActiveStreams);
        machineStream.Emit(new TranslationDeltaEvent("machine translation"));
        machineStream.Complete();
        await harness.WaitForAsync(events => LatestLines(events)
            .Single().DisplayTranslatedText == "machine translation");

        oldLlmStream.Emit(new TranslationDeltaEvent("late old llm translation"));
        oldLlmStream.Complete();
        await harness.DrainAsync();
        Assert.AreEqual(
            "machine translation",
            LatestLines(harness.Events).Single().DisplayTranslatedText);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task DisablingTranslationCancelsSlowLlmAndQueuedFinalsWithoutRevealingLateOutput()
    {
        var slowLlm = new ControlledTranslationStream(ignoreCancellation: true);
        var translations = new RecordingTranslationUseCases((_, _, token) => slowLlm.ReadAsync(token));
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "one two three four");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "one two three four");
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Second queued line.");
        await harness.WaitForAsync(events => LatestLines(events).Count >= 2);

        harness.Settings = harness.Settings with { IsTranslationEnabled = false };
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Third untranslated line.");
        await harness.WaitForAsync(events =>
            LatestLines(events).Count >= 3 && LatestLines(events).All(line => !line.IsTranslating));
        slowLlm.Emit(new TranslationDeltaEvent("late llm translation"));
        await harness.DrainAsync();

        Assert.AreEqual(1, translations.RequestCount);
        Assert.IsTrue(LatestLines(harness.Events)
            .All(line => string.IsNullOrEmpty(line.DisplayTranslatedText)));

        slowLlm.Complete();
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task ReenablingTranslationDoesNotPromoteCanceledLlmPreviewAsFinal()
    {
        var canceledLlm = new ControlledTranslationStream(ignoreCancellation: true);
        var restartedTranslation = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((index, _, token) =>
            index == 1
                ? canceledLlm.ReadAsync(token)
                : restartedTranslation.ReadAsync(token));
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "one two three four");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        canceledLlm.Emit(new TranslationDeltaEvent("readable canceled preview"));
        await harness.WaitForAsync(events =>
            LatestLines(events).Single().DisplayTranslatedText == "readable canceled preview");

        harness.Settings = harness.Settings with { IsTranslationEnabled = false };
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "one two three four");
        await harness.WaitForAsync(events => !LatestLines(events).Single().IsTranslating);
        harness.Settings = harness.Settings with { IsTranslationEnabled = true };
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "one two three four");
        await harness.WaitForAsync(events => LatestLines(events).Single().IsTranslating);
        Assert.AreEqual(1, translations.RequestCount, "The canceled provider still owns the physical gate.");

        canceledLlm.Complete();
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        restartedTranslation.Emit(new TranslationDeltaEvent("fresh final translation"));
        restartedTranslation.Complete();
        await harness.WaitForAsync(events =>
            LatestLines(events).Single().DisplayTranslatedText == "fresh final translation");

        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task RewritingATranslatedPrefixKeepsOldTranslationUntilReplacementCompletes()
    {
        var replacement = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((index, _, token) =>
            index == 1
                ? YieldTranslationAsync("old readable preview", token)
                : replacement.ReadAsync(token));
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "turn left now please");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(events => LatestLines(events)
            .Single().DisplayTranslatedText == "old readable preview");

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "turn right now please");
        await harness.WaitForAsync(events =>
        {
            var line = LatestLines(events).Single();
            return line.OriginalText == "turn right now please"
                   && line.DisplayTranslatedText == "old readable preview";
        });
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(_ => translations.RequestCount == 2);

        replacement.Emit(new TranslationDeltaEvent("new readable"));
        harness.Time.Advance(TimeSpan.FromMilliseconds(200));
        await harness.DrainAsync();
        var translating = LatestLines(harness.Events).Single();
        Assert.IsTrue(translating.IsTranslating);
        Assert.AreEqual("old readable preview", translating.DisplayTranslatedText);

        replacement.Emit(new TranslationDeltaEvent(" replacement"));
        replacement.Complete();
        await harness.WaitForAsync(events =>
        {
            var line = LatestLines(events).Single();
            return !line.IsTranslating
                   && line.DisplayTranslatedText == "new readable replacement";
        });

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "turn right now please");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(2, translations.RequestCount);
    }

    [TestMethod]
    public async Task NonPrefixFinalRevisionKeepsOldTranslationWhenReplacementFails()
    {
        var failedFinal = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((index, _, token) =>
            index == 1
                ? YieldTranslationAsync("old left translation", token)
                : failedFinal.ReadAsync(token));
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "turn left now please");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(events =>
            LatestLines(events).Single().DisplayTranslatedText == "old left translation");

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "turn right now please.");
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        var revised = LatestLines(harness.Events).Single();
        Assert.AreEqual("turn right now please.", revised.OriginalText);
        Assert.AreEqual("old left translation", revised.DisplayTranslatedText);

        failedFinal.Emit(new TranslationFailedEvent(new Error("test.failure", "formal failed")));
        failedFinal.Complete();
        await harness.WaitForAsync(events => !LatestLines(events).Single().IsTranslating);
        Assert.AreEqual(
            "old left translation",
            LatestLines(harness.Events).Single().DisplayTranslatedText);

        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task ExtendingBeyondTheTranslatedPrefixKeepsTheReadablePreview()
    {
        var translations = new RecordingTranslationUseCases("stable readable preview");
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "one two three four");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(events => LatestLines(events)
            .Single().DisplayTranslatedText == "stable readable preview");

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "one two three four five");
        await harness.WaitForAsync(events => LatestLines(events)
            .Single().OriginalText == "one two three four five");
        Assert.AreEqual(
            "stable readable preview",
            LatestLines(harness.Events).Single().DisplayTranslatedText);

        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task CanceledSlowPreviewKeepsSingleFlightAndCannotOverwriteNewRevision()
    {
        var oldStream = new ControlledTranslationStream(ignoreCancellation: true);
        var newStream = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((index, _, token) =>
            index == 1 ? oldStream.ReadAsync(token) : newStream.ReadAsync(token));
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "turn left now please");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        oldStream.Emit(new TranslationDeltaEvent("old readable preview"));
        await harness.WaitForAsync(events => LatestLines(events)
            .Single().DisplayTranslatedText == "old readable preview");

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "turn right now please");
        await harness.WaitForAsync(events => LatestLines(events)
            .Any(line => line.OriginalText == "turn right now please"));
        Assert.AreEqual(
            "old readable preview",
            LatestLines(harness.Events).Single().DisplayTranslatedText);
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.DrainAsync();
        Assert.AreEqual(1, translations.RequestCount, "Canceled provider must retain the single-flight slot.");

        oldStream.Complete();
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        newStream.Emit(new TranslationDeltaEvent("正确的新译文"));
        newStream.Complete();
        await harness.WaitForAsync(events =>
            LatestLines(events).Single().DisplayTranslatedText == "正确的新译文");
        Assert.AreEqual(1, translations.MaximumActiveStreams);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "turn right now please");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual("正确的新译文", LatestLines(harness.Events).Single().DisplayTranslatedText);
    }

    [TestMethod]
    public async Task IdenticalFinalKeepsAnActiveQuietTranslationWithoutRestartingOrExpiringIt()
    {
        var stream = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((_, _, token) => stream.ReadAsync(token));
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false,
            AutoClearInterval = 1
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "one two three four");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(SubtitleSessionCoordinator.AiQuietPeriod + TimeSpan.FromMilliseconds(100));
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "one two three four");
        await harness.DrainAsync();
        Assert.AreEqual(1, translations.RequestCount);
        Assert.IsTrue(LatestLines(harness.Events).Single().IsTranslating);

        harness.Time.Advance(TimeSpan.FromSeconds(1.1));
        await harness.DrainAsync();
        Assert.IsFalse(harness.Events.OfType<SpeechFloatingSubtitleRemovedEvent>().Any());
        stream.Emit(new TranslationDeltaEvent("final readable translation"));
        stream.Complete();
        await harness.WaitForAsync(events => !LatestLines(events).Single().IsTranslating);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        harness.Time.Advance(TimeSpan.FromSeconds(1.1));
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, translations.RequestCount);
    }

    [TestMethod]
    public async Task IdenticalFinalDoesNotRestartTtlAfterQuietTranslationCompleted()
    {
        var translations = new RecordingTranslationUseCases("completed translation");
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false,
            AutoClearInterval = 1
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Partial, "one two three four");
        await harness.WaitForAsync(events => LatestLines(events).Any());
        harness.Time.Advance(SubtitleSessionCoordinator.AiQuietPeriod + TimeSpan.FromMilliseconds(100));
        await harness.WaitForAsync(events => translations.RequestCount == 1
                                             && !LatestLines(events).Single().IsTranslating);

        harness.Time.Advance(TimeSpan.FromMilliseconds(600));
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "one two three four");
        await harness.DrainAsync();
        harness.Time.Advance(TimeSpan.FromMilliseconds(500));
        await harness.WaitForAsync(events => events.OfType<SpeechFloatingSubtitleRemovedEvent>().Any());
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, translations.RequestCount);
    }

    [TestMethod]
    public async Task SharedGatePreventsIgnoredCancellationFromOverlappingTheNextSession()
    {
        var firstStream = new ControlledTranslationStream(ignoreCancellation: true);
        var secondStream = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((index, _, token) =>
            index == 1 ? firstStream.ReadAsync(token) : secondStream.ReadAsync(token));
        var lane = new SubtitleTranslationLane();
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false
        };

        var first = new CoordinatorHarness(settings, translations, lane);
        await first.SendAsync(SpeechRecognitionEventKind.Final, "First session.");
        await first.WaitForAsync(_ => translations.RequestCount == 1);
        await first.DisposeAsync();

        await using var second = new CoordinatorHarness(settings, translations, lane);
        await second.SendAsync(SpeechRecognitionEventKind.Final, "Second session.");
        await second.DrainAsync();
        Assert.AreEqual(1, translations.RequestCount);
        Assert.AreEqual(1, translations.MaximumActiveStreams);

        firstStream.Complete();
        await second.WaitForAsync(_ => translations.RequestCount == 2);
        secondStream.Emit(new TranslationDeltaEvent("second translation"));
        secondStream.Complete();
        await second.WaitForAsync(events => LatestLines(events)
            .Single().DisplayTranslatedText == "second translation");
        await second.SendAsync(SpeechRecognitionEventKind.Stopped);
        await second.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, translations.MaximumActiveStreams);
    }

    [TestMethod]
    public async Task TransientFailureRetriesLongFinalBeforeQueuedNewerSubtitle()
    {
        const string longSource = "First sentence. Second sentence.";
        const string newerSource = "Newer subtitle.";
        var failed = new ControlledStructuredTranslationStream();
        var retry = new ControlledStructuredTranslationStream();
        var newer = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(failed, retry, newer);
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false,
            MaxFloatingHistory = 10
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, longSource);
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        var longLineId = LatestLines(harness.Events).Single().Id;
        await harness.SendAsync(SpeechRecognitionEventKind.Final, newerSource);
        await harness.WaitForAsync(events => LatestLines(events).Count == 2);
        var updatesBeforeFailure = harness.Events
            .OfType<SpeechSubtitleChangedEvent>()
            .Count(item => item.Subtitle.Id == longLineId);

        failed.Fail(new SdkStatusException(503));
        await harness.WaitForAsync(events => events
            .OfType<SpeechSubtitleChangedEvent>()
            .Count(item => item.Subtitle.Id == longLineId) > updatesBeforeFailure);
        Assert.IsTrue(LatestLines(harness.Events)
            .Single(line => line.Id == longLineId).IsTranslating);

        harness.Time.Advance(
            SubtitleSessionCoordinator.FinalTranslationRetryDelay - TimeSpan.FromMilliseconds(100));
        await harness.DrainAsync();
        Assert.AreEqual(1, translations.RequestCount);

        harness.Time.Advance(TimeSpan.FromMilliseconds(200));
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        using (var retryRequest = JsonDocument.Parse(translations.Invocations[1].Request.Text))
        {
            Assert.AreEqual(
                longSource,
                retryRequest.RootElement.GetProperty("current").GetString());
        }

        retry.Emit(StructuredSegment(0, "First sentence. ", "First translated. ", isFinal: true));
        retry.Emit(StructuredSegment(1, "Second sentence.", "Second translated.", isFinal: true));
        retry.Complete();
        await harness.WaitForAsync(_ => translations.RequestCount == 3);
        using (var newerRequest = JsonDocument.Parse(translations.Invocations[2].Request.Text))
        {
            Assert.AreEqual(
                newerSource,
                newerRequest.RootElement.GetProperty("current").GetString());
        }

        newer.Emit(StructuredSegment(0, newerSource, "Newer translated.", isFinal: true));
        newer.Complete();
        await harness.WaitForAsync(events => LatestLines(events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .All(line => !line.IsTranslating));
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var translationsBySource = LatestLines(harness.Events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .ToDictionary(line => line.OriginalText, line => line.DisplayTranslatedText);
        Assert.AreEqual("First translated. ", translationsBySource["First sentence. "]);
        Assert.AreEqual("Second translated.", translationsBySource["Second sentence."]);
        Assert.AreEqual("Newer translated.", translationsBySource[newerSource]);
    }

    [TestMethod]
    public async Task RepeatedTransientFailuresRetryOlderSubtitleBeforeContinuingQueue()
    {
        var failed = new ControlledStructuredTranslationStream();
        var failedRetry = new ControlledStructuredTranslationStream();
        var recovered = new ControlledStructuredTranslationStream();
        var newer = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(
            failed,
            failedRetry,
            recovered,
            newer);
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Older subtitle.");
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        var olderLineId = LatestLines(harness.Events).Single().Id;
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Newer subtitle.");
        await harness.WaitForAsync(events => LatestLines(events).Count == 2);
        var updatesBeforeFailure = harness.Events
            .OfType<SpeechSubtitleChangedEvent>()
            .Count(item => item.Subtitle.Id == olderLineId);

        failed.Fail(new SdkStatusException(503));
        await harness.WaitForAsync(events => events
            .OfType<SpeechSubtitleChangedEvent>()
            .Count(item => item.Subtitle.Id == olderLineId) > updatesBeforeFailure);
        harness.Time.Advance(SubtitleSessionCoordinator.FinalTranslationRetryDelay);
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        using (var retryRequest = JsonDocument.Parse(translations.Invocations[1].Request.Text))
        {
            Assert.AreEqual(
                "Older subtitle.",
                retryRequest.RootElement.GetProperty("current").GetString());
        }
        var updatesBeforeSecondFailure = harness.Events
            .OfType<SpeechSubtitleChangedEvent>()
            .Count(item => item.Subtitle.Id == olderLineId);
        failedRetry.Fail(new SdkStatusException(503));
        await harness.WaitForAsync(events => events
            .OfType<SpeechSubtitleChangedEvent>()
            .Count(item => item.Subtitle.Id == olderLineId) > updatesBeforeSecondFailure);
        Assert.AreEqual(2, translations.RequestCount);
        harness.Time.Advance(
            SubtitleSessionCoordinator.GetFinalTranslationRetryDelay(3));
        await harness.WaitForAsync(_ => translations.RequestCount == 3);
        using (var recoveredRequest = JsonDocument.Parse(translations.Invocations[2].Request.Text))
        {
            Assert.AreEqual(
                "Older subtitle.",
                recoveredRequest.RootElement.GetProperty("current").GetString());
        }

        recovered.Emit(StructuredSegment(
            0,
            "Older subtitle.",
            "Older translated.",
            isFinal: true));
        recovered.Complete();
        await harness.WaitForAsync(_ => translations.RequestCount == 4);
        using (var newerRequest = JsonDocument.Parse(translations.Invocations[3].Request.Text))
        {
            Assert.AreEqual(
                "Newer subtitle.",
                newerRequest.RootElement.GetProperty("current").GetString());
        }
        newer.Emit(StructuredSegment(0, "Newer subtitle.", "Newer translated.", isFinal: true));
        newer.Complete();
        await harness.WaitForAsync(events => LatestLines(events)
            .All(line => !line.IsTranslating));
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(4, translations.RequestCount);
        Assert.AreEqual(
            "Older translated.",
            LatestLines(harness.Events)
                .Single(line => line.OriginalText == "Older subtitle.")
                .DisplayTranslatedText);
        Assert.AreEqual(
            "Newer translated.",
            LatestLines(harness.Events)
                .Single(line => line.OriginalText == "Newer subtitle.")
                .DisplayTranslatedText);
    }

    [TestMethod]
    public async Task TransientFailuresWithoutHttpResponseRetryFinalTranslation()
    {
        Exception[] failures =
        [
            new HttpRequestException("The connection closed before a response was received."),
            new SdkStatusException(0)
        ];

        foreach (var failure in failures)
        {
            var failed = new ControlledStructuredTranslationStream();
            var retry = new ControlledStructuredTranslationStream();
            var translations = new RecordingStructuredTranslationUseCases(failed, retry);
            var settings = CreateSettings(translationEnabled: true) with
            {
                IsRealTimePreviewEnabled = false
            };
            await using var harness = new CoordinatorHarness(settings, translations);

            await harness.SendAsync(SpeechRecognitionEventKind.Final, "Retry this subtitle.");
            await harness.WaitForAsync(_ => translations.RequestCount == 1);
            var lineId = LatestLines(harness.Events).Single().Id;
            var updatesBeforeFailure = harness.Events
                .OfType<SpeechSubtitleChangedEvent>()
                .Count(item => item.Subtitle.Id == lineId);
            failed.Fail(failure);
            await harness.WaitForAsync(events => events
                .OfType<SpeechSubtitleChangedEvent>()
                .Count(item => item.Subtitle.Id == lineId) > updatesBeforeFailure);

            harness.Time.Advance(SubtitleSessionCoordinator.FinalTranslationRetryDelay);
            await harness.WaitForAsync(_ => translations.RequestCount == 2);
            retry.Emit(StructuredSegment(
                0,
                "Retry this subtitle.",
                "Retried translation.",
                isFinal: true));
            retry.Complete();
            await harness.WaitForAsync(events =>
                LatestLines(events).Single().DisplayTranslatedText == "Retried translation.");
            await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
            await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [TestMethod]
    public async Task PermanentFailureContinuesWithQueuedNewerSubtitleWithoutRetry()
    {
        var failed = new ControlledStructuredTranslationStream();
        var newer = new ControlledStructuredTranslationStream();
        var translations = new RecordingStructuredTranslationUseCases(failed, newer);
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Older subtitle.");
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Newer subtitle.");
        failed.Fail(new SdkStatusException(400));
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        using (var newerRequest = JsonDocument.Parse(translations.Invocations[1].Request.Text))
        {
            Assert.AreEqual(
                "Newer subtitle.",
                newerRequest.RootElement.GetProperty("current").GetString());
        }

        newer.Emit(StructuredSegment(0, "Newer subtitle.", "Newer translated.", isFinal: true));
        newer.Complete();
        await harness.WaitForAsync(events => LatestLines(events)
            .All(line => !line.IsTranslating));
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(2, translations.RequestCount);
        Assert.AreEqual(
            string.Empty,
            LatestLines(harness.Events)
                .Single(line => line.OriginalText == "Older subtitle.")
                .DisplayTranslatedText);
        Assert.AreEqual(
            "Newer translated.",
            LatestLines(harness.Events)
                .Single(line => line.OriginalText == "Newer subtitle.")
                .DisplayTranslatedText);
    }

    [TestMethod]
    public async Task TranslationTimeoutRetriesCancellableLlmWithoutAppendingErrorText()
    {
        var timedOut = new ControlledTranslationStream();
        var retry = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((index, _, token) =>
            index == 1 ? timedOut.ReadAsync(token) : retry.ReadAsync(token));
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "This translation will time out.");
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        var updatesBeforeTimeout = harness.Events
            .OfType<SpeechSubtitleChangedEvent>()
            .Count();
        harness.Time.Advance(TimeSpan.FromSeconds(30.1));
        await harness.WaitForAsync(events => events
            .OfType<SpeechSubtitleChangedEvent>()
            .Count() > updatesBeforeTimeout);
        harness.Time.Advance(SubtitleSessionCoordinator.FinalTranslationRetryDelay);
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        retry.Emit(new TranslationDeltaEvent("Recovered timeout translation."));
        retry.Complete();
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var line = LatestLines(harness.Events).Single();
        Assert.AreEqual(2, translations.RequestCount);
        Assert.IsFalse(line.IsTranslating);
        Assert.AreEqual("Recovered timeout translation.", line.DisplayTranslatedText);
        Assert.AreEqual("This translation will time out.", line.OriginalText);
    }

    [TestMethod]
    public async Task TranslationTimeoutCompletesEvenWhenTheProviderIgnoresCancellation()
    {
        var stream = new ControlledTranslationStream(ignoreCancellation: true);
        var translations = new RecordingTranslationUseCases((_, _, token) => stream.ReadAsync(token));
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "A provider can remain stuck.");
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        harness.Time.Advance(TimeSpan.FromSeconds(30.1));
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var line = LatestLines(harness.Events).Single();
        Assert.IsFalse(line.IsTranslating);
        Assert.AreEqual(string.Empty, line.DisplayTranslatedText);
        stream.Complete();
    }

    [TestMethod]
    public async Task TimedOutProviderFailsQueuedLinesWithoutSerialTimeouts()
    {
        var stream = new ControlledTranslationStream(ignoreCancellation: true);
        var translations = new RecordingTranslationUseCases((_, _, token) => stream.ReadAsync(token));
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false,
            MaxFloatingHistory = 10
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "First stuck line.");
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Second queued line.");
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Third queued line.");
        await harness.WaitForAsync(events => LatestLines(events).Count == 3);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);

        harness.Time.Advance(TimeSpan.FromSeconds(30.1));
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, translations.RequestCount);
        Assert.IsTrue(LatestLines(harness.Events).All(line => !line.IsTranslating));
        Assert.IsTrue(LatestLines(harness.Events).All(line =>
            string.IsNullOrEmpty(line.DisplayTranslatedText)));
        stream.Complete();
    }

    [TestMethod]
    public async Task TimedOutProviderClearsPendingPreviewAndRejectsNewPreviewWhileRecording()
    {
        var stream = new ControlledTranslationStream(ignoreCancellation: true);
        var translations = new RecordingTranslationUseCases((_, _, token) => stream.ReadAsync(token));
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "First stuck line.");
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        const string pendingSource = "one two three four";
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, pendingSource);
        await harness.WaitForAsync(events => LatestLines(events).Count == 2);
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.WaitForAsync(events =>
            translations.RequestCount == 1
            && LatestLines(events).Single(line => line.OriginalText == pendingSource).IsTranslating);

        var extendedSource = pendingSource + "x";
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, extendedSource);
        for (var index = 0; index < 59; index++)
        {
            harness.Time.Advance(TimeSpan.FromMilliseconds(500));
            extendedSource += "x";
            await harness.SendAsync(SpeechRecognitionEventKind.Partial, extendedSource);
        }
        await harness.WaitForAsync(events => LatestLines(events).All(line => !line.IsTranslating));
        Assert.AreEqual(1, translations.RequestCount);

        extendedSource += "x";
        await harness.SendAsync(SpeechRecognitionEventKind.Partial, extendedSource);
        harness.Time.Advance(
            SubtitleSessionCoordinator.AiPreviewDebounce + TimeSpan.FromMilliseconds(50));
        await harness.DrainAsync();

        Assert.AreEqual(1, translations.RequestCount);
        Assert.IsFalse(LatestLines(harness.Events).OrderBy(line => line.Id).Last().IsTranslating);

        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        stream.Complete();
    }

    [TestMethod]
    public async Task TimedOutSharedLaneFailsNextSessionFastAndRecoversAfterProviderExit()
    {
        var stuck = new ControlledTranslationStream(ignoreCancellation: true);
        var recovered = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((index, _, token) =>
            index == 1 ? stuck.ReadAsync(token) : recovered.ReadAsync(token));
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false
        };
        var lane = new SubtitleTranslationLane();

        await using var first = new CoordinatorHarness(settings, translations, lane);
        await first.SendAsync(SpeechRecognitionEventKind.Final, "First stuck session.");
        await first.WaitForAsync(_ => translations.RequestCount == 1);
        await first.SendAsync(SpeechRecognitionEventKind.Stopped);
        first.Time.Advance(TimeSpan.FromSeconds(30.1));
        await first.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsTrue(lane.IsUnavailable());

        await using var second = new CoordinatorHarness(settings, translations, lane);
        await second.SendAsync(SpeechRecognitionEventKind.Final, "Rejected while provider is stuck.");
        await second.WaitForAsync(events => LatestLines(events).Any());
        await second.DrainAsync();

        Assert.AreEqual(1, translations.RequestCount);
        Assert.IsFalse(LatestLines(second.Events).Single().IsTranslating);

        stuck.Complete();
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        {
            while (lane.IsUnavailable())
                await Task.Delay(5, timeout.Token);
        }

        await second.SendAsync(SpeechRecognitionEventKind.Final, "Translation recovers.");
        await second.WaitForAsync(_ => translations.RequestCount == 2);
        recovered.Emit(new TranslationDeltaEvent("recovered translation"));
        recovered.Complete();
        await second.WaitForAsync(events => LatestLines(events)
            .Any(line => line.DisplayTranslatedText == "recovered translation"));
        await second.SendAsync(SpeechRecognitionEventKind.Stopped);
        await second.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task TimedOutSelectionRetriesWithinSessionAfterProviderExit()
    {
        var timedOut = new ControlledTranslationStream();
        var recovered = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((index, _, token) =>
            index == 1 ? timedOut.ReadAsync(token) : recovered.ReadAsync(token));
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false
        };
        var lane = new SubtitleTranslationLane();

        await using var harness = new CoordinatorHarness(settings, translations, lane);
        await harness.SendAsync(SpeechRecognitionEventKind.Final, "First timeout.");
        await harness.WaitForAsync(events => LatestLines(events).Any(line => line.IsTranslating));
        var updatesBeforeTimeout = harness.Events
            .OfType<SpeechSubtitleChangedEvent>()
            .Count();
        harness.Time.Advance(TimeSpan.FromSeconds(30.1));
        await harness.WaitForAsync(events => events
            .OfType<SpeechSubtitleChangedEvent>()
            .Count() > updatesBeforeTimeout);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        {
            while (lane.IsUnavailable())
                await Task.Delay(5, timeout.Token);
        }

        harness.Time.Advance(SubtitleSessionCoordinator.FinalTranslationRetryDelay);
        await harness.WaitForAsync(_ => translations.RequestCount == 2);
        recovered.Emit(new TranslationDeltaEvent("same session translation"));
        recovered.Complete();
        await harness.WaitForAsync(events => LatestLines(events)
            .Single().DisplayTranslatedText == "same session translation");
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task WaiterTimeoutTripsLaneWhenCanceledHolderStillOwnsTheProvider()
    {
        var stuck = new ControlledTranslationStream(ignoreCancellation: true);
        var recovered = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((index, _, token) =>
            index == 1 ? stuck.ReadAsync(token) : recovered.ReadAsync(token));
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false
        };
        var lane = new SubtitleTranslationLane();

        var holder = new CoordinatorHarness(settings, translations, lane);
        await holder.SendAsync(SpeechRecognitionEventKind.Final, "Canceled holder.");
        await holder.WaitForAsync(_ => translations.RequestCount == 1);
        await holder.DisposeAsync();
        Assert.IsFalse(lane.IsUnavailable());

        await using var waiter = new CoordinatorHarness(settings, translations, lane);
        await waiter.SendAsync(SpeechRecognitionEventKind.Final, "Waiting session.");
        await waiter.WaitForAsync(events => LatestLines(events).Any(line => line.IsTranslating));
        waiter.Time.Advance(TimeSpan.FromSeconds(30.1));
        await waiter.WaitForAsync(events => LatestLines(events).Count == 1
                                           && LatestLines(events).All(line => !line.IsTranslating));
        Assert.IsTrue(lane.IsUnavailable());
        await waiter.SendAsync(SpeechRecognitionEventKind.Stopped);
        await waiter.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        await using var next = new CoordinatorHarness(settings, translations, lane);
        await next.SendAsync(SpeechRecognitionEventKind.Final, "Rejected behind stuck holder.");
        await next.WaitForAsync(events => LatestLines(events).Any());
        await next.DrainAsync();
        Assert.AreEqual(1, translations.RequestCount);
        Assert.IsFalse(LatestLines(next.Events).Single().IsTranslating);

        stuck.Complete();
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        {
            while (lane.IsUnavailable())
                await Task.Delay(5, timeout.Token);
        }

        await next.SendAsync(SpeechRecognitionEventKind.Final, "Recovered after holder exit.");
        await next.WaitForAsync(_ => translations.RequestCount == 2);
        recovered.Emit(new TranslationDeltaEvent("holder recovery translation"));
        recovered.Complete();
        await next.WaitForAsync(events => LatestLines(events)
            .Any(line => line.DisplayTranslatedText == "holder recovery translation"));
        await next.SendAsync(SpeechRecognitionEventKind.Stopped);
        await next.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task MachineTranslationRetainsTheShorterTimeout()
    {
        var stream = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((_, _, token) => stream.ReadAsync(token));
        var settings = CreateSettings(translationEnabled: true) with
        {
            EngineType = 0,
            IsRealTimePreviewEnabled = false
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Machine timeout source.");
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        harness.Time.Advance(TimeSpan.FromSeconds(15.1));
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsFalse(LatestLines(harness.Events).Single().IsTranslating);
    }

    [TestMethod]
    public async Task TranslationFailureStopsLoadingWithoutAppendingProviderError()
    {
        var stream = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases(
            (_, _, token) => stream.ReadAsync(token));
        await using var harness = new CoordinatorHarness(
            CreateSettings(translationEnabled: true),
            translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Keep only this source.");
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        stream.Emit(new TranslationFailedEvent(new Error("test.failure", "provider secret error")));
        stream.Complete();
        await harness.WaitForAsync(events => LatestLines(events).Any(line => !line.IsTranslating));
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        var line = LatestLines(harness.Events).Single();
        Assert.AreEqual("Keep only this source.", line.OriginalText);
        Assert.AreEqual(string.Empty, line.DisplayTranslatedText);
        Assert.IsFalse(line.OriginalText.Contains("provider secret error", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FinalTranslationBacklogKeepsEveryUnstartedJobInFifoOrder()
    {
        var first = new ControlledTranslationStream();
        var translations = new RecordingTranslationUseCases((index, _, token) =>
            index == 1
                ? first.ReadAsync(token)
                : YieldTranslationAsync($"translation {index}", token));
        var settings = CreateSettings(translationEnabled: true) with
        {
            IsRealTimePreviewEnabled = false,
            MaxFloatingHistory = 40
        };
        await using var harness = new CoordinatorHarness(settings, translations);

        await harness.SendAsync(SpeechRecognitionEventKind.Final, "Source line 1.");
        await harness.WaitForAsync(_ => translations.RequestCount == 1);
        for (var index = 2; index <= 34; index++)
            await harness.SendAsync(SpeechRecognitionEventKind.Final, $"Source line {index}.");
        await harness.WaitForAsync(events => LatestLines(events).Count == 34);
        await harness.SendAsync(SpeechRecognitionEventKind.Stopped);
        first.Emit(new TranslationDeltaEvent("translation 1"));
        first.Complete();
        await harness.WaitForAsync(events =>
        {
            var latest = LatestLines(events);
            return translations.RequestCount == 34
                   && latest.Count == 34
                   && latest.All(line => !line.IsTranslating
                                         && !string.IsNullOrWhiteSpace(
                                             line.DisplayTranslatedText));
        });
        await harness.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var lines = LatestLines(harness.Events).OrderBy(line => line.Id).ToArray();
        Assert.HasCount(34, lines);
        Assert.AreEqual(34, translations.RequestCount);
        for (var index = 1; index <= 34; index++)
        {
            var source = $"Source line {index}.";
            using var request = JsonDocument.Parse(
                translations.Invocations[index - 1].Request.Text);
            Assert.AreEqual(
                source,
                request.RootElement.GetProperty("current").GetString());
            Assert.AreEqual(source, lines[index - 1].OriginalText);
            Assert.AreEqual(
                $"translation {index}",
                lines[index - 1].DisplayTranslatedText);
        }
        Assert.IsFalse(harness.Events.OfType<SpeechFloatingSubtitleRemovedEvent>().Any());
        Assert.AreEqual(1, translations.MaximumActiveStreams);
    }

    private static async IAsyncEnumerable<TranslationEvent> YieldTranslationAsync(
        string text,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new TranslationDeltaEvent(text);
        await Task.CompletedTask;
    }

    private static SpeechRecognitionSettings CreateSettings(bool translationEnabled)
    {
        var initial = SettingsTestData.CreateBundle().SpeechRecognition;
        return initial with
        {
            RecognitionLanguage = "en",
            IsTranslationEnabled = translationEnabled,
            IsRealTimePreviewEnabled = translationEnabled,
            TargetLanguage = "zh-Hans",
            EngineId = "test",
            EngineType = 1,
            MaxSentencesPerLine = 1,
            MaxFloatingHistory = 20,
            AutoClearInterval = 0
        };
    }

    private static JsonElement StructuredSegment(
        int sequence,
        string source,
        string translation,
        bool isFinal) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["seq"] = sequence,
            ["source"] = source,
            ["translation"] = translation,
            ["final"] = isFinal
        });

    private static SpeechSubtitleLine AssertExactlyOneLatestLine(
        IReadOnlyCollection<SpeechSessionEvent> events)
    {
        var lines = LatestLines(events)
            .Where(line => !string.IsNullOrWhiteSpace(line.OriginalText))
            .ToArray();
        Assert.HasCount(1, lines);
        return lines[0];
    }

    private static IReadOnlyList<SpeechSubtitleLine> LatestLines(
        IReadOnlyCollection<SpeechSessionEvent> events) =>
        events.OfType<SpeechSubtitleChangedEvent>()
            .GroupBy(item => item.Subtitle.Id)
            .Select(group => group.Last().Subtitle)
            .ToArray();

    private sealed class CoordinatorHarness : IAsyncDisposable
    {
        private readonly Channel<SpeechRecognitionEvent> _recognition = Channel.CreateUnbounded<SpeechRecognitionEvent>();
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ConcurrentQueue<SpeechSessionEvent> _events = new();

        public CoordinatorHarness(
            SpeechRecognitionSettings settings,
            ITranslationUseCases? translation = null,
            SubtitleTranslationLane? aiTranslationLane = null,
            SubtitleTranslationLane? machineTranslationLane = null,
            ManualTimeProvider? timeProvider = null,
            SubtitleFloatingLifecycleRegistry? floatingLifecycle = null,
            Func<long>? nextSubtitleId = null)
        {
            Time = timeProvider
                   ?? new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Settings = settings;
            long nextId = 0;
            var coordinator = new SubtitleSessionCoordinator(
                () => Settings,
                translation ?? new ImmediateTranslationUseCases(),
                new BuiltInTranslationLanguageCatalog(),
                NullLogger.Instance,
                Time,
                nextSubtitleId ?? (() => Interlocked.Increment(ref nextId)),
                item => _events.Enqueue(item),
                aiTranslationLane,
                machineTranslationLane,
                floatingLifecycle);
            Completion = coordinator.RunAsync(
                _recognition.Reader.ReadAllAsync(_lifetime.Token),
                _lifetime.Token);
        }

        public SpeechRecognitionSettings Settings { get; set; }
        public ManualTimeProvider Time { get; }
        public Task Completion { get; }
        public IReadOnlyCollection<SpeechSessionEvent> Events => _events.ToArray();

        public ValueTask SendAsync(SpeechRecognitionEventKind kind, string? text = null) =>
            _recognition.Writer.WriteAsync(new SpeechRecognitionEvent(kind, text), _lifetime.Token);

        public async Task WaitForAsync(Func<IReadOnlyCollection<SpeechSessionEvent>, bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!predicate(Events))
                await Task.Delay(5, timeout.Token);
        }

        public async Task DrainAsync()
        {
            await Task.Yield();
            await Task.Delay(20);
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            try
            {
                await Completion.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
            }
            _lifetime.Dispose();
        }
    }

    private sealed class ImmediateTranslationUseCases : ITranslationUseCases
    {
        public ITranslationSession Prepare(TranslationProviderSelection? provider = null) =>
            new ImmediateTranslationSession();

        public Task<Result<TranslationResponse>> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TranslationEvent> StreamAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ImmediateTranslationSession : ITranslationSession
    {
        public bool SupportsIdentifiedStreaming => false;

        public Task<TranslationResponse> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TranslationResponse("即时翻译"));

        public async IAsyncEnumerable<TranslationEvent> StreamAsync(
            TranslationRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new TranslationDeltaEvent("即时翻译");
            await Task.CompletedTask;
        }

        public IAsyncEnumerable<IdentifiedTranslationDelta> StreamIdentifiedAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingTranslationUseCases : ITranslationUseCases
    {
        private readonly Func<int, TranslationRequest, CancellationToken, IAsyncEnumerable<TranslationEvent>> _stream;
        private readonly ConcurrentQueue<TranslationInvocation> _invocations = new();
        private readonly object _activitySync = new();
        private int _nextRequest;
        private int _activeStreams;

        public RecordingTranslationUseCases(string response)
            : this((_, _, token) => Immediate(response, token))
        {
        }

        public RecordingTranslationUseCases(
            Func<int, TranslationRequest, CancellationToken, IAsyncEnumerable<TranslationEvent>> stream)
        {
            _stream = stream;
        }

        public int RequestCount => _invocations.Count;
        public int MaximumActiveStreams { get; private set; }
        public IReadOnlyList<TranslationInvocation> Invocations => _invocations.ToArray();

        public ITranslationSession Prepare(TranslationProviderSelection? provider = null) =>
            new RecordingTranslationSession(this, provider);

        public Task<Result<TranslationResponse>> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TranslationEvent> StreamAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private async IAsyncEnumerable<TranslationEvent> RunAsync(
            TranslationProviderSelection? selection,
            TranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _nextRequest);
            _invocations.Enqueue(new TranslationInvocation(selection, request));
            lock (_activitySync)
            {
                _activeStreams++;
                MaximumActiveStreams = Math.Max(MaximumActiveStreams, _activeStreams);
            }
            try
            {
                await foreach (var item in _stream(index, request, cancellationToken)
                                   .WithCancellation(cancellationToken))
                {
                    yield return item;
                }
            }
            finally
            {
                lock (_activitySync)
                    _activeStreams--;
            }
        }

        private static async IAsyncEnumerable<TranslationEvent> Immediate(
            string response,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new TranslationDeltaEvent(response);
            await Task.CompletedTask;
        }

        private sealed class RecordingTranslationSession(
            RecordingTranslationUseCases owner,
            TranslationProviderSelection? selection) : ITranslationSession
        {
            public bool SupportsIdentifiedStreaming => false;

            public Task<TranslationResponse> TranslateAsync(
                TranslationRequest request,
                CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public IAsyncEnumerable<TranslationEvent> StreamAsync(
                TranslationRequest request,
                CancellationToken cancellationToken = default) =>
                owner.RunAsync(selection, request, cancellationToken);

            public IAsyncEnumerable<IdentifiedTranslationDelta> StreamIdentifiedAsync(
                TranslationRequest request,
                CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();
        }
    }

    private sealed class RecordingStructuredTranslationUseCases : ITranslationUseCases
    {
        private readonly ControlledStructuredTranslationStream[] _streams;
        private readonly ConcurrentQueue<TranslationInvocation> _invocations = new();
        private int _nextStream;
        private int _unstructuredRequestCount;

        public RecordingStructuredTranslationUseCases(
            params ControlledStructuredTranslationStream[] streams)
        {
            _streams = streams;
        }

        public int RequestCount => _invocations.Count;
        public int UnstructuredRequestCount => Volatile.Read(ref _unstructuredRequestCount);
        public IReadOnlyList<TranslationInvocation> Invocations => _invocations.ToArray();

        public ITranslationSession Prepare(TranslationProviderSelection? provider = null) =>
            new RecordingStructuredTranslationSession(this, provider);

        public Task<Result<TranslationResponse>> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TranslationEvent> StreamAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private async IAsyncEnumerable<JsonElement> RunStructuredAsync(
            TranslationProviderSelection? selection,
            TranslationRequest request,
            string runtimeContract,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(runtimeContract));
            _invocations.Enqueue(new TranslationInvocation(selection, request));
            var streamIndex = Interlocked.Increment(ref _nextStream) - 1;
            if (streamIndex >= _streams.Length)
                throw new InvalidOperationException("No structured test stream was configured.");
            await foreach (var item in _streams[streamIndex].ReadAsync(cancellationToken))
                yield return item;
        }

        private async IAsyncEnumerable<TranslationEvent> RunUnstructuredAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _unstructuredRequestCount);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        private sealed class RecordingStructuredTranslationSession(
            RecordingStructuredTranslationUseCases owner,
            TranslationProviderSelection? selection) :
            ITranslationSession,
            IStructuredJsonLinesTranslationSession
        {
            public bool SupportsIdentifiedStreaming => false;

            public Task<TranslationResponse> TranslateAsync(
                TranslationRequest request,
                CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public IAsyncEnumerable<TranslationEvent> StreamAsync(
                TranslationRequest request,
                CancellationToken cancellationToken = default) =>
                owner.RunUnstructuredAsync(cancellationToken);

            public IAsyncEnumerable<JsonElement> StreamJsonLinesAsync(
                TranslationRequest request,
                string runtimeContract,
                CancellationToken cancellationToken = default) =>
                owner.RunStructuredAsync(
                    selection,
                    request,
                    runtimeContract,
                    cancellationToken);

            public IAsyncEnumerable<IdentifiedTranslationDelta> StreamIdentifiedAsync(
                TranslationRequest request,
                CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();
        }
    }

    private sealed record TranslationInvocation(
        TranslationProviderSelection? Selection,
        TranslationRequest Request);

    private sealed class ControlledTranslationStream(bool ignoreCancellation = false)
    {
        private readonly Channel<TranslationEvent> _events = Channel.CreateUnbounded<TranslationEvent>();

        public void Emit(TranslationEvent item) =>
            Assert.IsTrue(_events.Writer.TryWrite(item));

        public void Complete() => _events.Writer.TryComplete();

        public async IAsyncEnumerable<TranslationEvent> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var effectiveToken = ignoreCancellation ? CancellationToken.None : cancellationToken;
            await foreach (var item in _events.Reader.ReadAllAsync(effectiveToken))
                yield return item;
        }
    }

    private sealed class ControlledStructuredTranslationStream
    {
        private readonly Channel<JsonElement> _items = Channel.CreateUnbounded<JsonElement>();

        public void Emit(JsonElement item) =>
            Assert.IsTrue(_items.Writer.TryWrite(item.Clone()));

        public void Complete() => _items.Writer.TryComplete();

        public void Fail(Exception exception) => _items.Writer.TryComplete(exception);

        public async IAsyncEnumerable<JsonElement> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var item in _items.Reader.ReadAllAsync(cancellationToken))
                yield return item;
        }
    }

    private sealed class SdkStatusException(int status) : Exception($"HTTP {status}")
    {
        public int Status { get; } = status;
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly DateTimeOffset _start = start;
    private long _timestamp;
    private TimeSpan _wallClockOffset;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
            return _start + TimeSpan.FromTicks(_timestamp) + _wallClockOffset;
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_sync)
            return _timestamp;
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        lock (_sync)
        {
            _timers.Add(timer);
            Change(timer, dueTime, period);
        }
        return timer;
    }

    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
        long target;
        lock (_sync)
            target = checked(_timestamp + duration.Ticks);

        while (true)
        {
            ManualTimer[] due;
            lock (_sync)
            {
                var next = _timers
                    .Where(timer => !timer.IsDisposed)
                    .Select(timer => timer.NextTimestamp)
                    .DefaultIfEmpty(long.MaxValue)
                    .Min();
                if (next > target)
                {
                    _timestamp = target;
                    return;
                }
                _timestamp = next;
                due = _timers
                    .Where(timer => !timer.IsDisposed && timer.NextTimestamp == next)
                    .ToArray();
                foreach (var timer in due)
                {
                    timer.NextTimestamp = timer.PeriodTicks > 0
                        ? checked(timer.NextTimestamp + timer.PeriodTicks)
                        : long.MaxValue;
                }
            }
            foreach (var timer in due)
                timer.Invoke();
        }
    }

    public void JumpWallClock(TimeSpan offset)
    {
        lock (_sync)
            _wallClockOffset += offset;
    }

    private void Change(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        timer.PeriodTicks = period == Timeout.InfiniteTimeSpan ? -1 : period.Ticks;
        timer.NextTimestamp = dueTime == Timeout.InfiniteTimeSpan
            ? long.MaxValue
            : checked(_timestamp + Math.Max(0, dueTime.Ticks));
    }

    private void Remove(ManualTimer timer)
    {
        lock (_sync)
            _timers.Remove(timer);
    }

    private sealed class ManualTimer(
        ManualTimeProvider owner,
        TimerCallback callback,
        object? state) : ITimer
    {
        public bool IsDisposed { get; private set; }
        public long NextTimestamp { get; set; } = long.MaxValue;
        public long PeriodTicks { get; set; } = -1;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._sync)
            {
                if (IsDisposed)
                    return false;
                owner.Change(this, dueTime, period);
                return true;
            }
        }

        public void Dispose()
        {
            lock (owner._sync)
            {
                if (IsDisposed)
                    return;
                IsDisposed = true;
                owner.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void Invoke() => callback(state);
    }
}
