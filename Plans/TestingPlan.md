# TestingPlan

This document enumerates tests (manual + automated) still needed to validate hierarchical list functionality.

## Legend
Type: U=Unit, I=Integration (DB + service), UI=UI / Interaction, P=Performance, A=Accessibility.
Priority: H=High, M=Medium, L=Low.

## 1. Service Layer Tests (U/I)
1.1 CreateListAsync
- Valid name persists list (I,H)
- Duplicate name per user rejected (I,H)

1.2 AddItemAsync (root)
- Adds with sparse order increasing (I,H)
- Multiple adds produce ascending order gaps (I,M)

1.3 AddChildItemAsync
- Depth enforcement (attempt depth 4 fails) (I,H)
- Parent list mismatch rejected (I,H)
- Concurrency mismatch throws (I,H)

1.4 MoveItemAsync
- Move within same parent updates order at tail (I,H)
- Move into new parent maintains depth limit (I,H)
- Prevent cycle (move ancestor into descendant) (I,H)
- Concurrency mismatch returns false + current revision (I,H)

1.5 SetItemOrderAsync
- Mid-range order recalculation respaces correctly (I,H)
- Ordering stable after multiple operations (I,M)

1.6 SetItemCompletedAsync
- Completing leaf succeeds (I,H)
- Completing parent with incomplete child fails (I,H)
- Auto-complete parent when all children complete (I,H)
- Un-complete child propagates incomplete up ancestors (I,H)

1.7 ResetSubtreeAsync
- Marks entire subtree incomplete (I,H)
- Ancestors also forced incomplete (I,M)
- Concurrency mismatch returns false (I,H)

1.8 Expanded State APIs
- Default expanded true when no row (I,M)
- Persist expanded false then reload (I,H)
- Bulk fetch returns dictionary subset for list scope (I,M)

1.9 RenameItemAsync
- Valid rename bumps revision (I,H)
- No-op rename (same name) ignored (I,M)
- Concurrency mismatch returns false + new revision (I,H)

1.10 Revision Behavior
- Structural ops bump revision (+1) (I,H)
- Non-structural (expanded state) do not bump revision (I,H)

1.11 Authentication
- Register + Authenticate round trip (I,H)
- Password hash verifies constant time equality (U,M)
- Iteration extraction fallback default (U,L)

1.12 Daily Reset
- Daily list resets items at date boundary (simulated) (I,M)
- Non-daily list unchanged (I,M)

## 2. UI Component Tests (UI/U)
2.1 Hierarchy Rendering
- Root + children + grandchildren appear with correct indent (UI,H)
- Collapse hides descendants (UI,H)
- Expand restores previous selection validity (UI,M)

2.2 Drag & Drop
- Drag into: becomes child (UI,H)
- Drag below: ordering changes, stays sibling (UI,H)
- Hover expand triggers after delay (UI,M)
- Pre-drag hold visual state (UI,M)
- Abort drag clears states (UI,M)

2.3 Inline Rename
- Toggle shows entry with original text (UI,H)
- Save updates label immediately (UI,H)
- Cancel restores original (UI,M)
- Empty or whitespace aborts rename (UI,M)
- Concurrency mismatch refreshes list + alert (UI,H)

2.4 Completion Tri-State
- Partial indicator displays "-" only when some children incomplete (UI,H)
- Parent auto-check when all children complete (UI,H)
- Attempt to check parent with incomplete child shows alert and reverts (UI,H)

2.5 Subtree Reset
- Button enabled when selection present (UI,M)
- Reset sets all descendants incomplete (UI,H)
- Parent selection persists after refresh (UI,M)

2.6 Keyboard Navigation (Windows)
- Up/Down selects sibling (UI,H)
- Left collapses or selects parent (UI,H)
- Right expands or moves into first child (UI,H)

2.7 Responsive Layout
- Width < threshold stacks cards vertically (UI,M)
- Width >= threshold displays two-column (UI,M)

2.8 Theme Toggle
- Dark theme applies background/stroke changes (UI,M)
- RebuildVisibleItems triggers style refresh (UI,L)

2.9 Completed Badge
- Hidden when any item incomplete (UI,M)
- Visible when all items complete (UI,M)

2.10 Selection Persistence
- After refresh selected item remains selected if still present (UI,M)
- Collapsing ancestor re-selects ancestor, not hidden descendant (UI,M)

2.11 Child Creation Controls
- Hidden when selection depth >= 3 (UI,H)
- Enabled only when text non-empty (UI,M)

2.12 Error Handling UI
- Alerts shown on blocked moves (depth/cycle) (UI,H)
- Alerts shown on concurrency mismatches (UI,H)

## 3. Accessibility (A/UI)
3.1 Expand Icon Semantic Name toggles Expand/Collapse (A,H)
3.2 Focus restoration after list change (Windows) (A,M)
3.3 Color contrast meets accessible ratios (Primary vs background) (A,M)

## 4. Performance / Reliability (P/I)
4.1 Polling loop: no overlapping refresh calls under rapid revisions (I,M)
4.2 Large subtree (stress ~200 items) drag responsiveness (UI,L)
4.3 Rename + rapid drag sequence does not deadlock states (UI,L)

## 5. Security (U/I)
5.1 Password hashing salt length correct (U,M)
5.2 ConstantTimeEquals negative cases (U,L)
5.3 Unauthorized access (simulate missing userId) aborts theme save (I,M)

## 6. Concurrency Scenarios (I/UI)
6.1 Simultaneous rename vs move (I,H)
6.2 Simultaneous reorder from two clients -> mid refresh resolves (I,H)
6.3 Simultaneous subtree reset and child add (I,M)

## 7. Data Integrity (I)
7.1 Depth invariant: no item inserted beyond level 3 (I,H)
7.2 Order resequencing keeps monotonic increasing multiples of step (I,M)
7.3 Deleting an item removes its expanded state rows (I,M)

## 8. Regression Guards (Automate later)
8.1 Ensure revision increments exactly once per structural operation.
8.2 Ensure expanded state insert does not alter revision.
8.3 Ensure daily reset does not violate completion gating logic afterward.

## Automation Strategy
- Service tests: xUnit + test PostgreSQL (could use container) seeded per test.
- UI tests: .NET MAUI UITest / Playwright for Windows + snapshot comparison.
- Accessibility: axe + manual contrast verification.
- Performance: measure drag operation time with stopwatch under stress dataset.

## Priorities to Implement First
1) High-priority Service invariants (1.3, 1.4, 1.6, 1.7, 1.10)
2) Core UI interactions (2.2, 2.4, 2.6, 2.5)
3) Concurrency edge cases (6.1, 6.2)
4) Accessibility semantics (3.1)

## Exit Criteria
All High priority tests passing. No known active issues. Concurrency mismatches gracefully handled. Depth invariant enforced with tests. Drag & drop stable for 50+ items.

## Open Questions
- Should subtree reset exclude ancestor incomplete propagation? (Currently includes.)
- Should daily reset also clear expanded states? (Currently leaves them.)

## Next Actions
- Implement unit/integration test project.
- Script seed/test teardown.
- Add CI pipeline to run tests on PR.
