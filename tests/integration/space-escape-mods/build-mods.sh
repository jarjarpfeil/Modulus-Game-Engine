#!/bin/bash
# build-mods.sh — Build all SpaceEscape mods and assemble into ModReassemblyHost
set -e

ROOT="$(cd "$(dirname "$0")" && pwd)"
HOST_DIR="$ROOT/ModReassemblyHost"
MODS_DIR="$HOST_DIR/bin/Debug/mods"
SLN="$ROOT/../../../../build/Stride.sln"

echo "=== Building SpaceEscape Integration Test Mods ==="

# Clean up locked ssdeps files that cause build failures
find "$ROOT/../../../../sources" -name '*.ssdeps' -path '*/bin/*' -delete 2>/dev/null || true

# Build each mod
declare -A MOD_NAMES
MOD_NAMES[mod-character]="ModCharacter"
MOD_NAMES[mod-background]="ModBackground"
MOD_NAMES[mod-ui]="ModUI"
MOD_NAMES[mod-rendering]="ModRendering"
MOD_NAMES[mod-chaos]="ModChaos"

for mod in mod-character mod-background mod-ui mod-rendering mod-chaos; do
    proj="${MOD_NAMES[$mod]}"
    echo "--- Building $mod ($proj.csproj) ---"
    dotnet build "$ROOT/$mod/$proj.csproj" \
        -p:StrideNativeWindowsArm64Enabled=false \
        -c Debug
done

# mod-assets is data-only, no code to build
echo "--- Setting up mod-assets (data-only) ---"

# Create mods directory
mkdir -p "$MODS_DIR"

# Copy each mod's output to the host's mods directory
for mod in mod-character mod-background mod-ui mod-rendering mod-chaos; do
    MOD_SRC="$ROOT/$mod/bin/Debug"
    MOD_DST="$MODS_DIR/$mod"
    
    echo "--- Assembling $mod ---"
    rm -rf "$MOD_DST"
    mkdir -p "$MOD_DST"
    
    # Copy mod.json
    cp "$ROOT/$mod/mod.json" "$MOD_DST/"
    
    # Copy assemblies
    cp "$MOD_SRC"/*.dll "$MOD_DST/" 2>/dev/null || true
    cp "$MOD_SRC"/*.pdb "$MOD_DST/" 2>/dev/null || true
    
    # Copy shaders if present
    if [ -d "$ROOT/$mod/shaders" ]; then
        cp -r "$ROOT/$mod/shaders" "$MOD_DST/"
    fi
    
    echo "  -> $MOD_DST"
done

# Copy assets mod
echo "--- Assembling mod-assets ---"
ASSETS_DST="$MODS_DIR/mod-assets"
rm -rf "$ASSETS_DST"
mkdir -p "$ASSETS_DST"
cp "$ROOT/mod-assets/mod.json" "$ASSETS_DST/"

# Copy SpaceEscape assets
if [ -d "$ROOT/../samples/Games/SpaceEscape/Assets/Shared" ]; then
    cp -r "$ROOT/../samples/Games/SpaceEscape/Assets/Shared" "$ASSETS_DST/assets"
    echo "  -> Copied SpaceEscape assets"
fi

echo ""
echo "=== All mods assembled in $MODS_DIR ==="
echo ""
echo "Mod list:"
for dir in "$MODS_DIR"/*/; do
    if [ -f "$dir/mod.json" ]; then
        ID=$(grep '"id"' "$dir/mod.json" | head -1 | sed 's/.*"id": "\(.*\)".*/\1/')
        echo "  $ID -> $dir"
    fi
done
