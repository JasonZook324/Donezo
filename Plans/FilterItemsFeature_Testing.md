# Filter Items Feature — Testing Checklist

Status: Completed (manual testing passed across target platforms)

## Functional Scenarios (Pass)
- Toggle behavior
  - OFF ? all items visible.
  - ON ? fully completed items (and their completed subtrees) are hidden.
  - Partial items remain visible.
- Immediate update
  - Completing an item with Hide Completed ON removes it from view immediately.
  - Unchecking a completed item with Hide Completed ON shows it again.
- Selection behavior
  - If the selected item becomes hidden, selection clears without errors.
  - Keyboard navigation and move up/down buttons remain enabled only when selection is valid.
- Expansion
  - Expanding/collapsing behaves normally for visible items.
  - Hidden parents/children do not produce orphan visuals.
- Drag & drop
  - Drag between visible items works.
  - No crashes when interacting near boundaries with hidden items.

## Server Persistence (Pass)
- Per user, per list storage reflects on load.
- Toggle updates persist to server and survive list switching.
- Cross-session persistence via server verified.
- Cross-list isolation verified.
- Network failure fallback: local Preferences used; UI remains responsive; no crashes.

## Data/Schema Validation (Pass)
- Table `user_list_prefs` present with PK (user_id, list_id).
- Default `hide_completed=false`.
- Upserts idempotent; `updated_at` refreshed.

## Regression (Pass)
- Theme toggle unaffected.
- Daily flag behavior unaffected (including daily reset path).
- List reset, rename, add item/child behave as before.
- Completed badge reflects true list completion independent of filter.

## Performance (Pass)
- Large list manual test acceptable; no noticeable UI stalls.

## Accessibility (Pass)
- Hide Completed control named and focusable; toggle state announced.

## Notes / Follow-ups
- Optional UX polish: dim visible completed items when filter is OFF; add screen-reader descriptions for drag/drop targets.
- Consider adding UI tests for filtered traversal and selection clearing.
