# Newspaper UI Setup Guide

## Overview
The newspaper UI has been enhanced to support scrollable news lists with title+body display. This document explains how to configure the NewspaperPanel prefab in Unity Editor.

## Current Implementation Status

### Code Changes (Complete)
- ✅ `NewsItemView.cs`: Component for individual news items (title + body)
- ✅ `NewsItem.prefab`: Prefab for news items with styled title/body text
- ✅ `NewspaperPanelView.cs`: Updated to support both legacy and scrollable modes

### UI Prefab Configuration (Manual Step Required)

The code supports **two rendering modes**:

#### Mode 1: Legacy Slots (Current Default)
- Uses existing 3 fixed slots per media page
- Each slot shows title + body from `NewsInstance`
- **No Unity Editor changes required** - works with current prefab
- Set `useLegacySlots = true` in NewspaperPanelView inspector

#### Mode 2: Scrollable List (Recommended)
- Replaces fixed slots with a scrollable container
- Dynamically instantiates `NewsItem` prefabs
- Supports "Show More" button to expand from 3 to 30 items
- **Requires Unity Editor prefab modifications** (see below)

## How to Enable Scrollable Mode

### Step 1: Open NewspaperPanel Prefab
1. In Unity Editor, navigate to `Assets/Prefabs/UI/NewspaperPanel.prefab`
2. Open for editing

### Step 2: Add ScrollView Container (Per Media Page)

For each Paper page (Paper1, Paper2, Paper3):

1. **Add ScrollView**
   - Right-click on `Window/PaperPages/Paper1` → UI → Scroll View
   - Rename to `NewsScrollView`
   - Configure RectTransform:
     - Anchor: Stretch (all sides)
     - Left: 20, Right: 20, Top: 80, Bottom: 80
   
2. **Configure ScrollView Component**
   - Horizontal: Unchecked
   - Vertical: Checked
   - Movement Type: Clamped
   - Scroll Sensitivity: 20

3. **Configure Content Container**
   - Find `NewsScrollView/Viewport/Content`
   - Add `VerticalLayoutGroup` component:
     - Padding: Left 10, Right 10, Top 10, Bottom 10
     - Spacing: 10
     - Child Force Expand Width: Checked
     - Child Force Expand Height: Unchecked
   - Add `ContentSizeFitter` component:
     - Vertical Fit: Preferred Size

4. **Hide Old Slots** (Optional)
   - Disable GameObjects: `Slot_Headline`, `Slot_BlockA`, `Slot_BlockB`
   - Or delete them if you're confident in the new system

### Step 3: Add Show More Button

At the bottom of each Paper page:

1. **Create Button**
   - Right-click on `Window/PaperPages/Paper1` → UI → Button
   - Rename to `ShowMoreButton`
   - Position at bottom of page (Anchor: Bottom, Height: 40)

2. **Add Text**
   - Child TextMeshPro component should display "显示全部"
   - Rename text GameObject to `ShowMoreButtonText`

### Step 4: Wire References in NewspaperPanelView

1. Select the `NewspaperPanel` prefab root
2. Find `NewspaperPanelView` component in Inspector
3. Set the following references:
   - **News Item Prefab**: Drag `Assets/Prefabs/UI/NewsItem.prefab`
   - **News Content Root**: Drag `NewsScrollView/Viewport/Content` from hierarchy
   - **Show More Button**: Drag `ShowMoreButton` from hierarchy
   - **Show More Button Text**: Drag `ShowMoreButton/Text` from hierarchy
4. **Uncheck** `Use Legacy Slots`

### Step 5: Repeat for All Media Pages

Repeat Steps 2-4 for:
- `Paper2` (SENSATIONAL media)
- `Paper3` (INVESTIGATIVE media)

**OR** use a single shared ScrollView if you want all pages to share the same container (simpler but less flexible for future per-media customization).

## Alternative: Simplified Single-Container Approach

If you want to avoid per-page ScrollViews:

1. Create a single `NewsScrollView` at `Window/NewsScrollView` level
2. Show/hide it based on which tab is active
3. Only need to wire one set of references
4. Saves prefab complexity but less flexible for future customization

## Features Enabled by Scrollable Mode

1. **Title + Body Separation**: Each news item clearly shows title (bold, larger) and body (regular, multi-line)
2. **Show More**: Default 3 items, expandable to 30 items
3. **Proper Sorting**: News sorted by day desc, severity desc
4. **Empty State**: Shows "暂无报道" placeholder when no news
5. **Performance**: Limits rendering to max 30 items to prevent lag
6. **Debug Logging**: Proper logging format: `[NewsUI] day=X media=Y total=Z show=W mode=M`

## Testing

After configuration:

1. Start the game
2. Open newspaper panel (基金会晨报)
3. Switch between media tabs (Paper1/2/3)
4. Verify:
   - Each news shows title + body
   - Only 3 items shown by default
   - "Show More" button appears if more than 3 items
   - Clicking "Show More" expands list
   - Tab switching filters correctly by media
   - Empty state shows "暂无报道"

## Troubleshooting

### Issue: News items not appearing
- **Check**: NewsItemPrefab is assigned in Inspector
- **Check**: NewsContentRoot reference is correct
- **Check**: `useLegacySlots` is unchecked

### Issue: Show More button not working
- **Check**: ShowMoreButton and ShowMoreButtonText are assigned
- **Check**: Button has onClick listener (should be auto-wired)

### Issue: News showing on wrong tab
- **Check**: Each Paper page has its own ScrollView/Content reference
- **Check**: NewspaperPanelSwitcher is calling `Render(mediaProfileId)` correctly

## Legacy Mode Behavior

If `useLegacySlots = true` (default):
- Uses existing 3-slot layout
- Each slot now properly shows title + body from `NewsInstance.Title` and `NewsInstance.Description`
- No prefab changes required
- Works immediately with current setup

## Migration Path

For gradual migration:
1. ✅ Keep `useLegacySlots = true` initially (current state)
2. Test that title+body display works in legacy mode
3. Create ScrollView in Unity Editor when ready
4. Switch `useLegacySlots = false`
5. Test scrollable mode
6. Remove old slot GameObjects once confident

## Code Integration Points

The code is designed to be defensive:
- If scrollable components aren't wired, falls back to legacy mode automatically
- If NewsItemPrefab is missing, uses legacy slots
- If no news content, shows appropriate placeholder

This allows incremental prefab updates without breaking existing functionality.
