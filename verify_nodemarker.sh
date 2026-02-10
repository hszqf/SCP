#!/bin/bash
# NodeMarkerView Implementation Verification Script
# Checks that all required files and changes are in place

echo "=== NodeMarkerView Implementation Verification ==="
echo ""

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

SUCCESS_COUNT=0
FAIL_COUNT=0

check_file() {
    local file=$1
    local desc=$2
    
    if [ -f "$file" ]; then
        echo -e "${GREEN}✓${NC} $desc: $file"
        ((SUCCESS_COUNT++))
        return 0
    else
        echo -e "${RED}✗${NC} $desc: $file (MISSING)"
        ((FAIL_COUNT++))
        return 1
    fi
}

check_content() {
    local file=$1
    local pattern=$2
    local desc=$3
    
    if [ ! -f "$file" ]; then
        echo -e "${RED}✗${NC} $desc: File not found"
        ((FAIL_COUNT++))
        return 1
    fi
    
    if grep -q "$pattern" "$file"; then
        echo -e "${GREEN}✓${NC} $desc"
        ((SUCCESS_COUNT++))
        return 0
    else
        echo -e "${RED}✗${NC} $desc (NOT FOUND)"
        ((FAIL_COUNT++))
        return 1
    fi
}

echo "Checking Core Files..."
check_file "Assets/Scripts/UI/Map/NodeMarkerView.cs" "NodeMarkerView script"
check_file "Assets/Scripts/Editor/NodeMarkerPrefabGenerator.cs" "Prefab generator"
check_file "Assets/Scripts/UI/Map/NewMapRuntime.cs" "NewMapRuntime"
check_file "Assets/Prefabs/UI/Map.meta" "Map prefabs folder meta"

echo ""
echo "Checking NodeMarkerView.cs implementation..."
check_content "Assets/Scripts/UI/Map/NodeMarkerView.cs" "public void Bind" "Bind method"
check_content "Assets/Scripts/UI/Map/NodeMarkerView.cs" "public void Refresh" "Refresh method"
check_content "Assets/Scripts/UI/Map/NodeMarkerView.cs" "GetRepresentativeTask" "Task selection logic"
check_content "Assets/Scripts/UI/Map/NodeMarkerView.cs" "HasUnknownAnomaly" "Unknown anomaly detection"
check_content "Assets/Scripts/UI/Map/NodeMarkerView.cs" "\[MapUI\]" "MapUI logging"

echo ""
echo "Checking NewMapRuntime.cs updates..."
check_content "Assets/Scripts/UI/Map/NewMapRuntime.cs" "NodeMarkerView nodeMarkerPrefab" "Prefab field"
check_content "Assets/Scripts/UI/Map/NewMapRuntime.cs" "Instantiate(nodeMarkerPrefab" "Prefab instantiation"
check_content "Assets/Scripts/UI/Map/NewMapRuntime.cs" "view.Bind" "View binding"
check_content "Assets/Scripts/UI/Map/NewMapRuntime.cs" "view.Refresh" "View refresh"
check_content "Assets/Scripts/UI/Map/NewMapRuntime.cs" "OnStateChanged" "State change subscription"
check_content "Assets/Scripts/UI/Map/NewMapRuntime.cs" "OnGameStateChanged" "State change handler"

echo ""
echo "Checking prefab generator..."
check_content "Assets/Scripts/Editor/NodeMarkerPrefabGenerator.cs" "MenuItem.*Generate NodeMarkerView Prefab" "Menu item"
check_content "Assets/Scripts/Editor/NodeMarkerPrefabGenerator.cs" "CreateDot" "Dot creation"
check_content "Assets/Scripts/Editor/NodeMarkerPrefabGenerator.cs" "CreateTaskBar" "Task bar creation"
check_content "Assets/Scripts/Editor/NodeMarkerPrefabGenerator.cs" "CreateEventBadge" "Event badge creation"
check_content "Assets/Scripts/Editor/NodeMarkerPrefabGenerator.cs" "CreateUnknownIcon" "Unknown icon creation"
check_content "Assets/Scripts/Editor/NodeMarkerPrefabGenerator.cs" "raycastTarget.*false" "Raycast settings"

echo ""
echo "Checking raycast target settings..."
check_content "Assets/Scripts/Editor/NodeMarkerPrefabGenerator.cs" "rootImage.raycastTarget = true" "Root button raycast enabled"

echo ""
echo "=== Verification Summary ==="
echo -e "${GREEN}Passed: $SUCCESS_COUNT${NC}"
echo -e "${RED}Failed: $FAIL_COUNT${NC}"
echo ""

if [ $FAIL_COUNT -eq 0 ]; then
    echo -e "${GREEN}✓ All checks passed!${NC}"
    echo ""
    echo "Next steps:"
    echo "1. Open Unity Editor"
    echo "2. Run: Tools > SCP > Generate NodeMarkerView Prefab"
    echo "3. Assign prefab to NewMapRuntime in MapBootstrap GameObject"
    echo "4. Test in Play mode"
    exit 0
else
    echo -e "${RED}✗ Some checks failed. Please review the output above.${NC}"
    exit 1
fi
