# Addendum — the raw evidence path no longer exists

Dated 2026-09-06. The run report is left as written; this records what has
changed about the location it cites.

The header table gives Raw evidence as
`…\AppData\Local\Temp\claude\…\scratchpad\testrun-20260905-125626-b5441f1`.
That was a session scratch directory and it is gone. It was never committed:
it held absolute paths and full console output.

What survives of this run is what is beside this file — the summary, the case
results and the addendum on DEF-001.

The harnesses this run's successor used are now committed at
[`tools/verification/`](../../../../tools/verification/README.md), so its
figures are re-derivable against a fresh isolated server. This run predates
them, and several of its checks were performed by hand, so treat its numbers as
a record of what was observed on `b5441f1` rather than as something to re-run.

Later runs record a command and a fixture instead of a path, so this does not
recur.
