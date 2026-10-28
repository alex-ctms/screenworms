#!/usr/bin/env bash
# Build from WSL: stage sources in the Windows temp folder, compile through interop, copy bin/ back,
# and drop a copy in %LOCALAPPDATA%\ScreenWorm\bin so Windows can run it from a local path.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
win_lad="$(/mnt/c/Windows/System32/cmd.exe /c 'echo %LOCALAPPDATA%' 2>/dev/null | tr -d '\r')"
stage="$(wslpath -u "$win_lad")/Temp/screenworm-build/src"
rm -rf "$stage" && mkdir -p "$stage"
cp "$here"/*.cs "$here"/build.cmd "$here"/screenworm.ico "$stage/"
( cd "$stage" && /mnt/c/Windows/System32/cmd.exe /c build.cmd ) | tr -d '\r'
inst="$(wslpath -u "$win_lad")/ScreenWorm/bin"
mkdir -p "$here/bin" "$inst"
sleep 1
for f in screenworm.exe screenworm-cli.exe screenworm.scr; do
  if [ -f "$stage/bin/$f" ]; then cp "$stage/bin/$f" "$here/bin/" && cp "$stage/bin/$f" "$inst/"; else echo "warning: $f missing after build (antivirus?)"; fi
done
echo "built: $here/bin/  and  $win_lad\\ScreenWorm\\bin\\"
