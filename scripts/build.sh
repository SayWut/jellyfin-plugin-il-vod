#!/usr/bin/env bash
# Builds the plugin and assembles the folder Jellyfin loads.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLUGIN_NAME="IL-VOD"
PLUGIN_FOLDER_NAME="IlVod"
PLUGIN_VERSION="0.1.0.0"
PLUGIN_GUID="b8239c51-1963-42d7-82ee-d98ce3e75db4"
TARGET_ABI="12.1.0.0"
PUBLISH_DIR="$PROJECT_ROOT/artifacts/publish"
PLUGIN_DIR="$PROJECT_ROOT/artifacts/${PLUGIN_FOLDER_NAME}_${PLUGIN_VERSION}"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"

"$DOTNET" --list-sdks | grep -q '^10\.' || { echo "The .NET 10 SDK is required (found: $("$DOTNET" --list-sdks | tr '\n' ' '))." >&2; exit 1; }

rm -rf "$PUBLISH_DIR" "$PLUGIN_DIR"
"$DOTNET" publish "$PROJECT_ROOT/src/Jellyfin.Plugin.IlVod" -c Release -o "$PUBLISH_DIR" --nologo

mkdir -p "$PLUGIN_DIR"
cp "$PUBLISH_DIR/Jellyfin.Plugin.IlVod.dll" "$PLUGIN_DIR/"

cat > "$PLUGIN_DIR/meta.json" <<EOF
{
  "category": "Channels",
  "changelog": "IL-VOD: Israeli VOD providers in one channel (mako)",
  "description": "Shows Israeli VOD providers in one Jellyfin channel, fetching only what a user opens or plays.",
  "guid": "$PLUGIN_GUID",
  "name": "$PLUGIN_NAME",
  "overview": "Israeli VOD providers as one Jellyfin channel",
  "owner": "ofek",
  "targetAbi": "$TARGET_ABI",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "version": "$PLUGIN_VERSION",
  "status": "Active",
  "autoUpdate": false,
  "assemblies": []
}
EOF

python3 -m json.tool "$PLUGIN_DIR/meta.json" >/dev/null || { echo "meta.json is not valid JSON." >&2; exit 1; }

echo "Built: $PLUGIN_DIR"
ls -la "$PLUGIN_DIR"
