/// <summary>
/// Cases the harness measures after every case of <c>Scenarios.all</c>. <c>Main</c> loads this module only then, so
/// the code loaded while <c>Scenarios.all</c> runs is the code of a harness without these cases.
/// </summary>
module FableCounters.Appended

open CounterBench
open FableCounters.Scenarios

let private workload (prepare: int -> Workloads.Workload) (n: int) : Prepared =
    let prepared = prepare n

    {
        Run = prepared.Run
        Teardown = prepared.Teardown
    }

/// <summary>The cases, with the operation counts of the .NET bench. <c>scale</c> multiplies each scenario's N.</summary>
let appended (scale: int) : Case list =
    [
        for name, unit, edit in
            [
                "keyed-remove", "alternately remove and restore the middle row", Workloads.removeMiddle
                "keyed-swap", "alternately swap and restore the second and second-to-last rows", Workloads.swapInner
                "keyed-insert", "alternately prepend a row and restore", Workloads.insertFirst
            ] do
            {
                Scenario = name
                Engine = "Ranvier (memo-free rows)"
                Unit = $"%s{unit} of a %d{Workloads.Rows}-row projection"
                Ops = 50 * scale
                Prepare = workload (OnceWorkloads.keyedOncePass edit)
            }
        {
            Scenario = Control.Scenario
            Engine = "Control"
            Unit = $"refill a %d{Control.Size}-entry closure table, sum it, and sort a copy of its array"
            Ops = 50 * scale
            Prepare = workload Control.prepare
        }
    ]
