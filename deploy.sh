#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLUGINS_DIR="/mnt/c/Program Files (x86)/steam/steamapps/common/OPERATOR/BepInEx/plugins"

echo "Building solution..."
dotnet build "$SCRIPT_DIR/OPERATOR_Mods.slnx" --configuration Release

echo "Deploying to $PLUGINS_DIR..."

PROJECTS=(
    "OPERATOR.Binoculars"
    "OPERATOR.Common"
    "OPERATOR.Debug"
    "OPERATOR.MagCheck"
    "OPERATOR.PlayerLoadouts"
    "OPERATOR.UsableVlite"
)

for PROJECT in "${PROJECTS[@]}"; do
    DLL="$SCRIPT_DIR/$PROJECT/bin/Release/net6.0/$PROJECT.dll"
    if [[ -f "$DLL" ]]; then
        cp "$DLL" "$PLUGINS_DIR/"
        echo "  Deployed $PROJECT.dll"
    else
        echo "  WARNING: $DLL not found, skipping"
    fi
done

# Third-party NuGet deps used by OPERATOR.Common.Networking (MessagePack + transitive).
# Emitted into OPERATOR.Common/bin via CopyLocalLockFileAssemblies; not present in BepInEx/core
# or the dotnet/ runtime, so they must be deployed alongside the plugins.
# MessagePack 2.5.x (the .NET 6-era line) uses the in-box System.Collections.Immutable, so only
# these need shipping. (Do NOT use MessagePack 3.x here: it needs Immutable 8.0.0, which the game's
# .NET 6 runtime can't load — DynamicUnionResolver throws a FileLoadException at static init.)
DEPS=(
    "MessagePack.dll"
    "MessagePack.Annotations.dll"
    "Microsoft.NET.StringTools.dll"
)

for DEP in "${DEPS[@]}"; do
    SRC="$SCRIPT_DIR/OPERATOR.Common/bin/Release/net6.0/$DEP"
    if [[ -f "$SRC" ]]; then
        cp "$SRC" "$PLUGINS_DIR/"
        echo "  Deployed $DEP"
    else
        echo "  WARNING: $SRC not found, skipping"
    fi
done

echo "Done."
