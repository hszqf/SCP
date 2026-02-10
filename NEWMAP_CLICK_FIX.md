# NewMap Click Non-Responsiveness Fix

## Issue
NewMap displayed but clicking nodes did not trigger the expected log: `[MapUI] Click nodeId=...`

## Root Cause Analysis
Unity UI click events require two critical components to function:
1. **EventSystem** with **StandaloneInputModule** - handles input events
2. **GraphicRaycaster** on Canvas - detects UI clicks via raycasting

If either component is missing, UI clicks will not work at all.

Additionally, improper `raycastTarget` settings can block clicks:
- Background images with `raycastTarget=true` intercept clicks meant for buttons
- Child elements (text, decorative images) with `raycastTarget=true` can block parent buttons

## Solution Implemented

### 1. Added Comprehensive Diagnostic Logs (Part A)
Located in `NewMapRuntime.Initialize()`:

**Before UI Creation:**
- EventSystem existence and status
- Canvas GraphicRaycaster existence and configuration

**After UI Creation:**
- EventSystem re-check with warning if missing
- Background image raycastTarget status
- Node widget creation logs with button and raycast info

**Example Log Output:**
```
[MapUI] EventSystem found=True (before UI creation)
[MapUI] EventSystem gameObject=EventSystem active=True enabled=True
[MapUI] Canvas GraphicRaycaster found=True canvas=Canvas
[MapUI] GraphicRaycaster enabled=True ignoreReversedGraphics=True blockingObjects=None
[MapUI] NodeWidget created for nodeId=BASE button=True raycastTarget=True
[MapUI] Click nodeId=BASE
```

### 2. Auto-Fix Missing Components
If EventSystem is missing:
- Create new GameObject "EventSystem"
- Add EventSystem component
- Add StandaloneInputModule component

If Canvas lacks GraphicRaycaster:
- Add GraphicRaycaster to Canvas

### 3. Fixed Raycast Target Settings
Set `raycastTarget` appropriately on all UI elements:
- ✅ **Button Image**: `raycastTarget=true` (intercepts clicks)
- ❌ **Background Image**: `raycastTarget=false` (transparent to clicks)
- ❌ **Dot Image**: `raycastTarget=false`
- ❌ **Name Text**: `raycastTarget=false`
- ❌ **Badge Image**: `raycastTarget=false`
- ❌ **Icon Text**: `raycastTarget=false`

## Changes Made
File: `Assets/Scripts/UI/Map/NewMapRuntime.cs`

1. Added `using UnityEngine.EventSystems;`
2. Added diagnostic logs for EventSystem and GraphicRaycaster
3. Auto-create EventSystem if missing (with StandaloneInputModule)
4. Auto-add GraphicRaycaster to Canvas if missing
5. Set `bgImage.raycastTarget = false` to prevent blocking
6. Set `widgetImage.raycastTarget = true` to enable button clicks
7. Set all child elements `raycastTarget = false` to prevent interference
8. Added node widget creation logs

## Verification
After these changes, clicking any node should:
1. Trigger the log: `[MapUI] Click nodeId={nodeId}`
2. Open the NodePanelView via `UIPanelRoot.I.OpenNode(nodeId)`

The diagnostic logs will help identify any remaining issues with EventSystem or raycaster configuration.

## Notes
- No changes to fonts, news system, or scene loading (as required)
- Minimal surgical changes to NewMapRuntime.cs only
- Backward compatible - existing functionality unchanged
- Self-healing - auto-creates missing UI infrastructure components
