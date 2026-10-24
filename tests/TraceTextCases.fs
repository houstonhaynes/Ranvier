module Ranvier.TraceTextCases

#if RANVIER_TRACE
open Ranvier

/// <summary>Expected diagnostic text shared by the JIT, Fable and NativeAOT smoke suites.</summary>
let cases () : (string * string * obj) list =
    let snapshot = TraceModel.snapshot [||]

    [
        "user write", "UserWrite 1", box (WhyRoot.UserWrite 1)
        "created", "Created 2", box (WhyRoot.Created 2)
        "pulled", "Pulled 3", box (WhyRoot.Pulled 3)
        "checkpoint", "BeforeCheckpoint \"trace.jsonl\"", box (WhyRoot.BeforeCheckpoint "trace.jsonl")
        "null checkpoint", "BeforeCheckpoint null", box (WhyRoot.BeforeCheckpoint null)
        "unrecorded", "Unrecorded 4", box (WhyRoot.Unrecorded 4)
        "settled root", "Settled 5", box (WhyRoot.Settled 5)
        "disposed reason", "Disposed 1", box (WhyNotReason.Disposed 1)
        "queued", "Queued 2", box (WhyNotReason.Queued 2)
        "unobserved", "Unobserved 3", box (WhyNotReason.Unobserved 3)
        "checked clean", "CheckedClean (4, [5; 6])", box (WhyNotReason.CheckedClean (4, [ 5; 6 ]))
        "skipped reader", "SkippedAsRunningReader 7", box (WhyNotReason.SkippedAsRunningReader 7)
        "not reached", "NotReached 8", box (WhyNotReason.NotReached 8)
        "in flight", "InFlight", box TraceFlightState.InFlight
        "settled flight", "Settled (2, true)", box (TraceFlightState.Settled (2, true))
        "unheld flight", "Settled (2, false)", box (TraceFlightState.Settled (2, false))
        "failed flight", "Failed (3, false)", box (TraceFlightState.Failed (3, false))
        "cancelled flight", "Failed (3, true)", box (TraceFlightState.Failed (3, true))
        "dropped flight", "Dropped (4, Superseded)", box (TraceFlightState.Dropped (4, TraceDropReason.Superseded))
        "fresh node", "Fresh", box TraceNodeStatus.Fresh
        "running node", "Running", box TraceNodeStatus.Running
        "ended node", "Ended Pending", box (TraceNodeStatus.Ended RunStatus.Pending)
        "disposed node", "Disposed", box TraceNodeStatus.Disposed
        "event",
        "{ Seq = 1\n  Kind = Write\n  Node = 2\n  Other = 3\n  Arg = 4\n  Flag = 5\n  Cause = 6\n  Payload = \"tea\" }",
        box
            {
                Seq = 1
                Kind = TraceEventKind.Write
                Node = 2
                Other = 3
                Arg = 4
                Flag = 5
                Cause = 6
                Payload = "tea"
            }
        "null event payload",
        "{ Seq = 0\n  Kind = GraphNew\n  Node = 0\n  Other = 0\n  Arg = 0\n  Flag = 0\n  Cause = 0\n  Payload = null }",
        box
            {
                Seq = 0
                Kind = TraceEventKind.GraphNew
                Node = 0
                Other = 0
                Arg = 0
                Flag = 0
                Cause = 0
                Payload = null
            }
        "origin",
        "{ Seq = 1\n  Node = 2\n  Kind = Signal\n  Label = Some \"tea\"\n  Owner = 3\n  Owners = [3; 4]\n  Path = \"/tea\"\n  Run = 5\n  Site = null }",
        box
            {
                Seq = 1
                Node = 2
                Kind = TraceNodeKind.Signal
                Label = Some "tea"
                Owner = 3
                Owners = [ 3; 4 ]
                Path = "/tea"
                Run = 5
                Site = null
            }
        "step",
        "{ Seq = 1\n  Kind = Moved\n  Node = 2\n  Other = 3\n  Value = Some 4 }",
        box
            {
                Seq = 1
                Kind = TraceEventKind.Moved
                Node = 2
                Other = 3
                Value = Some (box 4)
            }
        "null step value",
        "{ Seq = 1\n  Kind = Moved\n  Node = 2\n  Other = 3\n  Value = Some null }",
        box
            {
                Seq = 1
                Kind = TraceEventKind.Moved
                Node = 2
                Other = 3
                Value = Some (null: obj)
            }
        "why",
        "{ Node = 1\n  Run = 2\n  Steps = []\n  Root = Some (UserWrite 3) }",
        box
            {
                Node = 1
                Run = 2
                Steps = []
                Root = Some (WhyRoot.UserWrite 3)
            }
        "run",
        "{ Run = 1\n  Start = 2\n  Status = Some Ok\n  Moved = true\n  Root = Created 3\n  Flush = 4\n  Value = None }",
        box<TraceRun>
            {
                Run = 1
                Start = 2
                Status = Some RunStatus.Ok
                Moved = true
                Root = WhyRoot.Created 3
                Flush = 4
                Value = None
            }
        "history", "{ Node = 1\n  Runs = [] }", box { Node = 1; Runs = [] }
        "flight",
        "{ Flight = 1\n  Start = 2\n  Run = 3\n  State = Dropped (4, Disposed) }",
        box
            {
                Flight = 1
                Start = 2
                Run = 3
                State = TraceFlightState.Dropped (4, TraceDropReason.Disposed)
            }
        "waiting",
        "{ Node = 1\n  Sources = [2; 3]\n  Flights = [] }",
        box
            {
                Node = 1
                Sources = [ 2; 3 ]
                Flights = []
            }
        "snapshot node",
        "{ Id = 1\n  Kind = Memo\n  Owner = 2\n  Path = \"/tea\"\n  Incarnation = 3\n  Label = None\n  Site = \"App.fs:4\"\n  Created = 5\n  Runs = 6\n  LastRun = 7\n  PendingMark = 8\n  Status = Ended Ok\n  Value = Some \"null\" }",
        box
            {
                Id = 1
                Kind = TraceNodeKind.Memo
                Owner = 2
                Path = "/tea"
                Incarnation = 3
                Label = None
                Site = "App.fs:4"
                Created = 5
                Runs = 6
                LastRun = 7
                PendingMark = 8
                Status = TraceNodeStatus.Ended RunStatus.Ok
                Value = Some "null"
            }
        "snapshot owner",
        "{ Id = 1\n  Parent = 2\n  Host = 3\n  Root = false\n  Path = \"/tea\"\n  Label = Some \"tea\"\n  Disposed = true }",
        box
            {
                Id = 1
                Parent = 2
                Host = 3
                Root = false
                Path = "/tea"
                Label = Some "tea"
                Disposed = true
            }
        "snapshot maps",
        "{ Seq = 0\n  Root = 0\n  Nodes = map []\n  Owners = map []\n  Sources = map [(1, [2; 3])]\n  Observers = map [(2, set [1; 4])]\n  Siblings = map [(\"/tea\", map [(\"row\", 2)])]\n  Incarnations = map [(\"/tea\", 3)] }",
        box
            { snapshot with
                Sources = Map.ofList [ 1, [ 2; 3 ] ]
                Observers = Map.ofList [ 2, Set.ofList [ 1; 4 ] ]
                Siblings = Map.ofList [ "/tea", Map.ofList [ "row", 2 ] ]
                Incarnations = Map.ofList [ "/tea", 3 ]
            }
        "dump",
        "{ Target = \"net\"\n  Graph = 1\n  Checkpoint = null\n  SeqFrom = 2\n  Snapshot = { Seq = 0\n    Root = 0\n    Nodes = map []\n    Owners = map []\n    Sources = map []\n    Observers = map []\n    Siblings = map []\n    Incarnations = map [] }\n  Events = [||] }",
        box
            {
                Target = "net"
                Schema = 1
                Graph = 1
                Checkpoint = null
                SeqFrom = 2
                Snapshot = snapshot
                Events = [||]
                Payloads = [||]
                Gaps = []
            }
        "timing",
        "{ Mode = \"debounce\"\n  CapturedStatus = "
        + string Status.Pending
        + "\n  PublishedStatus = "
        + string Status.None
        + "\n  WindowOpen = true\n  RemainingMilliseconds = 12\n  WinningCapture = 3 }",
        box
            {
                Mode = "debounce"
                CapturedStatus = Status.Pending
                PublishedStatus = Status.None
                WindowOpen = true
                RemainingMilliseconds = 12.
                WinningCapture = 3
            }
    ]
#endif
