#!/bin/bash
# Unity batch mode setup script for SimpleWorldMap
# This script runs Unity Editor in batch mode to automatically setup the map system
# Usage: ./setup_simple_map.sh

set -e

echo "=========================================="
echo "SimpleWorldMap Automated Setup Script"
echo "=========================================="

# Check if Unity is available
UNITY_PATH=""
if [ -f "/Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity" ]; then
    UNITY_PATH=$(ls -t /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity | head -n 1)
elif command -v unity-editor &> /dev/null; then
    UNITY_PATH=$(which unity-editor)
elif [ -f "/opt/unity/Editor/Unity" ]; then
    UNITY_PATH="/opt/unity/Editor/Unity"
fi

if [ -z "$UNITY_PATH" ]; then
    echo "❌ Error: Unity Editor not found!"
    echo ""
    echo "Please install Unity Editor or manually run:"
    echo "  1. Open project in Unity Editor"
    echo "  2. Go to Tools > SCP > Setup Simple Map (Full)"
    echo ""
    echo "Or see FIX_OLD_MAP_DISPLAY_ISSUE.md for manual setup steps"
    exit 1
fi

echo "✓ Unity found: $UNITY_PATH"
echo ""

PROJECT_PATH=$(pwd)

echo "Running Unity in batch mode to setup SimpleWorldMap..."
echo "Project: $PROJECT_PATH"
echo ""

# Run Unity in batch mode to execute the setup
"$UNITY_PATH" \
    -quit \
    -batchmode \
    -projectPath "$PROJECT_PATH" \
    -executeMethod Editor.MapSetupAutomation.FullSetup \
    -logFile setup_map.log

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ SimpleWorldMap setup completed successfully!"
    echo ""
    echo "Generated files:"
    echo "  - Assets/Prefabs/UI/Map/*.prefab (6 prefabs)"
    echo "  - Updated Assets/Scenes/Main.unity"
    echo ""
    echo "Next steps:"
    echo "  1. Open project in Unity Editor to verify"
    echo "  2. Press Play to test the new map"
    echo ""
else
    echo ""
    echo "❌ Setup failed. Check setup_map.log for details"
    echo ""
    exit 1
fi

# Show log tail
if [ -f "setup_map.log" ]; then
    echo "Last 20 lines of log:"
    echo "----------------------------------------"
    tail -n 20 setup_map.log
    echo "----------------------------------------"
    echo ""
    echo "Full log: setup_map.log"
fi
