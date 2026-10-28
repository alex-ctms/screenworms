# ScreenWorm

Native Windows screensaver (C# + OpenGL via P/Invoke). Sources in `native/`, nothing else to install.

## Build and test

- Build from WSL: `native/build.sh` (stages sources under the Windows temp folder, runs `build.cmd` with the
  in-box `csc.exe`, copies `screenworm.exe` (GUI), `screenworm-cli.exe` (console) and `screenworm.scr` to `native/bin/` and `%LOCALAPPDATA%\ScreenWorm\bin\`).
- C# 5 only (the in-box compiler): no string interpolation, `?.`, `nameof`, expression-bodied members, tuples, `out var`.
- Visual check without touching the user's monitors: `native/bin/screenworm-cli.exe --screenshot out.png --demo --seconds 15`
  (run via interop; write the PNG to a Windows path, then view it). `--live` uses real stats instead of the demo workload.
- Live run: `screenworm.exe --window` (one resizable window) or `--run` (all monitors; Esc quits). `--seconds N` auto-quits.
- Logs: `%LOCALAPPDATA%\ScreenWorm\screenworm.log`.

## Conventions

- World space is physical pixels of the virtual desktop (y down); each window renders its monitor's rectangle.
- One GL context drives every window from a single render thread; the sim runs on that thread at a fixed 120 Hz.
- Stats are sampled once a second on a background thread and handed over as an immutable `StatSample`.
