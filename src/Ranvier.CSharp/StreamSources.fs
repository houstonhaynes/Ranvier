namespace Ranvier.CSharp

open System
open System.Threading
open System.Threading.Tasks
open Ranvier

/// <summary>Stream sources for C#.</summary>
[<AbstractClass; Sealed>]
type StreamSources =
    /// <summary>A source whose every start calls <c>run</c>.</summary>
    static member Create<'T>(run: Func<StreamSink<'T>, CancellationToken, Task>) : StreamSource<'T> =
        StreamSource.create (fun sink token -> run.Invoke (sink, token))

    /// <summary>A source that subscribes to <c>items</c> on each start.</summary>
    static member FromObservable<'T>(items: IObservable<'T>) : StreamSource<'T> =
        StreamSource.ofObservable items

    /// <summary>A source that enumerates <c>items</c> with the stream's token on each start.</summary>
    static member FromAsyncEnumerable<'T>(items: Collections.Generic.IAsyncEnumerable<'T>) : StreamSource<'T> =
        StreamSource.ofAsyncEnumerable items

#if NET8_0_OR_GREATER
    /// <summary>A source that reads <c>reader</c> on each start, delivering every buffered item before waiting again.</summary>
    static member FromChannel<'T>(reader: Channels.ChannelReader<'T>) : StreamSource<'T> =
        StreamSource.ofChannel reader
#endif
