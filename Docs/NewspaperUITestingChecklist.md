# Newspaper UI Enhancement - Testing & Verification Checklist

## Overview
This checklist helps verify that the newspaper UI enhancement is working correctly. The implementation supports **two modes**: Legacy (active by default) and Scrollable (requires Unity Editor setup).

## Quick Start

### Current State (Legacy Mode - No Setup Required)
The code is ready to use **immediately** with the existing NewspaperPanel prefab:
- ✅ Uses existing 3 fixed slots per media page
- ✅ Shows title + body for each news item
- ✅ Filters correctly by media (FORMAL/SENSATIONAL/INVESTIGATIVE)
- ✅ Sorts by newest first
- ✅ Works without any Unity Editor changes

### Optional Enhancement (Scrollable Mode - Requires Unity Setup)
For the full scrollable list with show-more button:
- 📖 Follow instructions in `Docs/NewspaperUISetup.md`
- 🛠️ Requires Unity Editor prefab modifications
- ⚡ Enables expandable list (3 → 30 items)

---

## Testing Checklist

### 1. Basic Functionality (Legacy Mode)

#### Start Game
- [ ] Game starts without errors
- [ ] No console errors related to NewspaperPanelView
- [ ] Debug log shows: `[Newspaper] bind close ok`

#### Open Newspaper Panel
- [ ] Click newspaper icon in HUD
- [ ] Panel opens and displays correctly
- [ ] Title shows "基金会晨报"
- [ ] Day counter shows current day (e.g., "Day1", "Day5")

#### Tab Switching
- [ ] Click **Paper1** tab
  - [ ] Panel shows content
  - [ ] Console log: `[NewsUI] SwitchTab index=0 media=FORMAL`
- [ ] Click **Paper2** tab
  - [ ] Panel switches to new page
  - [ ] Console log: `[NewsUI] SwitchTab index=1 media=SENSATIONAL`
- [ ] Click **Paper3** tab
  - [ ] Panel switches to new page
  - [ ] Console log: `[NewsUI] SwitchTab index=2 media=INVESTIGATIVE`

#### Content Display (Paper1 - FORMAL)
- [ ] At least one news item visible (bootstrap news should appear on Day 1)
- [ ] **Headline Slot** shows:
  - [ ] Title text (bold/larger font)
  - [ ] Body text (smaller font, may be multi-line)
  - [ ] Not empty (should show "暂无" if truly no news)
- [ ] **Block A Slot** shows title + body
- [ ] **Block B Slot** shows title + body

#### Content Filtering
- [ ] **Paper1 (FORMAL)** shows:
  - [ ] Bootstrap news (Day 1)
  - [ ] Random daily news (if any)
  - [ ] Formal fact-based news (if any)
- [ ] **Paper2 (SENSATIONAL)** shows:
  - [ ] Only sensational fact-based news
  - [ ] Empty or "暂无" if no such news exists yet
- [ ] **Paper3 (INVESTIGATIVE)** shows:
  - [ ] Only investigative fact-based news
  - [ ] Empty or "暂无" if no such news exists yet

#### Debug Logging
Check console for proper log format:
- [ ] On panel open: `[NewsUI] day=X media=FORMAL total=Y show=3 mode=Collapsed`
- [ ] On tab switch: `[NewsUI] SwitchTab index=N media=XXX`
- [ ] On refresh: `[NewsUI] day=X media=XXX total=Y show=Z mode=M`
- [ ] Logs are **not spammy** (one log per action, not per frame)

#### Empty State
To test, check Paper2 or Paper3 on early days when no fact news exists:
- [ ] Shows "暂无" in slots
- [ ] No errors in console
- [ ] Panel still functional

### 2. Multi-Day Behavior

#### Advance to Day 2
- [ ] Click "End Day" button
- [ ] Day advances to 2
- [ ] Newspaper panel refreshes (if open)
- [ ] Console log: `[NewsUI] day=2 media=XXX total=Y show=Z mode=Collapsed`

#### Day Filtering
- [ ] Only **current day's news** appears in newspaper
- [ ] Yesterday's news does **not** appear
- [ ] Total count in log reflects current day only

#### Sorting Verification
When multiple news items exist on same day:
- [ ] Newest news appears first (higher day number if somehow different)
- [ ] News with same day sorted by severity (if implemented)
- [ ] Order is consistent across tab switches

### 3. Integration with Fact System

#### Trigger a Fact
Perform an action that generates a fact (e.g., anomaly spawn, investigation completion):
- [ ] Fact is created (check console for `[FactNews]` logs)
- [ ] News is generated from fact
- [ ] News appears in appropriate media tab
- [ ] News has proper title and body (not "FACT_..." placeholders)

#### Media Distribution
When fact news exists:
- [ ] Same fact generates news for multiple media
- [ ] FORMAL version in Paper1
- [ ] SENSATIONAL version in Paper2 (different wording)
- [ ] INVESTIGATIVE version in Paper3 (different wording)
- [ ] Each media shows different interpretation of same fact

### 4. Edge Cases

#### No News Scenario
- [ ] Start new game
- [ ] Open newspaper on Day 1
- [ ] At minimum, bootstrap news appears
- [ ] If truly no news, shows placeholder "暂无"

#### Many News Scenario
Generate lots of news (e.g., multiple anomalies, tasks):
- [ ] Top 3 news appear in slots (legacy mode)
- [ ] Older news are not shown (correctly filtered)
- [ ] No UI overflow or layout issues
- [ ] Console log shows correct total count

#### Close/Reopen
- [ ] Close newspaper panel
- [ ] Open again
- [ ] Content refreshes correctly
- [ ] No duplicate items
- [ ] Correct media tab active

---

## 5. Optional: Scrollable Mode Testing

**NOTE**: Only test this section if you've completed Unity Editor setup per `Docs/NewspaperUISetup.md`

### After Unity Editor Setup
- [ ] NewsItem.prefab assigned in Inspector
- [ ] NewsContentRoot reference set
- [ ] ShowMoreButton reference set
- [ ] `useLegacySlots` unchecked in Inspector

### Scrollable List Display
- [ ] Open newspaper panel
- [ ] See dynamic list of news items (not fixed slots)
- [ ] Each item shows in NewsItem prefab format:
  - [ ] Background box
  - [ ] Title (bold, yellow-white, 24pt)
  - [ ] Body (regular, light gray, 18pt, multi-line)

### Show-More Button
- [ ] When 3 or fewer items:
  - [ ] Show-more button **hidden**
- [ ] When more than 3 items:
  - [ ] Show-more button **visible**
  - [ ] Text shows: "显示全部 (N条更多)" where N = total - 3

### Expand/Collapse
- [ ] Click "显示全部" button
  - [ ] List expands to show all items (up to 30)
  - [ ] Button text changes to "收起"
  - [ ] Console log: `[NewsUI] day=X media=Y total=Z show=W mode=Expanded`
- [ ] Click "收起" button
  - [ ] List collapses to 3 items
  - [ ] Button text changes to "显示全部 (N条更多)"
  - [ ] Console log: `[NewsUI] day=X media=Y total=Z show=3 mode=Collapsed`

### Scrolling
- [ ] When more than viewport can fit:
  - [ ] Scroll bar appears
  - [ ] Can scroll up/down
  - [ ] All items accessible

### Empty State (Scrollable)
- [ ] When no news:
  - [ ] Shows single placeholder item
  - [ ] Title: "暂无报道"
  - [ ] Body: "今日无新闻事件"
  - [ ] Show-more button hidden

### Tab Switching (Scrollable)
- [ ] Switch between tabs
- [ ] Content refreshes correctly
- [ ] Scroll position resets
- [ ] Show-more state resets to collapsed

---

## Known Issues & Limitations

### Expected Behavior
- **Legacy news defaults to FORMAL**: Bootstrap and random daily news appear only in Paper1
- **Severity sorting is placeholder**: All news have severity=0, so day is primary sort
- **Max 30 items in expanded mode**: Performance limitation (scrollable mode only)
- **No object pooling**: Items created/destroyed on each render (acceptable for low counts)

### Not Issues (By Design)
- **Paper2/Paper3 may be empty early**: Fact news only appears after game events
- **Same fact appears in all tabs**: Each media has different wording
- **No pagination in legacy mode**: Only shows top 3 items

---

## Troubleshooting

### Issue: No news appears
**Check**:
- [ ] Is it Day 1? Bootstrap news should appear
- [ ] Check console for `[NewsGen]` logs
- [ ] Check `State.NewsLog` count in debugger
- [ ] Verify media filtering is correct

### Issue: All tabs show same content
**Check**:
- [ ] Only have legacy news? (defaults to FORMAL)
- [ ] Need fact-based news to see media distribution
- [ ] Trigger game events to generate facts

### Issue: "暂无" shows but news exists
**Check**:
- [ ] Are you on correct day? (news filtered by day)
- [ ] Check console log for total count
- [ ] Verify media profile matches tab

### Issue: Console spam
**Check**:
- [ ] Should only log on user action (open/switch/expand)
- [ ] If logging every frame, check for recursion
- [ ] Review `_isGeneratingNews` guard in FactNewsGenerator

### Issue: Show-more button not working (scrollable mode)
**Check**:
- [ ] Is `useLegacySlots` unchecked?
- [ ] Are references wired in Inspector?
- [ ] Is NewsItemPrefab assigned?
- [ ] Check console for errors

---

## Success Criteria

### Minimum (Legacy Mode)
✅ Newspaper panel opens without errors  
✅ Three media tabs switch correctly  
✅ Each news shows title + body  
✅ Filtering by media works  
✅ Sorting by day works  
✅ Empty state shows placeholder  
✅ Debug logs are clean and informative  

### Full (Scrollable Mode)
✅ All minimum criteria met  
✅ Scrollable list displays correctly  
✅ Show-more button appears when > 3 items  
✅ Expanding shows up to 30 items  
✅ Collapsing returns to 3 items  
✅ Empty state shows proper placeholder  
✅ Performance is acceptable  

---

## Developer Notes

### Code Structure
```
NewspaperPanelView.cs
├── Render(mediaProfileId)
│   ├── Filter by day + media
│   ├── Sort by day desc, severity desc
│   └── Choose mode:
│       ├── RenderLegacySlots() - Uses existing slots
│       └── RenderScrollableList() - Dynamic instantiation
└── ToggleShowMore()
    └── Toggle _isExpanded, re-render
```

### Data Flow
```
User Action
  ↓
NewspaperPanelSwitcher.ShowPaper(index)
  ↓
NewspaperPanelView.Render(mediaProfileId)
  ↓
Filter: NewsLog.Where(day + media)
  ↓
Sort: OrderByDescending(day, severity)
  ↓
Display: RenderLegacySlots() or RenderScrollableList()
```

### Key Constants
- `DefaultDisplayCount = 3` - Items shown by default
- `MaxRenderCount = 30` - Max items when expanded
- `mediaProfileId` default: "FORMAL"

### Important Files
- `Assets/Scripts/UI/NewspaperPanelView.cs` - Main logic
- `Assets/Scripts/UI/NewsItemView.cs` - Item component
- `Assets/Prefabs/UI/NewsItem.prefab` - Item prefab
- `Assets/Scripts/Core/News.cs` - NewsInstance model

---

## Final Verification

Before marking as complete, verify:
- [ ] All "Basic Functionality" tests pass
- [ ] Debug logging is clean
- [ ] No console errors
- [ ] Documentation is accurate
- [ ] Code follows project conventions
- [ ] Ready for code review
- [ ] Ready for merge to main

---

## Next Steps

1. **Run Basic Tests**: Complete section 1-4 of this checklist
2. **Optional**: Set up scrollable mode per `Docs/NewspaperUISetup.md`
3. **Report Issues**: Document any problems found
4. **Request Review**: When ready, request PR review
5. **Merge**: After approval, merge to main branch

## Support

For questions or issues:
- Review `Docs/NewspaperUIImplementation.md` for technical details
- Review `Docs/NewspaperUIVisualReference.md` for UI structure
- Review `Docs/NewspaperUISetup.md` for Unity Editor setup
- Check code comments in `NewspaperPanelView.cs`
