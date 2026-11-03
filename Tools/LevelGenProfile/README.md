# Level gen profiling outside the Editor

```
dotnet build Tools/LevelGenProfile -c Release -t:Profile -p:ProfileArgs="--seeds 30 --report /tmp/report.md"
```

Generates seeds 0..n-1 on GuaranteeTests' five presets plus the values in `Assets/Level Gen/LevelGenConfig.asset`, on
the Editor's Mono with float32 maths off (as `Tools/LevelGenTests`; see its README for why), and reports per preset:
ms per level and per attempt (mean, median, p95, max), attempts and why they were retried, time per stage, the build
plan's time and primitive counts.

Stage times come from `StageProfiler` calls in the core. They compile away everywhere except here (this project
defines `LEVELGEN_PROFILE`); `-p:NoProfile=true` builds without them.

Options (pass in `ProfileArgs`; write a comma as `%2C` for MSBuild):
- `--seeds N`, `--presets 0%2C2%2C5` (0 default, 1 small, 2 dense, 3 tall, 4 small-full, 5 asset).
- `--hashes FILE` writes each level's description hash and build-plan hash; `--compare FILE` checks them against an
  earlier run, for a change that must keep levels byte-identical.
- `--overlaps` runs the overlap check (which the generator skips) on every generated level and lists overlaps by the
  kinds of the two primitives, with examples. `--dump PRESET SEED N [OWNER]` prints the first N overlaps of one level
  with both primitives' footprints.
