#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")"
if [ "$(uname -s)" != Linux ] || [ "$(uname -m)" != x86_64 ]; then
    echo 'Этот архив предназначен для Linux x86_64.' >&2
    exit 1
fi
bin="$HOME/.local/bin"
apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$bin" "$apps"
for name in music-rpc music-rpc-gui music-rpc-background; do
    install -m 755 "$name" "$bin/$name"
done
# Desktop Exec requires escaping backslashes, quotes, dollar signs and backticks.
python3 - "$bin/music-rpc-gui" "$apps/music-rpc.desktop" <<'PYDESKTOP'
import pathlib, sys
value = sys.argv[1].replace('\\', '\\\\').replace('"', '\\"').replace('`', '\\`').replace('$', '\\$').replace('%', '%%')
pathlib.Path(sys.argv[2]).write_text('[Desktop Entry]\nType=Application\nName=Music RPC\nComment=Музыка из AyuGram и Apple Music в Discord\nExec="' + value + '"\nIcon=audio-x-generic\nTerminal=false\nCategories=AudioVideo;Audio;\nStartupNotify=true\n')
PYDESKTOP
printf '%s\n' 'Установлено. Открой Music RPC в меню приложений или ~/.local/bin/music-rpc-gui'
