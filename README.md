# ScreenWorm

A Windows screensaver in the spirit of the old NetWare console worms, rebuilt as a GPU toy.
One luminous worm per logical CPU core roams the whole multi-monitor desktop; its length,
speed and the pace of the light pulses running down its body follow that core's load.
Around it the rest of the machine is encoded in the scene:

- **SMT siblings** share a hue (a lighter tint for the second thread) and swim together.
- **Network** traffic streaks across the desktop as comets: inbound left to right, outbound right to left; rate sets how many.
- **Disk** reads ring out in cyan and writes in amber, sized by throughput.
- **Memory** pressure thickens and purples the nebula and dims the stars.
- **GPU** load swells the bloom and the chromatic fringe.
- **Threads** set how much plankton drifts in the background; busy cores shed sparks (a quarter million GPU particles).
- **Per-core frequency boost** brightens the worm.

Everything is one process: stats come straight from the kernel, rendering is OpenGL 3.3
(HDR float targets, transform-feedback particles, a six-level bloom pyramid, ACES tonemap).
No browser, no runtime to install: it compiles with the C# compiler that ships inside .NET Framework 4.x.

## Build

From Windows: `native\build.cmd`. From WSL: `native/build.sh` (compiles through interop and also
copies the result to `%LOCALAPPDATA%\ScreenWorm\bin`). Output: `native/bin/screenworm.exe` (GUI build, also copied to `screenworm.scr`) and
`native/bin/screenworm-cli.exe` (same program with a console, for the helper commands below).

## Use

```
screenworm.exe --run                full screen on every monitor; Esc, Q or a click quits
screenworm.exe --window             resizable window for a look
screenworm.exe /c                   settings: render scale, legend, particles, CRT look
screenworm-cli.exe --install        make it the active screensaver (timeout: Windows Screen Saver Settings)
screenworm-cli.exe --screenshot out.png --demo --seconds 15   render offscreen to a PNG
screenworm-cli.exe --stats          live kernel stats as JSON lines
```

Keys while running (not in screensaver mode): `h` cycles the legend (off, subtle, full), `c` CRT scanlines, `b` bloom.
Options: `--q 0.5..1` pins the render scale (otherwise it adapts to hold 60 fps), `--particles N`, `--hud 0|1|2`, `--demo` synthesises a workload.

Settings and logs live in `%LOCALAPPDATA%\ScreenWorm\`.

If a freshly built `screenworm.exe` or `.scr` disappears seconds after the build, your antivirus quarantined an
unsigned executable (one did here until the build carried version info and an icon); ask for an exclusion
rather than changing the code.

## Layout

- `native/Program.cs` modes, windows, screensaver lifetime, config dialog, screenshot mode
- `native/Renderer.cs` OpenGL pipeline; `native/Shaders.cs` GLSL
- `native/Sim.cs` worm colony, comets, rings, noise, demo workload
- `native/Stats.cs` kernel counters (NtQuerySystemInformation), memory, I/O, processes, PDH for CPU boost and GPU engines, monitors
- `native/Hud.cs` GDI+ legend rendered into a texture; `native/Gl.cs` WGL/OpenGL bindings
