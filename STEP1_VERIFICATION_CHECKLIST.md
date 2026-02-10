# Step 1 Verification Checklist

## Implementation Complete ✅

All required modifications have been made:

### ✅ Modification A: LogOverlay Filter Update
- **File**: `Assets/Scripts/Runtime/Debug/LogOverlay.cs`
- **Change**: Added `[MapBoot]` and `[MapUI]` to the log filter
- **Line**: 96-104

### ✅ Modification B: MapBootDiagnostics Script
- **File**: `Assets/Scripts/Runtime/Debug/MapBootDiagnostics.cs`
- **Status**: Created new file with all required diagnostics
- **Output Format**: Matches specification exactly

### ✅ Modification C: LogOverlayBootstrap
- **File**: `Assets/Scripts/Runtime/Debug/LogOverlayBootstrap.cs`
- **Status**: Already uses `FindAnyObjectByType` (no changes needed)

## Expected LogOverlay Output

After game launch, you should see exactly these 6 lines in LogOverlay:

```
[MapBoot] OverlayAlive=true overlayType=LogOverlay
[MapBoot] EventSystem count=1
[MapBoot] EventSystem module=StandaloneInputModule
[MapBoot] CanvasRaycaster count=1
[MapBoot] UICamera count=0
[MapBoot] Done
```

**Note**: The actual values may differ based on your scene configuration:
- `OverlayAlive` should be `true` if LogOverlay is working
- `EventSystem count` should be 1 (or it's a problem)
- `EventSystem module` can be `StandaloneInputModule`, `InputSystemUIInputModule`, or `None`
- `CanvasRaycaster count` should be >= 1 for UI to work
- `UICamera count` is optional (0 is OK for Overlay canvas mode)

## How to Verify

### Option 1: WebGL Build (Recommended for iOS testing)
1. Push this branch to GitHub
2. Wait for CI build to complete: https://github.com/hszqf/SCP/actions
3. Deploy the WebGL build
4. Open in browser with `?debug=1` parameter
5. Look for "OVERLAY_OK" at top-left (confirms LogOverlay is rendering)
6. Click the "Export" button in LogOverlay
7. Scroll to find the `[MapBoot]` lines
8. Take a screenshot or copy the text

### Option 2: Unity Editor (Quick local verification)
1. Open the project in Unity Editor
2. Press Play
3. Open Console window (Window > General > Console)
4. Filter by typing "[MapBoot]" in the search box
5. Verify all 6 lines appear
6. Check timestamps to confirm they run in correct order

### Option 3: Check CI Logs
1. Go to: https://github.com/hszqf/SCP/actions
2. Find the latest build for this branch
3. Open "Build WebGL" job logs
4. Search for "[MapBoot]" in the logs
5. Unity Editor logs during build might show these messages

## Troubleshooting

### If LogOverlay doesn't show [MapBoot] logs:
- Verify "OVERLAY_OK" appears at top-left (overlay is rendering)
- Click "Show" button if it says "Hide"
- Check that `Application.logMessageReceived` is subscribed (it should be automatic)

### If [MapBoot] logs don't appear at all:
- Check Unity Console for errors during script compilation
- Verify `MapBootDiagnostics.cs` and `.meta` files exist in `Assets/Scripts/Runtime/Debug/`
- Confirm `RuntimeInitializeOnLoadMethod` attribute is present

### If EventSystem count is 0:
- This indicates a critical setup issue
- EventSystem is required for UI interaction
- Check Main scene has an EventSystem GameObject

### If module type is "None":
- EventSystem exists but has no InputModule component
- UI clicks will not work
- Add either StandaloneInputModule or InputSystemUIInputModule to EventSystem

## Next Steps (Step 2)

Once you've verified the diagnostics output:

1. **Screenshot or Export**: Take a screenshot of the LogOverlay Export panel, or copy the full export text
2. **Share Results**: Post the diagnostic output in the issue
3. **Await Step 2 Instructions**: The next step will ensure EventSystem/InputModule are properly configured for WebGL/iOS

## Technical Notes

### Execution Order
1. `LogOverlayBootstrap` runs at `AfterSceneLoad` and creates LogOverlay
2. `MapBootDiagnostics` runs at `AfterSceneLoad` 
3. `MapBootDiagnostics` waits 1 frame to ensure LogOverlay is initialized
4. Diagnostics run and output 6 log lines
5. LogOverlay captures and displays them (because they use `[MapBoot]` prefix)

### Why the 1-Frame Delay?
The coroutine delay ensures all `RuntimeInitializeOnLoadMethod` scripts have completed initialization. Without this, LogOverlay might not exist yet when diagnostics run, resulting in `OverlayAlive=false`.

### Conditional Compilation
The script uses `#if ENABLE_INPUT_SYSTEM` to check for the new Input System package. If not present, it only checks for the legacy `StandaloneInputModule`.

## Files Changed

```
Assets/Scripts/Runtime/Debug/LogOverlay.cs          (modified)
Assets/Scripts/Runtime/Debug/MapBootDiagnostics.cs  (created)
Assets/Scripts/Runtime/Debug/MapBootDiagnostics.cs.meta (created)
MAPBOOT_DIAGNOSTICS_STEP1.md                        (created)
STEP1_VERIFICATION_CHECKLIST.md                     (this file)
```

## No Build Errors Expected

The implementation:
- Uses only Unity APIs available in Unity 6
- Follows existing code patterns in the repository
- Has no external dependencies
- Uses conditional compilation for optional Input System
- Should compile cleanly in WebGL build

If you encounter build errors, please share the error message for investigation.
