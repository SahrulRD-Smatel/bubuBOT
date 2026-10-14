#!/bin/bash
# render-outfits.sh — compile and run the Mochi outfit planche renderer
# NOT in CI. Run manually: bash scripts/render-outfits.sh
# Output: /tmp/bubu-outfits.png

set -e
cd "$(dirname "$0")/.."

SDK=$(xcrun --sdk macosx --show-sdk-path)

echo "Compiling renderer..."
swiftc \
  -parse-as-library \
  -sdk "$SDK" \
  -target arm64-apple-macosx15.0 \
  NotchBuddy/Sources/BubuKit/IslandScreenGeometry.swift \
  NotchBuddy/Sources/BubuKit/IslandTypes.swift \
  NotchBuddy/Sources/BubuKit/MochiWardrobe.swift \
  NotchBuddy/Sources/BubuKit/BotEngine.swift \
  NotchBuddy/Sources/BubuKit/MochiOutfitDrawing.swift \
  scripts/RenderOutfits.swift \
  -framework AppKit \
  -framework SwiftUI \
  -o /tmp/bubu-render-outfits \
  2>&1

echo "Running renderer..."
/tmp/bubu-render-outfits
echo "Opening..."
open /tmp/bubu-outfits.png
