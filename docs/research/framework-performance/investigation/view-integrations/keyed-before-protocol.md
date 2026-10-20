# Keyed-rows baseline: timing protocol note

`keyed-before.json` is the keyed-rows baseline taken before the view-integrations work.

swap1k measured after a forced GC costs about 0.26 ms, spent in `RowsOf.Stage`, against about 0.07 ms in the steady loop. The difference is a property of the timing protocol (post-GC deoptimisation) and is not being optimised.
