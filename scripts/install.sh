#!/usr/bin/env bash
# Copies the built plugin to the phone, restarts Jellyfin (after asking), and reports the plugin's status.
#   SSH_HOST=<ip> SSH_USER=<user> ./scripts/install.sh
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLUGIN_FOLDER="Mako_0.1.0.0"
LOCAL_DIR="$PROJECT_ROOT/artifacts/$PLUGIN_FOLDER"
REMOTE_PLUGINS_RELATIVE='config/jellyfin/plugins'
REMOTE_PLUGINS_DIR='$HOME/'"$REMOTE_PLUGINS_RELATIVE"
JELLYFIN_URL='http://localhost:8096'
STARTUP_TIMEOUT_SECONDS=180

HOST="${SSH_HOST:?set SSH_HOST (see oneplus-ssh.personal.md)}"
USER_="${SSH_USER:?set SSH_USER (see oneplus-ssh.personal.md)}"
PORT="${SSH_PORT:-8022}"
KEY="${SSH_KEY:-$HOME/.ssh/oneplus_media}"
SSH=(ssh -n -i "$KEY" -p "$PORT" -o ConnectTimeout=10 "$USER_@$HOST")

[ -f "$LOCAL_DIR/Jellyfin.Plugin.Mako.dll" ] || { echo "Nothing to install: run scripts/build.sh first." >&2; exit 1; }

echo "Copying $PLUGIN_FOLDER to the phone..."
"${SSH[@]}" "mkdir -p $REMOTE_PLUGINS_DIR/$PLUGIN_FOLDER"
rsync -a --delete -e "ssh -i $KEY -p $PORT" "$LOCAL_DIR/" "$USER_@$HOST:$REMOTE_PLUGINS_RELATIVE/$PLUGIN_FOLDER/"

read -r -p "Restart Jellyfin now? Playback in progress will stop. [y/N] " answer
if [[ ! "$answer" =~ ^[Yy]$ ]]; then
    echo "Copied but not loaded. The plugin loads at the next Jellyfin restart."
    exit 0
fi

"${SSH[@]}" '$HOME/stack/mediactl restart jellyfin'

echo "Waiting for Jellyfin to answer..."
"${SSH[@]}" "
    deadline=\$(( \$(date +%s) + $STARTUP_TIMEOUT_SECONDS ))
    until curl -sf -m 5 $JELLYFIN_URL/System/Info/Public >/dev/null; do
        [ \$(date +%s) -lt \$deadline ] || { echo 'Jellyfin did not come back in time.' >&2; exit 1; }
        sleep 5
    done
"

echo "Plugin status:"
MAKO_STATUS_QUERY='.[] | select(.Name == "Mako") | "\(.Name) \(.Version): \(.Status)"'
if ! plugin_status=$("${SSH[@]}" "
    set -o pipefail
    . \"\$HOME/stack.env\"; . \"\$HOME/stack/lib/api.sh\"
    curl -sS -f -m 20 -H \"\$(jellyfin_auth \"\$JELLYFIN_KEY\")\" \"$JELLYFIN_URL/Plugins\" \
        | jq -r '$MAKO_STATUS_QUERY'
"); then
    echo "Could not read /Plugins from Jellyfin (is JELLYFIN_KEY valid?)." >&2
    exit 1
fi

if [ -z "$plugin_status" ]; then
    echo "Mako is not listed by /Plugins: the folder was not discovered. Check ~/config/jellyfin/log/ on the phone." >&2
    exit 1
fi

echo "$plugin_status"
