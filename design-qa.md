# Controller Mapper UI redesign — design QA

final result: passed

## Source and scope

User-provided design references:
- `C:/Users/1/Pictures/Screenshots/屏幕截图 2026-09-23 071348.png` — four-card settings hub.
- `C:/Users/1/Pictures/Screenshots/屏幕截图 2026-09-23 071257.png` — centered controller, callouts, back/help and bottom actions.
- The remaining supplied screenshots ground the sparse per-setting page structure.

This is a redesign of an existing WPF desktop product, not a browser prototype or an exact functional clone of PlayStation Accessories. Native WPF rendering is used for verification. Existing global configuration and engine behavior are preserved.

## Evidence

Implementation: `artifacts/frontend-v2-qa/`:
- `01-home.png`: empty/default hub, 1260 × 760.
- `02-controller.png`: controller overview, 1260 × 760.
- `03-mapping-editor.png`: validation error modal, 1260 × 760.
- `04-mapping-list.png`: configured mappings and Shift rule, 1260 × 760.
- `05-turbo.png`: valid trigger/output configuration, 1260 × 760.
- `06-small-device.png`: no device, 1000 × 660.
- `07-small-mapping.png`: compact mapping page, 1000 × 660.
- `08-profiles.png`: configuration management, 1260 × 760.

WPF RenderTargetBitmap captures use 96 DPI (one image pixel per layout unit); there is no CSS viewport. The source hub is approximately 1260 × 755. The mapping reference includes an OS title bar; its application-owned region is compared separately from that chrome. Minor frame-size differences are not treated as fidelity findings. Source and implementation were opened in the same comparison tool input. Modal and configuration captures were also opened at full resolution for focused control and copy review.

## Comparison history and findings

1. Initial comparison: [P2] rule-list button displayed only `0`, omitting its action label. Fixed by formatting a TextBlock inside the button. Final controller capture shows “查看映射规则（0）”.
2. Initial comparison: [P2] controller labels lacked visual connection to the related controls. Added thin UI callout connectors anchored to the generated line-art asset. Rechecked the normal and compact controller views.
3. Initial review: [P2] persistent output state could become ambiguous after status messages changed. Added a separate enabled/disabled label beside the status message, independent of transient messages.
4. Interaction review: staged Shift edits must not affect the profile when cancelled, and clearing Shift must not orphan existing layer rules. Implemented draft state and atomic validation before replacing mapping rules. Both cases pass checks.

No actionable P0/P1/P2 findings remain for the requested scope.

## Required fidelity surfaces

- Fonts/typography: Microsoft YaHei UI / Segoe UI; centered 25-unit titles, 17-unit section text, 13-unit body text. Chinese text remains readable at normal size. Text truncation is limited to status and long configuration names, with tooltips.
- Spacing/layout: two by two 180-unit cards match the reference hierarchy. Each setting has a separate page. Bottom stop/save controls remain outside scrollable page content. The controller preview scales at the minimum window size.
- Colors/tokens: near-black background fading toward navy; charcoal cards; white main text and pill buttons. Blue is reserved for control accent and active input. Previous mint/teal dashboard styling is removed.
- Image quality: generated 1536 × 1024 unbranded controller outline is embedded as a WPF resource. It is downsampled without stretching, with edge opacity to blend its dark background. It replaces the old handmade filled controller drawing. Navigation icons use the existing WPF UI icon library. Connector lines are interactive UI annotations, not replacement illustration artwork.
- Copy/content: labels describe real supported functionality. Unsupported stick curves, trigger deadzones and rumble controls are documented as unavailable, not represented by fake working settings.

## Intentional adaptations

- Four hub cards expose mapping, turbo, profiles and device/runtime control to match this app's implemented capabilities.
- Native Xbox logical-button names remain in configuration data; callouts include familiar PlayStation equivalents.
- The controller illustration is generic and unbranded. Independent back-button availability is clearly qualified.
- A compact persistent status/stop/save area is retained because this product emits virtual input.

## Verification

28 checks pass, including global-schema migration, navigation, editor open/cancel, mapping replacement, invalid-edit rollback, Shift dependencies, save behavior and arm prerequisites. WPF binding log is empty. Checks use isolated data and fake IO, without connecting a virtual driver. Physical hardware and games remain untested.

## Follow-up polish

P3: the smallest supported viewport scales the controller labels down; the same inputs remain available in the full-size editor dropdown. No functional controls are clipped or hidden by the footer.
