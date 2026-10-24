using Ranvier;
using Ranvier.CSharp;
using Xunit;
using static Ranvier.CSharp.Reactive;

namespace CSharpCallers;

public class TracingTests
{
    [Fact]
    public void NamedReturnsTheBodysValue()
    {
        using var active = new Graph().Activate();

        Assert.Equal(42, Tracing.Named("answer", () => 42));
    }

    [Fact]
    public void NamedRunsAnActionBody()
    {
        using var active = new Graph().Activate();
        var ran = false;

        Tracing.Named("body", () => { ran = true; });

        Assert.True(ran);
    }

#if RANVIER_TRACE
    [Fact]
    public void EventsSinceReadsAfterTheCursorAndCheckpointDropsThePrefix()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Signal(1);
        count.Value = 2;
        var events = Tracing.Events(graph);
        var last = events[^1].Seq;

        Assert.Equal(events.Skip(1).Select(e => e.Seq), Tracing.EventsSince(graph, 1).Select(e => e.Seq));
        Assert.Null(Tracing.CheckpointOf(graph).Name);

        Tracing.Checkpoint(graph, last, "cut");

        Assert.Equal("cut", Tracing.CheckpointOf(graph).Name);
        Assert.Equal(last, Tracing.CheckpointOf(graph).Snapshot.Seq);
        Assert.Empty(Tracing.EventsSince(graph, last));
        Assert.Throws<ArgumentOutOfRangeException>(() => Tracing.EventsSince(graph, 0));
    }

    [Fact]
    public void ExportStreamsTheDumpTextToAMemorySink()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Signal(1);
        var codecs = Tracing.Codecs();
        var sink = Tracing.MemorySink();
        var export = Tracing.Export(graph, sink, 64, TraceExportFull.Wait, TraceExportCheckpoint.Never, codecs);

        count.Value = 2;
        count.Value = 3;
        var expected = Tracing.DumpText(graph, codecs);
        export.Dispose();

        Assert.True(export.Completion.IsCompletedSuccessfully);
        Assert.Equal(expected, sink.Text);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Tracing.Export(graph, sink, 0, TraceExportFull.Drop, TraceExportCheckpoint.Enqueued, codecs));
    }

    [Fact]
    public void OriginNamesTheLabelAndTheCallersFile()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));

        var origin = Tracing.Origin(graph, count);

        Assert.Contains("count", origin);
        Assert.Contains("TracingTests.cs:", origin);
    }

    [Fact]
    public void OriginOfAFacadeNodeNamesTheCallersFile()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Signal(1);
        var doubled = Memo(() => count.Value * 2);

        Assert.Contains("TracingTests.cs:", Tracing.Origin(graph, doubled));
    }

    [Fact]
    public void LabelReplacesTheNodesPathSegment()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));

        Tracing.Label(graph, count, "renamed");

        Assert.Contains("renamed", Tracing.Origin(graph, count));
    }

    [Fact]
    public void WhyTracesARunBackToTheWrite()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));
        var doubled = Tracing.Named("doubled", () => Memo(() => count.Value * 2));
        _ = doubled.Value;

        count.Value = 2;
        _ = doubled.Value;

        var why = Tracing.Why(graph, doubled);
        Assert.Contains("count", why);
        Assert.Equal(why, Tracing.Why(graph, doubled, 2));
    }

    [Fact]
    public void WhyTracesAnEffectsRunBackToTheWrite()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));
        var log = Effect(() => _ = count.Value);
        Flush();

        count.Value = 2;
        Flush();

        Assert.Contains("count", Tracing.Why(graph, log));
        Assert.Contains("TracingTests.cs:", Tracing.Origin(graph, log));
    }

    [Fact]
    public void AnEffectOnIsQueriedByItsPath()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));
        Tracing.Named("log", () => EffectOn(() => count.Value, _ => { }));
        Flush();

        count.Value = 2;
        Flush();

        Assert.Contains("count", Tracing.Why(graph, "/log"));
        Assert.Equal(Tracing.Why(graph, "/log"), Tracing.Why(graph, "/log", 2));
        Assert.Contains("TracingTests.cs:", Tracing.Origin(graph, "/log"));
        Assert.Throws<ArgumentException>(() => Tracing.Why(graph, "/missing"));
    }

    [Fact]
    public void ResolveFindsANodeByItsPath()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));

        Assert.Equal(((INode)count).Id, Tracing.Resolve(graph, "/count"));
        Assert.Null(Tracing.Resolve(graph, "/missing"));
    }

    [Fact]
    public void DumpTextHoldsOneJsonObjectPerLine()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));
        count.Value = 2;

        var lines = Tracing.DumpText(graph).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.StartsWith("{", line));
        Assert.Empty(Tracing.Reconcile(graph));
    }

    private sealed record Point(int X, int Y);

    private static readonly TraceCodec PointCodec = Tracing.Codec(
        "point",
        2,
        value => value is Point p ? $"{p.X},{p.Y}" : null,
        data =>
        {
            var parts = data.Split(',');
            return new Point(int.Parse(parts[0]), int.Parse(parts[1]));
        });

    [Fact]
    public void DocumentsCarryTheSchemaKindAndReadPoint()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var codecs = Tracing.Codecs(PointCodec);
        var x = Tracing.Named("x", () => Signal(1));
        var m = Tracing.Named("m", () => Memo(() => new Point(x.Value, 0)));
        _ = m.Value;
        x.Value = 2;
        _ = m.Value;
        var seq = Tracing.LastSeq(graph);

        var documents = new[]
        {
            ("state", Tracing.StateDocument(graph, codecs)),
            ("runs", Tracing.RunsDocument(graph, codecs, m)),
            ("why", Tracing.WhyDocument(graph, codecs, m)),
            ("whyNot", Tracing.WhyNotDocument(graph, m)),
            ("waiting", Tracing.WaitingDocument(graph, m)),
            ("lookup", Tracing.LookupDocument(graph, m)),
        };

        foreach (var (kind, text) in documents)
        {
            using var json = System.Text.Json.JsonDocument.Parse(text);
            Assert.Equal(3, json.RootElement.GetProperty("schema").GetInt32());
            Assert.Equal(kind, json.RootElement.GetProperty("kind").GetString());
            Assert.Equal(seq, json.RootElement.GetProperty("seq").GetInt32());
        }

        Assert.Contains("{\"case\":\"Encoded\",\"codec\":\"point\",\"version\":2,\"data\":\"2,0\"}", documents[0].Item2);
        Assert.Equal(Tracing.WhyDocument(graph, codecs, m), Tracing.WhyDocument(graph, codecs, "/m"));
        Assert.Equal(Tracing.RunsDocument(graph, codecs, m), Tracing.RunsDocument(graph, codecs, "/m"));
        Assert.Equal(Tracing.LookupDocument(graph, m), Tracing.LookupDocument(graph, "/m"));
        Assert.Contains("\"case\":\"Live\"", Tracing.LookupDocument(graph, m));
        Assert.Contains("\"case\":\"Missing\"", Tracing.LookupDocument(graph, "/missing"));
    }

    [Fact]
    public void ACodecListEncodesClaimedValuesAndAStrictListRaisesForTheRest()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var point = Tracing.Named("point", () => Signal(new Point(0, 0)));
        var other = Tracing.Named("other", () => Signal(new object()));
        point.Value = new Point(3, 4);
        other.Value = new object();

        var state = Tracing.StateDocument(graph, Tracing.Codecs(PointCodec));

        Assert.Contains("\"data\":\"3,4\"", state);
        Assert.Contains("{\"case\":\"Lossy\"", state);
        Assert.Contains("\"form\":\"encoded\"", Tracing.DumpText(graph, Tracing.Codecs(PointCodec)));
        Assert.Throws<TraceCodecException>(() => Tracing.StateDocument(graph, Tracing.StrictCodecs(PointCodec)));
    }
#endif
}
