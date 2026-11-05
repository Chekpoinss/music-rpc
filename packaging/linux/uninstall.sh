#!/usr/bin/env sh
set -eu
rm -f "$HOME/.local/bin/music-rpc" "$HOME/.local/bin/music-rpc-gui" "$HOME/.local/bin/music-rpc-background"
rm -f "${XDG_DATA_HOME:-$HOME/.local/share}/applications/music-rpc.desktop"
printf '%s\n' 'Программа удалена. Настройки сохранены в ${XDG_CONFIG_HOME:-~/.config}/music-rpc/.'
