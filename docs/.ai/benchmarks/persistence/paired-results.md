
## Allocated bytes per operation

| Case | base (median) | head (median) | head/base median | paired range |
|---|---:|---:|---:|---:|
| app-detail | Ranvier | 12952.0 | 12952.0 | 1.000 | 1.000..1.000 |
| app-table | Ranvier | 78476.0 | 78476.0 | 1.000 | 1.000..1.000 |
| async-recover | Ranvier | 180374.3 | 180374.3 | 1.000 | 1.000..1.000 |
| async-resolve | Ranvier | 68608.0 | 68608.0 | 1.000 | 1.000..1.000 |
| chain | FSharp.Data.Adaptive | 472.0 | 472.0 | 1.000 | 1.000..1.000 |
| chain | R3 | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| chain | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| chain-affinity | Ranvier (Guarded) | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| chain-affinity | Ranvier (Serialised) | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| chain-affinity | Ranvier (Unchecked) | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| control | Control | 1048.0 | 1048.0 | 1.000 | 1.000..1.000 |
| create | FSharp.Data.Adaptive | 2413717.0 | 2413717.0 | 1.000 | 1.000..1.000 |
| create | R3 | 584160.0 | 584160.0 | 1.000 | 1.000..1.000 |
| create | Ranvier | 577376.0 | 577376.0 | 1.000 | 1.000..1.000 |
| create-on | Ranvier | 489520.0 | 489520.0 | 1.000 | 1.000..1.000 |
| cutoff | FSharp.Data.Adaptive | 352.0 | 352.0 | 1.000 | 1.000..1.000 |
| cutoff | R3 | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| cutoff | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| derive-effect | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| derive-on | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| dispose | FSharp.Data.Adaptive | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| dispose | R3 | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| dispose | Ranvier | 56.0 | 56.0 | 1.000 | 1.000..1.000 |
| dispose-on | Ranvier | 56.0 | 56.0 | 1.000 | 1.000..1.000 |
| editable-edit | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| editable-upstream | Ranvier | 2400.0 | 2400.0 | 1.000 | 1.000..1.000 |
| fail-recompute | Ranvier (failing) | 1576.0 | 1576.0 | 1.000 | 1.000..1.000 |
| fail-recompute | Ranvier (succeeding) | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| flight | Ranvier (CancelPrevious) | 1720.0 | 1720.0 | 1.000 | 1.000..1.000 |
| flight | Ranvier (Drop) | 872.0 | 872.0 | 1.000 | 1.000..1.000 |
| flight | Ranvier (DropWhilePending) | 872.0 | 872.0 | 1.000 | 1.000..1.000 |
| flight | Ranvier (FinishCurrent) | 1744.0 | 1744.0 | 1.000 | 1.000..1.000 |
| flight | Ranvier (KeepLatest) | 1672.0 | 1672.0 | 1.000 | 1.000..1.000 |
| flight | Ranvier (Queue) | 3184.0 | 3184.0 | 1.000 | 1.000..1.000 |
| keyed-insert | Ranvier (fresh items) | 4432.0 | 4432.0 | 1.000 | 1.000..1.000 |
| keyed-insert | Ranvier (memo-free rows) | 4504.0 | 4504.0 | 1.000 | 1.000..1.000 |
| keyed-insert | Ranvier (stable items) | 4432.0 | 4432.0 | 1.000 | 1.000..1.000 |
| keyed-remove | Ranvier (fresh items) | 4428.0 | 4428.0 | 1.000 | 1.000..1.000 |
| keyed-remove | Ranvier (memo-free rows) | 4500.0 | 4500.0 | 1.000 | 1.000..1.000 |
| keyed-remove | Ranvier (stable items) | 4428.0 | 4428.0 | 1.000 | 1.000..1.000 |
| keyed-swap | Ranvier (fresh items) | 4136.0 | 4136.0 | 1.000 | 1.000..1.000 |
| keyed-swap | Ranvier (memo-free rows) | 4160.0 | 4160.0 | 1.000 | 1.000..1.000 |
| keyed-swap | Ranvier (stable items) | 4136.0 | 4136.0 | 1.000 | 1.000..1.000 |
| mvu-dispatch | Ranvier (Mvu) | 400.0 | 400.0 | 1.000 | 1.000..1.000 |
| mvu-dispatch | Ranvier (signal per field) | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| persist-write | Ranvier (persisted signal) | (absent) | 800.0 | added | |
| persist-write | Ranvier (signal) | (absent) | 0.0 | added | |
| project-chain | Ranvier | 2528.0 | 2528.0 | 1.000 | 1.000..1.000 |
| project-churn | Ranvier (key reader) | 5016.0 | 5016.0 | 1.000 | 1.000..1.000 |
| project-churn | Ranvier (no reader) | 4616.0 | 4616.0 | 1.000 | 1.000..1.000 |
| project-edit | Ranvier | 112.0 | 112.0 | 1.000 | 1.000..1.000 |
| project-reorder | Ranvier | 4136.0 | 4136.0 | 1.000 | 1.000..1.000 |
| server-render | Ranvier | 199168.0 | 199168.0 | 1.000 | 1.000..1.000 |
| shape-diamond | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| shape-dynamic | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| stream-burst-1 | Ranvier (AsyncSource, batched) | 160.0 | 160.0 | 1.000 | 1.000..1.000 |
| stream-burst-1 | Ranvier (AsyncSource, unbatched) | 32.0 | 32.0 | 1.000 | 1.000..1.000 |
| stream-burst-1 | Ranvier (stream, batched) | 120.0 | 120.0 | 1.000 | 1.000..1.000 |
| stream-burst-16 | Ranvier (AsyncSource, batched) | 640.0 | 640.0 | 1.000 | 1.000..1.000 |
| stream-burst-16 | Ranvier (AsyncSource, unbatched) | 512.0 | 512.0 | 1.000 | 1.000..1.000 |
| stream-burst-16 | Ranvier (stream, batched) | 120.0 | 120.0 | 1.000 | 1.000..1.000 |
| stream-burst-256 | Ranvier (AsyncSource, batched) | 8320.0 | 8320.0 | 1.000 | 1.000..1.000 |
| stream-burst-256 | Ranvier (AsyncSource, unbatched) | 8192.0 | 8192.0 | 1.000 | 1.000..1.000 |
| stream-burst-256 | Ranvier (stream, batched) | 120.0 | 120.0 | 1.000 | 1.000..1.000 |
| stream-restart | Ranvier (async memo, CancelPrevious) | 760.0 | 760.0 | 1.000 | 1.000..1.000 |
| stream-restart | Ranvier (stream) | 608.0 | 608.0 | 1.000 | 1.000..1.000 |
| timed-admit-both | Ranvier | 1536.0 | 1536.0 | 1.000 | 1.000..1.000 |
| timed-admit-debounce | Ranvier | 1536.0 | 1536.0 | 1.000 | 1.000..1.000 |
| timed-admit-first | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| timed-admit-last | Ranvier | 1536.0 | 1536.0 | 1.000 | 1.000..1.000 |
| timed-capture-both | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| timed-capture-debounce | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| timed-capture-first | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| timed-capture-last | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| update | FSharp.Data.Adaptive | 63200.0 | 63200.0 | 1.000 | 1.000..1.000 |
| update | R3 | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| update | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| update-on | Ranvier | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| work-form | Ranvier (createEffect) | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| work-form | Ranvier (createEffectOn) | 0.0 | 0.0 | 1.000 | 1.000..1.000 |
| work-status | Ranvier (createEffect) | 3980.0 | 3980.0 | 1.000 | 1.000..1.000 |
| work-status | Ranvier (createEffectOn) | 398.4 | 398.4 | 1.000 | 1.000..1.000 |
| work-value | Ranvier (createEffect) | 6467.2 | 6467.2 | 1.000 | 1.000..1.000 |
| work-value | Ranvier (createEffectOn) | 6467.2 | 6467.2 | 1.000 | 1.000..1.000 |

## Stopwatch ns per operation

| Case | base (median) | head (median) | head/base median | paired range |
|---|---:|---:|---:|---:|
| chain | FSharp.Data.Adaptive | 484.2 | 421.8 | 0.871 | 0.556..1.077 |
| chain | R3 | 18.1 | 18.2 | 1.011 | 0.978..1.228 |
| chain | Ranvier | 111.6 | 107.9 | 0.975 | 0.715..1.805 |
| create | FSharp.Data.Adaptive | 3519187.5 | 3363912.5 | 0.953 | 0.592..1.389 |
| create | R3 | 108450.0 | 103937.5 | 0.984 | 0.835..1.827 |
| create | Ranvier | 122637.5 | 94200.0 | 0.786 | 0.350..1.102 |
| cutoff | FSharp.Data.Adaptive | 31.1 | 27.3 | 0.904 | 0.389..1.892 |
| cutoff | R3 | 3.0 | 2.9 | 0.967 | 0.242..1.036 |
| cutoff | Ranvier | 2.4 | 2.4 | 1.000 | 0.518..1.042 |
| derive-on | Ranvier | 22.2 | 23.7 | 1.082 | 0.215..2.659 |
| mvu-dispatch | Ranvier (Mvu) | 2336.8 | 2440.2 | 1.069 | 0.975..1.787 |
| mvu-dispatch | Ranvier (signal per field) | 26.4 | 27.2 | 0.985 | 0.812..1.038 |
| persist-write | Ranvier (persisted signal) | (absent) | 517.9 | added | |
| persist-write | Ranvier (signal) | (absent) | 43.8 | added | |
| project-edit | Ranvier | 2558.0 | 2648.0 | 1.024 | 0.780..1.430 |
| update | FSharp.Data.Adaptive | 71966.0 | 69778.0 | 1.000 | 0.671..1.332 |
| update | R3 | 1556.0 | 1558.0 | 1.003 | 0.907..2.877 |
| update | Ranvier | 3012.0 | 3112.0 | 1.039 | 0.761..1.077 |
| update-on | Ranvier | 3428.0 | 3538.0 | 1.046 | 0.410..55.344 |
