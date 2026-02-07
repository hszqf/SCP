# Newspaper UI Visual Structure

## Current Layout (Legacy Mode)

```
┌─────────────────────────────────────────────┐
│  基金会晨报              Day 5      [Close] │
├─────────────────────────────────────────────┤
│  [Paper1] [Paper2] [Paper3]                 │  ← Tabs for media selection
├─────────────────────────────────────────────┤
│                                             │
│  ┌──────────────────────────────────────┐  │
│  │ Slot_Headline                        │  │
│  │                                      │  │
│  │  HeadlineTitleTMP:   基金会发现异常  │  │  ← Title (from NewsInstance.Title)
│  │  HeadlineDeckTMP:    某地发现...     │  │  ← Body (from NewsInstance.Description)
│  └──────────────────────────────────────┘  │
│                                             │
│  ┌──────────────────────────────────────┐  │
│  │ Slot_BlockA                          │  │
│  │                                      │  │
│  │  BlockATitleTMP:     收容突破事件    │  │  ← Title
│  │  BlockABodyTMP:      某异常逃脱...   │  │  ← Body
│  └──────────────────────────────────────┘  │
│                                             │
│  ┌──────────────────────────────────────┐  │
│  │ Slot_BlockB                          │  │
│  │                                      │  │
│  │  BlockBTitleTMP:     调查完成        │  │  ← Title
│  │  BlockBBodyTMP:      干员成功...     │  │  ← Body
│  └──────────────────────────────────────┘  │
│                                             │
└─────────────────────────────────────────────┘
```

## Future Layout (Scrollable Mode - After Unity Setup)

```
┌─────────────────────────────────────────────┐
│  基金会晨报              Day 5      [Close] │
├─────────────────────────────────────────────┤
│  [Paper1] [Paper2] [Paper3]                 │  ← Tabs for media selection
├─────────────────────────────────────────────┤
│  ╔═══════════════════════════════════════╗ │  ← ScrollView
│  ║                                       ║ │
│  ║  ┌─────────────────────────────────┐ ║ │
│  ║  │ NewsItem #1                     │ ║ │
│  ║  │  [Title] 基金会发现异常          │ ║ │  ← TitleText (bold, 24pt)
│  ║  │  [Body]  某地发现未知异常现象    │ ║ │  ← BodyText (regular, 18pt)
│  ║  │         需要立即派遣干员调查...  │ ║ │     (multi-line, auto-wrap)
│  ║  └─────────────────────────────────┘ ║ │
│  ║                                       ║ │
│  ║  ┌─────────────────────────────────┐ ║ │
│  ║  │ NewsItem #2                     │ ║ │
│  ║  │  [Title] 收容突破事件            │ ║ │
│  ║  │  [Body]  某异常成功逃脱收容室    │ ║ │
│  ║  │         造成重大损失...          │ ║ │
│  ║  └─────────────────────────────────┘ ║ │
│  ║                                       ║ │
│  ║  ┌─────────────────────────────────┐ ║ │
│  ║  │ NewsItem #3                     │ ║ │
│  ║  │  [Title] 调查任务完成            │ ║ │
│  ║  │  [Body]  干员成功完成异常调查    │ ║ │
│  ║  │         获得重要情报...          │ ║ │
│  ║  └─────────────────────────────────┘ ║ │
│  ║                                       ║ │
│  ╚═══════════════════════════════════════╝ │
│                                             │
│         [显示全部 (5条更多)]                │  ← ShowMoreButton (when collapsed)
│  or                                         │
│              [收起]                         │  ← ShowMoreButton (when expanded)
│                                             │
└─────────────────────────────────────────────┘
```

## Empty State

```
┌─────────────────────────────────────────────┐
│  基金会晨报              Day 5      [Close] │
├─────────────────────────────────────────────┤
│  [Paper1] [Paper2] [Paper3]                 │
├─────────────────────────────────────────────┤
│                                             │
│  ┌──────────────────────────────────────┐  │
│  │                                      │  │
│  │         暂无报道                     │  │  ← Placeholder title
│  │                                      │  │
│  │       今日无新闻事件                 │  │  ← Placeholder body
│  │                                      │  │
│  └──────────────────────────────────────┘  │
│                                             │
└─────────────────────────────────────────────┘
```

## Media Tab Behavior

### Paper1 (FORMAL - 正式媒体)
- Filter: `newsInstance.mediaProfileId == "FORMAL"`
- Shows: Bootstrap news, RandomDaily news, Formal fact-based news
- Style: Professional, factual reporting

### Paper2 (SENSATIONAL - 煽情媒体)
- Filter: `newsInstance.mediaProfileId == "SENSATIONAL"`
- Shows: Sensational fact-based news only
- Style: Dramatic, attention-grabbing headlines

### Paper3 (INVESTIGATIVE - 调查媒体)
- Filter: `newsInstance.mediaProfileId == "INVESTIGATIVE"`
- Shows: Investigative fact-based news only
- Style: Detailed, analytical reporting

## Data Flow

```
1. User clicks tab (e.g., Paper2)
   ↓
2. NewspaperPanelSwitcher.ShowPaper(1)
   ↓
3. Sets mediaProfileId = "SENSATIONAL"
   ↓
4. Calls NewspaperPanelView.Render("SENSATIONAL")
   ↓
5. Filter: NewsLog.Where(n => n.Day == currentDay && n.mediaProfileId == "SENSATIONAL")
   ↓
6. Sort: OrderByDescending(day).ThenByDescending(severity)
   ↓
7. Render:
   - Legacy mode: Fill 3 slots with top 3 items
   - Scrollable mode: Instantiate NewsItem prefabs for top 3 (or up to 30 if expanded)
   ↓
8. Display in UI
```

## Component Hierarchy (Scrollable Mode)

```
NewspaperPanel (GameObject)
├── Window (GameObject)
│   ├── Header (GameObject)
│   │   ├── TitleTMP ("基金会晨报")
│   │   └── DayTMP ("Day 5")
│   ├── PaperTabs (GameObject)
│   │   ├── Paper1Button → calls ShowPaper(0)
│   │   ├── Paper2Button → calls ShowPaper(1)
│   │   └── Paper3Button → calls ShowPaper(2)
│   └── PaperPages (GameObject)
│       ├── Paper1 (GameObject) - Active when FORMAL selected
│       │   └── NewsScrollView (ScrollRect)
│       │       └── Viewport
│       │           └── Content (VerticalLayoutGroup + ContentSizeFitter)
│       │               ├── NewsItem (prefab instance #1)
│       │               ├── NewsItem (prefab instance #2)
│       │               └── NewsItem (prefab instance #3)
│       ├── Paper2 (GameObject) - Active when SENSATIONAL selected
│       │   └── NewsScrollView (ScrollRect)
│       │       └── ...
│       └── Paper3 (GameObject) - Active when INVESTIGATIVE selected
│           └── NewsScrollView (ScrollRect)
│               └── ...
└── ShowMoreButton (Button) - at bottom of active page
    └── ShowMoreButtonText (TMP)
```

## NewsItem Prefab Structure

```
NewsItem (GameObject) - Root with NewsItemView component
├── [RectTransform] Anchor: Top, Height: auto (ContentSizeFitter)
├── [Image] Background (dark semi-transparent)
├── [VerticalLayoutGroup] Padding: 10, Spacing: 5
├── [NewsItemView] Component with references
└── Children:
    ├── TitleText (TMP)
    │   └── Font: Bold, Size: 24, Color: Yellow-white
    └── BodyText (TMP)
        └── Font: Regular, Size: 18, Color: Light gray, Word Wrap: ON
```

## Show-More Button States

### Collapsed State (Default)
- Text: "显示全部 (N条更多)" where N = total - 3
- OnClick: Sets `_isExpanded = true`, calls `Render()`
- Visible: Only when total news > 3

### Expanded State
- Text: "收起"
- OnClick: Sets `_isExpanded = false`, calls `Render()`
- Visible: Always when expanded

### Hidden State
- When total news ≤ 3
- Button is hidden (`SetActive(false)`)

## Color Scheme

### TitleText
- Color: `{r: 1, g: 0.9, b: 0.7, a: 1}` (warm yellow-white)
- Font Weight: 700 (Bold)
- Font Size: 24

### BodyText
- Color: `{r: 0.85, g: 0.85, b: 0.85, a: 1}` (light gray)
- Font Weight: 400 (Regular)
- Font Size: 18

### Background (NewsItem)
- Color: `{r: 0.1, g: 0.1, b: 0.1, a: 0.8}` (dark semi-transparent)

## Responsive Behavior

### Scrollable Mode
- Content height: Auto-sized by ContentSizeFitter
- Scroll: Vertical only
- Items: Full width, auto height based on text content
- Overflow: Scrollable when items exceed viewport height

### Legacy Mode
- Fixed slot heights
- Text: May truncate if too long
- No scrolling within slots

## Performance Notes

- **Legacy Mode**: No instantiation, just text updates (very fast)
- **Scrollable Mode**: 
  - Default (3 items): Instantiate 3 GameObjects (~negligible)
  - Expanded (up to 30 items): Instantiate up to 30 GameObjects (~acceptable)
  - No object pooling (not needed for these counts)
  - Items destroyed on each render (simple, no memory leaks)
