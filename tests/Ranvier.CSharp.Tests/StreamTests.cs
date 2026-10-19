using System.Threading.Channels;
using Ranvier;
using Ranvier.CSharp;
using Xunit;

namespace CSharpCallers;

public class StreamTests
{
    [Fact]
    public void StreamFoldPublishesFoldedItems()
    {
        using var active = new Graph().Activate();
        StreamSink<int>? sink = null;
        var memo = Reactive.StreamFold(0, (int s, int x) => s + x,
            _ => StreamSources.Create<int>((s, _) => { sink = s; return new TaskCompletionSource().Task; }));

        Assert.True(memo.TryValue.IsPending);
        sink!.Next(2);
        sink.Next(3);
        Assert.Equal(5, memo.Value);
    }

    [Fact]
    public void StreamPublishesTheLatestItem()
    {
        using var active = new Graph().Activate();
        StreamSink<string>? sink = null;
        var memo = Reactive.Stream(
            _ => StreamSources.Create<string>((s, _) => { sink = s; return new TaskCompletionSource().Task; }));

        Assert.True(memo.TryValue.IsPending);
        sink!.Next("a");
        sink.Next("b");
        Assert.Equal("b", memo.Value);
    }

    [Fact]
    public void FromAsyncEnumerableDeliversItems()
    {
        var graph = new Graph(GraphOptions.Default.WithDispatcher(new ManualDispatcher()));
        using var active = graph.Activate();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource();
        var memo = Reactive.StreamFold(0, (int s, int x) => s + x,
            _ => StreamSources.FromAsyncEnumerable(Items(gate.Task, finished)));

        Assert.True(memo.TryValue.IsPending);
        gate.SetResult();
        Assert.True(finished.Task.Wait(TimeSpan.FromSeconds(10)), "the enumerator did not finish within 10 s");
        graph.Pump();
        Assert.Equal(6, memo.Value);
    }

    [Fact]
    public void FromObservableFoldsItemsAndKeepsTheStateOnCompletion()
    {
        var graph = new Graph(GraphOptions.Default.WithDispatcher(new ManualDispatcher()));
        using var active = graph.Activate();
        var subject = new Subject();
        var memo = Reactive.StreamFold(0, (int s, int x) => s + x, _ => StreamSources.FromObservable(subject));

        Assert.True(memo.TryValue.IsPending);
        subject.Observer!.OnNext(2);
        subject.Observer.OnNext(3);
        subject.Observer.OnCompleted();
        graph.Pump();
        Assert.Equal(5, memo.Value);
        memo.Dispose();
        Assert.Equal(1, subject.Unsubscribed);
    }

    [Fact]
    public void FromObservableFailsOnError()
    {
        var graph = new Graph(GraphOptions.Default.WithDispatcher(new ManualDispatcher()));
        using var active = graph.Activate();
        var subject = new Subject();
        var memo = Reactive.Stream(_ => StreamSources.FromObservable(subject));

        Assert.True(memo.TryValue.IsPending);
        subject.Observer!.OnError(new InvalidOperationException("rx"));
        graph.Pump();
        Assert.True(memo.TryValue.IsFailed);
        Assert.Equal("rx", Assert.Throws<InvalidOperationException>(() => memo.Value).Message);
    }

    [Fact]
    public void FromChannelFoldsBufferedAndLaterItems()
    {
        var graph = new Graph(GraphOptions.Default.WithDispatcher(new ManualDispatcher()));
        using var active = graph.Activate();
        var channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { AllowSynchronousContinuations = true });
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        var memo = Reactive.StreamFold(0, (int s, int x) => s + x, _ => StreamSources.FromChannel(channel.Reader));

        Assert.Equal(3, memo.Value);
        channel.Writer.TryWrite(4);
        channel.Writer.Complete();
        graph.Pump();
        Assert.Equal(7, memo.Value);
    }

    private sealed class Subject : IObservable<int>
    {
        public IObserver<int>? Observer { get; private set; }
        public int Unsubscribed { get; private set; }

        public IDisposable Subscribe(IObserver<int> observer)
        {
            Observer = observer;
            return new Unsubscriber(this);
        }

        private sealed class Unsubscriber(Subject subject) : IDisposable
        {
            public void Dispose() => subject.Unsubscribed++;
        }
    }

    private static async IAsyncEnumerable<int> Items(Task gate, TaskCompletionSource finished)
    {
        try
        {
            await gate;
            yield return 1;
            yield return 2;
            yield return 3;
        }
        finally
        {
            finished.TrySetResult();
        }
    }
}
