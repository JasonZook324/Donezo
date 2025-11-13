Add hierarchical children items to lists with drag-and-drop ordering, depth limit, completion gating, tri-state UI, and multi-user concurrency.

Decisions
- Data access: Raw SQL via Npgsql.
- Depth limit: 3 (root=1, child=2, grandchild=3). No level 4.
- Same-list enforcement: Service code validates parent and child share list.
- Sorting: Stable hierarchical order using `ParentItemId + Order`; derive `SortKey` via recursive CTE at query time.
- Drag-and-drop: Handle drop-on (append as child), drop-above/below (insert as sibling at position), horizontal drag adjusts level (24 px per level), auto-expand on hover after 500 ms.
- Expand state: Persist per user in `item_ui_state`.
- Completion gating: Parents cannot be completed until all direct children are complete; minus state indicates partial.
- Auto-complete ancestors: When a child becomes complete and all siblings complete, auto-complete the parent and continue upward.
- Deletion: Cascade delete subtree (warn with descendant count). No extra warning for list delete.
- Concurrency: Optimistic via `lists.revision` (increment on any item change). Client mutations include expected revision; mismatch rejects and triggers reload.
- Polling: When list view is active, poll every 10 seconds.
- Cross-list drag: Disallowed with toast.
- Accessibility: Use app’s existing icon style.

Schema changes (SQL to apply)
- `items`:
  - Add `parent_item_id int null references items(id) on delete cascade`.
  - Add `order int not null default 0`.
  - Indexes: `(list_id, parent_item_id, order)` and `(parent_item_id)`.
- `lists`:
  - Add `revision bigint not null default 0`.
- `item_ui_state`:
  - Table `(user_id int references users(id) on delete cascade, item_id int references items(id) on delete cascade, expanded boolean not null default true, primary key(user_id, item_id))`.

Service interface (additions; keep existing signatures)
- Records
  - Replace/extend item DTO:
    - `ItemRecord(int Id, int ListId, string Name, bool IsCompleted, int? ParentItemId, bool HasChildren, int ChildrenCount, int IncompleteChildrenCount, int Level, string SortKey, int Order)`.
- Queries
  - `Task<ItemRecord?> GetItemAsync(int itemId, ...)`.
  - `Task<IReadOnlyList<ItemRecord>> GetChildrenAsync(int parentItemId, ...)`.
  - `Task<IReadOnlyList<ItemRecord>> GetItemsAsync(int listId, ...)` returns flattened hierarchy with Level and derived SortKey, ordered by SortKey.
- Mutations (concurrency-aware)
  - `Task<int> AddItemAsync(int listId, string name, int? parentItemId, int? order, long expectedRevision, ...)`.
  - `Task<(SetItemResult Result, long NewRevision)> SetItemCompletedExtendedAsync(int itemId, bool completed, long expectedRevision, ...)`.
  - `Task<(bool Ok, long NewRevision)> UpdateItemParentAsync(int itemId, int? newParentItemId, int? newOrder, long expectedRevision, ...)`.
  - `Task<(bool Ok, long NewRevision)> UpdateItemOrderAsync(int itemId, int newOrder, long expectedRevision, ...)`.
  - `Task<bool> DeleteItemAsync(int itemId, ...)` increments revision.
  - `Task<long> GetListRevisionAsync(int listId, ...)`.
  - Expand state: `Task SetItemExpandedAsync(int userId, int itemId, bool expanded, ...)`, `Task<IDictionary<int, bool>> GetExpandedStatesAsync(int userId, int listId, ...)`.
  - Descendant count for confirmation: `Task<int> GetDescendantCountAsync(int itemId, ...)`.
- Backward compatibility
  - Keep existing `AddItemAsync(listId, name, ...)` and `SetItemCompletedAsync(itemId, completed, ...)` as wrappers that call new logic with defaults.

Key algorithms
- Hierarchy query (recursive CTE)
  - Produce `level` and `path` (`SortKey`) from `ParentItemId + Order`. Order roots by `Order`, then children beneath their parent.
- Depth enforcement
  - On add/move, compute parent depth (via CTE) and ensure `parentDepth + 1 <= 3`.
  - For moving an item with subtree, compute item’s deepest relative level; ensure `newParentDepth + deepestRelative <= 3`.
- Ordering strategy
  - Use sparse ordering with step of 1024 among siblings.
  - Insert between neighbors by midpoint; if no gap, normalize that sibling set in a single transaction.
- Completion gating
  - When completing an item, block if any direct child is incomplete (optimization accepted).
  - When a child completes, check parent; if all direct children complete, set parent complete and continue upward.
  - When setting an item to incomplete, only update that item. Tri-state is computed from children counts.
- Concurrency
  - Mutations require `expectedRevision == lists.revision`; otherwise return `ConcurrencyMismatch` and do not change data.
  - On successful mutation, increment `lists.revision` and return the new value.
- Delete with warning
  - Before delete, compute descendant count via CTE and show dialog.

MAUI UI changes
- List rendering
  - `CollectionView` bound to flattened items with `Level` for indentation (`Margin.Left = Level * 16`).
  - Expand/collapse toggle on parent rows; persist via `item_ui_state` service calls; load states alongside items and filter visible flat list accordingly.
- Tri-state completion control
  - ImageButton using existing icon style:
    - Unchecked square when `IsCompleted == false && IncompleteChildrenCount == 0 && !HasChildren`.
    - Minus when `HasChildren && IncompleteChildrenCount > 0` (blocked: tap shows toast "Complete all child items first").
    - Checked when `IsCompleted == true && IncompleteChildrenCount == 0`.
- Drag-and-drop
  - Row contains a drag handle ImageButton (?). Long-press starts drag on touch; click-drag on desktop.
  - Drop zones:
    - On-item: append as child at end of children.
    - Above/below: insert as sibling at the drop position under the same parent.
  - Horizontal drag: 24 px thresholds adjust intended level (indent guides). Clamp to valid range and max depth.
  - Auto-expand hovered collapsed parents after 500 ms; keep expanded.
  - Invalid target (depth > 3): show red preview/lock and reject with toast.
- Polling
  - When list view is active, poll `GetItemsAsync` every 10 seconds. If revision changed, refresh flat list and maintain expand state.
- Delete dialog
  - Show: "Deleting this item will also delete its X child items. Continue?".
- Cross-list drag
  - Reject with toast: "Cross-list move is not supported.".
- Accessibility
  - Provide `AutomationProperties.Name` and `HelpText` on tri-state ImageButton and drag handle, using existing icon style.

Testing
- Service unit tests
  - Add/move within depth succeeds; exceeding depth fails.
  - Same-list enforcement when adding/moving.
  - Ordering: midpoint insertion, normalization when needed.
  - Completion: blocked when children incomplete; auto-completion of ancestors; incomplete child downgrades parent state visually.
  - Delete: descendant count matches, cascade removes subtree.
  - Concurrency: mismatched revision rejects and returns appropriate result.
- UI tests
  - Drag events: on-item vs above/below behavior; horizontal indent; auto-expand after 500 ms.
  - Tri-state blocked tap shows toast.
  - Expand persistence across sessions.
  - Poll updates reflect concurrent changes.

Rollout steps
1) Apply schema changes.
2) Implement new service methods and records; keep existing signatures operational.
3) Update list page view model to use flattened hierarchical query, expanded states, and tri-state logic.
4) Implement drag-and-drop gestures and visual indicators.
5) Add polling and revision checks.
6) Test across Android, iOS, Windows, Mac Catalyst.
7) Ship.
