#!/usr/bin/env bash
# Removes the plugin folder from the phone and restarts Jellyfin (after asking).
#   SSH_HOST=<ip> SSH_USER=<user> ./scripts/uninstall.sh
set -euo pipefail

PLUGIN_FOLDER_GLOB='Mako_*'

HOST="${SSH_HOST:?set SSH_HOST (see oneplus-ssh.personal.md)}"
USER_="${SSH_USER:?set SSH_USER (see oneplus-ssh.personal.md)}"
PORT="${SSH_PORT:-8022}"
KEY="${SSH_KEY:-$HOME/.ssh/oneplus_media}"
SSH=(ssh -n -i "$KEY" -p "$PORT" -o ConnectTimeout=10 "$USER_@$HOST")

list_cmd=": \"\${HOME:?remote HOME is empty}\"; ls -d \$HOME/config/jellyfin/plugins/$PLUGIN_FOLDER_GLOB 2>/dev/null"
found="$("${SSH[@]}" "$list_cmd" || true)"

if [ -z "$found" ]; then
    echo "No Mako plugin folder on the phone. Nothing changed."
    exit 0
fi

echo "Plugin folders on the phone:"
echo "$found"

read -r -p "Delete them and restart Jellyfin? [y/N] " answer
[[ "$answer" =~ ^[Yy]$ ]] || { echo "Nothing changed."; exit 0; }

delete_cmd=": \"\${HOME:?remote HOME is empty}\"; rm -rf \$HOME/config/jellyfin/plugins/$PLUGIN_FOLDER_GLOB && \$HOME/stack/mediactl restart jellyfin"
"${SSH[@]}" "$delete_cmd"
echo "Removed. The plugin's settings file under plugins/configurations/ is left in place."
