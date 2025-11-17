# Feature: Hide Completed Items Toggle

## Status
- Implemented in `DashboardPage` using a `Switch` (`_hideCompletedSwitch`) with a small row above the `CollectionView`.
- Empty-state label added: shows when Hide Completed is ON and all items are filtered; includes hidden count text.
- Persistence implemented per user per list:
  - Server: `INeonDbService.GetListHideCompletedAsync` / `SetListHideCompletedAsync` backed by `user_list_prefs` table.
  - Local fallback: `Preferences` key `LIST_HIDE_COMPLETED_{listId}`.
- Filtering logic implemented via `AddWithDescendantsFiltered` and used in `RebuildVisibleItems()`.
- Completion checkbox handler triggers `RebuildVisibleItems()` when Hide Completed is ON to update the view immediately.
- Selection/drag/drop logic updated to clear selection if the selected item becomes hidden; keyboard navigation preserved.
- Build: passing on all target frameworks.
- Manual testing: Passed per checklist.

## Goals
- Allow users to toggle between:
  - Show All (default): show all items.
  - Hide Completed: hide fully completed items (and thus their completed subtrees).
- Keep existing expansion, drag/drop, selection, and ordering behaviors intact.
- Persist the preference per user per list (server-first; local fallback).

## UX
- A “Hide Completed” switch is in the Items card, above the `CollectionView`.
- Default state is Show All unless server or local preference indicates otherwise.
- When enabled, fully completed items disappear immediately; partially-complete items remain.
- When all items are hidden, a subtle message displays: “All {n} items are completed and hidden.”

## State & Persistence
- `_hideCompleted: bool` stored in `DashboardPage`.
- Persist per `(userId, listId)`.
- Server persistence (implemented):
  - `Task<bool?> GetListHideCompletedAsync(int userId, int listId, CancellationToken ct = default);`
  - `Task SetListHideCompletedAsync(int userId, int listId, bool hideCompleted, CancellationToken ct = default);`
- Local fallback (Preferences):
  - Key: `LIST_HIDE_COMPLETED_{listId}`
  - On toggle: write to server (best-effort) and local.
  - On load: try server ? if null/exception, use local ? default false.

## Data/Backend
- New Postgres table:
  - `user_list_prefs(user_id int, list_id int, hide_completed boolean not null default false, updated_at timestamptz not null default now(), primary key(user_id, list_id))`
- API implementation in `NeonDbService` completed.
- No changes to `ItemRecord`.

## UI Changes (DashboardPage.cs)
- New controls:
  - `_hideCompletedSwitch` (`Switch` with label “Hide Completed”).
  - `_emptyFilteredLabel` placeholder message.
- `BuildUi()` wires `_hideCompletedSwitch.Toggled` to `OnHideCompletedToggledAsync`.

## Load/Save Preference
- After list selection and before building items:
  - `LoadHideCompletedPreferenceForSelectedListAsync()` loads server/local and syncs `_hideCompleted` and the switch state.
- On user toggle:
  - Update `_hideCompleted`.
  - Persist to server (if userId/listId) and local Preferences.
  - Call `RebuildVisibleItems()`.

## Filtering Logic
- `AddWithDescendantsFiltered(ItemVm node, List<ItemVm> target)`:
  - If `_hideCompleted && node.IsCompleted`, skip.
  - Else add and recurse children if expanded.
- `RebuildVisibleItems()` uses filtered traversal when `_hideCompleted` is true.
- If selection becomes hidden, call `ClearSelectionAndUi()`.

## Other Call Sites
- Completion checkbox handler:
  - After successful update, calls `RebuildVisibleItems()` if `_hideCompleted` is ON.
- `RefreshItemsAsync()`:
  - Loads expanded states and items, then loads hide-completed preference, then rebuilds visible items.

## Drag/Drop & Ordering
- Operate on visible set. Hidden completed items are not targetable and selection is cleared when hidden.

## Badge Behavior
- `Completed` badge continues to reflect true completion over `_allItems`.

## Edge Cases
- Entire list completed + Hide Completed ON ? `_items` empty; badge may show Completed; no errors.
- Toggle OFF restores view respecting prior expansion states.
- Parent collapses/expands unaffected by filter logic.
- Offline mode: local preference still works; server update best-effort.

## Implementation Steps (Updated)
1. Add UI control and `_hideCompleted` field with event handler. [Done]
2. Add server API methods and implement in `NeonDbService`. [Done]
3. Add local preference helpers. [Done]
4. Add `LoadHideCompletedPreferenceForSelectedListAsync()` and call it from `RefreshItemsAsync()`. [Done]
5. Add filtered traversal and switch `RebuildVisibleItems()` to use it. [Done]
6. Update completion checkbox handler to rebuild when hiding is active. [Done]
7. Add empty-state label and logic to show when list is fully filtered. [Done]
8. Testing per checklist (cross-platform). [Done]
9. Optional UX polish (dim completed items when visible, add SR-friendly descriptions). [Optional]

## Acceptance Criteria
- Toggling Hide Completed immediately adds/removes fully completed items.
- Preference is remembered per user and per list across sessions (server-first).
- Selection/drag/drop/key navigation stable and error-free.
- Badge reflects true list completion regardless of filter.
- Empty-state label appears appropriately with accurate count.

## Next Step
- Optional UX polish and documentation updates; prepare to merge branch and include in release notes.
