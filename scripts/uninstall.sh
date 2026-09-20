#!/usr/bin/env bash
# Removes the plugin folder from the phone and restarts Jellyfin (after asking).
#   SSH_HOST=<ip> SSH_USER=<user> ./scripts/uninstall.sh
set -euo pipefail

PLUGIN_FOLDER_GLOB='Mako_*'

HOST="${SSH_HOST:?set SSH_HOST (see oneplus-ssh.personal.md)}"
USER_="${SSH_USER:?set SSH_USER (see oneplus-ssh.personal.md)}"
PORT="${SSH_PORT:-8022}"
KEY="${SSH_KEY:-$HOME/.ssh/oneplus_media}"
SSH=(ssh -i "$KEY" -p "$PORT" -o ConnectTimeout=10 "$USER_@$HOST")

echo "Plugin folders on the phone:"
"${SSH[@]}" "ls -d \$HOME/config/jellyfin/plugins/$PLUGIN_FOLDER_GLOB 2>/dev/null || echo '(none)'"

read -r -p "Delete them and restart Jellyfin? [y/N] " answer
[[ "$answer" =~ ^[Yy]$ ]] || { echo "Nothing changed."; exit 0; }

"${SSH[@]}" "rm -rf \$HOME/config/jellyfin/plugins/$PLUGIN_FOLDER_GLOB && \$HOME/stack/mediactl restart jellyfin"
echo "Removed. The plugin's settings file under plugins/configurations/ is left in place."
