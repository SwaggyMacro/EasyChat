using System.Text.Json;
using EasyChat.Application.Speech;

namespace EasyChat.Application.Tests.Speech;

[TestClass]
public sealed class JsonLinesSubtitlePlanBuilderTests
{
    [TestMethod]
    public void ValidRecordsExposeAggregatePrefixesAndCompleteAnImmutablePlan()
    {
        var builder = new JsonLinesSubtitlePlanBuilder("Hello. Next.");

        var firstAccepted = builder.TryAdd(
            Parse("""
                {"seq":0,"source":"Hello.","translation":"Hello translated.","final":true}
                """),
            out var first);
        var secondAccepted = builder.TryAdd(
            Parse("""
                {"seq":1,"source":" Next.","translation":"Next translated.","final":true}
                """),
            out var second);

        Assert.IsTrue(firstAccepted);
        Assert.AreEqual("Hello translated.", first.Translation);
        Assert.AreEqual(6, first.CoveredLength);
        Assert.AreEqual(1, first.Count);
        Assert.IsTrue(secondAccepted);
        Assert.AreEqual(
            "Hello translated.Next translated.",
            second.Translation);
        Assert.AreEqual(12, second.CoveredLength);
        Assert.AreEqual(2, second.Count);
        Assert.IsTrue(builder.TryComplete(out var plan));
        Assert.HasCount(2, plan.Segments);
        Assert.AreEqual(0, plan.Segments[0].Sequence);
        Assert.AreEqual("Hello.", plan.Segments[0].Source);
        Assert.AreEqual("Hello translated.", plan.Segments[0].Translation);
        Assert.IsTrue(plan.Segments[0].IsFinal);
        Assert.AreEqual(" Next.", plan.Segments[1].Source);
    }

    [TestMethod]
    public void LastRecordMayBeNonFinal()
    {
        var builder = new JsonLinesSubtitlePlanBuilder("unfinished thought");

        Assert.IsTrue(builder.TryAdd(
            Parse("""
                {"seq":0,"source":"unfinished thought","translation":"pending","final":false}
                """),
            out _));

        Assert.IsTrue(builder.TryComplete(out var plan));
        Assert.IsFalse(plan.Segments[0].IsFinal);
    }

    [TestMethod]
    public void RecordAfterNonFinalRecordPermanentlyInvalidatesPlan()
    {
        var builder = new JsonLinesSubtitlePlanBuilder("one two");
        Assert.IsTrue(builder.TryAdd(
            Parse("""
                {"seq":0,"source":"one ","translation":"first","final":false}
                """),
            out _));

        Assert.IsFalse(builder.TryAdd(
            Parse("""
                {"seq":1,"source":"two","translation":"second","final":true}
                """),
            out _));
        Assert.IsFalse(builder.TryComplete(out _));
    }

    [TestMethod]
    public void CombinedCompleteSentencesRemainAValidReadableSegment()
    {
        const string source = "First. Second.";
        var builder = new JsonLinesSubtitlePlanBuilder(source);

        Assert.IsTrue(builder.TryAdd(
            Parse("""
                {"seq":0,"source":"First. Second.","translation":"combined","final":true}
                """),
            out _));
        Assert.IsTrue(builder.TryComplete(out var plan));
        Assert.AreEqual(source, plan.Segments[0].Source);
    }

    [TestMethod]
    [DataRow("{\"seq\":0,\"source\":\"First. unfinished\",\"translation\":\"combined\",\"final\":true}")]
    [DataRow("{\"seq\":0,\"source\":\"First.\",\"translation\":\"translated\",\"final\":false}")]
    public void RecordCannotMislabelCompletedSentences(string json)
    {
        var source = Parse(json).GetProperty("source").GetString()!;
        var builder = new JsonLinesSubtitlePlanBuilder(source);

        Assert.IsFalse(builder.TryAdd(Parse(json), out _));
        Assert.IsFalse(builder.TryComplete(out _));
    }

    [TestMethod]
    public void BoundaryWhitespaceOmittedByLlmIsRecoveredFromTheSourceSnapshot()
    {
        var builder = new JsonLinesSubtitlePlanBuilder("Hello. Next.");

        Assert.IsTrue(builder.TryAdd(
            Parse("""
                {"seq":0,"source":"Hello.","translation":"first","final":true}
                """),
            out _));
        Assert.IsTrue(builder.TryAdd(
            Parse("""
                {"seq":1,"source":"Next.","translation":"second","final":true}
                """),
            out _));
        Assert.IsTrue(builder.TryComplete(out var plan));
        Assert.AreEqual(" Next.", plan.Segments[1].Source);
    }

    [TestMethod]
    public void ProtectedDotsRemainValidInsideOneSentence()
    {
        const string source = "Dr. Smith used 3.14 at example.com.";
        var builder = new JsonLinesSubtitlePlanBuilder(source);

        Assert.IsTrue(builder.TryAdd(
            Parse("""
                {"seq":0,"source":"Dr. Smith used 3.14 at example.com.","translation":"valid","final":true}
                """),
            out _));
        Assert.IsTrue(builder.TryComplete(out _));
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("{\"seq\":\"0\",\"source\":\"text\",\"translation\":\"value\",\"final\":true}")]
    [DataRow("{\"seq\":0,\"source\":null,\"translation\":\"value\",\"final\":true}")]
    [DataRow("{\"seq\":0,\"source\":\"text\",\"translation\":null,\"final\":true}")]
    [DataRow("{\"seq\":0,\"source\":\"text\",\"translation\":\"value\",\"final\":1}")]
    [DataRow("{\"Seq\":0,\"source\":\"text\",\"translation\":\"value\",\"final\":true}")]
    [DataRow("{\"seq\":0,\"source\":\"text\",\"translation\":\"value\",\"final\":true,\"note\":\"extra\"}")]
    [DataRow("{\"seq\":0,\"seq\":0,\"source\":\"text\",\"translation\":\"value\",\"final\":true}")]
    public void MalformedRecordInvalidatesPlan(string json)
    {
        var builder = new JsonLinesSubtitlePlanBuilder("text");

        Assert.IsFalse(builder.TryAdd(Parse(json), out _));
        Assert.IsFalse(builder.TryComplete(out _));
    }

    [TestMethod]
    [DataRow("{\"seq\":1,\"source\":\"Exact\",\"translation\":\"value\",\"final\":true}")]
    [DataRow("{\"seq\":0,\"source\":\"exact\",\"translation\":\"value\",\"final\":true}")]
    [DataRow("{\"seq\":0,\"source\":\"Exact!\",\"translation\":\"value\",\"final\":true}")]
    [DataRow("{\"seq\":0,\"source\":\"\",\"translation\":\"value\",\"final\":true}")]
    [DataRow("{\"seq\":0,\"source\":\"Exact\",\"translation\":\" \\t\",\"final\":true}")]
    public void InvalidSequenceSourceOrTranslationPermanentlyInvalidatesPlan(string json)
    {
        var builder = new JsonLinesSubtitlePlanBuilder("Exact");

        Assert.IsFalse(builder.TryAdd(Parse(json), out _));
        Assert.IsFalse(builder.TryAdd(
            Parse("""
                {"seq":0,"source":"Exact","translation":"valid","final":true}
                """),
            out _));
        Assert.IsFalse(builder.TryComplete(out _));
    }

    [TestMethod]
    public void SourceMatchingIsOrdinalAcrossUnicodeCodeUnits()
    {
        const string snapshot = "Cafe\u0301 \ud83d\udc69\u200d\ud83d\udcbb";
        var builder = new JsonLinesSubtitlePlanBuilder(snapshot);

        Assert.IsTrue(builder.TryAdd(
            Parse("""
                {"seq":0,"source":"Cafe\u0301 ","translation":"part one","final":true}
                """),
            out _));
        Assert.IsTrue(builder.TryAdd(
            Parse("""
                {"seq":1,"source":"\ud83d\udc69\u200d\ud83d\udcbb","translation":"part two","final":true}
                """),
            out _));
        Assert.IsTrue(builder.TryComplete(out var plan));
        Assert.AreEqual(snapshot, string.Concat(plan.Segments.Select(segment => segment.Source)));
    }

    [TestMethod]
    public void CompleteFailsWithoutRecordsOrWithIncompleteCoverage()
    {
        var empty = new JsonLinesSubtitlePlanBuilder("source");
        var incomplete = new JsonLinesSubtitlePlanBuilder("source text");
        Assert.IsTrue(incomplete.TryAdd(
            Parse("""
                {"seq":0,"source":"source","translation":"value","final":true}
                """),
            out _));

        Assert.IsFalse(empty.TryComplete(out _));
        Assert.IsFalse(incomplete.TryComplete(out _));
    }

    [TestMethod]
    public void SuccessfulCompletionIsIdempotentAndRejectsFurtherRecords()
    {
        var builder = new JsonLinesSubtitlePlanBuilder("source");
        Assert.IsTrue(builder.TryAdd(
            Parse("""
                {"seq":0,"source":"source","translation":"value","final":true}
                """),
            out _));
        Assert.IsTrue(builder.TryComplete(out var first));

        Assert.IsFalse(builder.TryAdd(
            Parse("""
                {"seq":1,"source":"","translation":"later","final":true}
                """),
            out _));
        Assert.IsTrue(builder.TryComplete(out var second));
        Assert.AreSame(first, second);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
