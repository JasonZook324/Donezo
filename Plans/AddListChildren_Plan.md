Add hierarchical children items to lists with drag-and-drop ordering, depth limit, completion gating, tri-state UI, multi-user concurrency, child item creation UI, and inline in-place rename.

Decisions (key points)
- Depth limit: 3 (root=1, child=2, grandchild=3).
- Sorting: Sparse integer Order (step=1024) + recursive CTE sort_key.
- Drag-and-drop: into (mid) / below (bottom gap) with hover expand + optimistic concurrency via list revision.
- Completion gating: Direct children must be complete; parent auto-completes then.
- Expanded state: Per-user persistence; no revision bump.
- Concurrency: Optimistic with lists.revision.
- Polling: 10s revision diff refresh.
- Child creation UI: Entry + "+ Sub-item" visible only when selection depth < 3.
- Rename: Inline editor (Entry + Save/Cancel) with optimistic concurrency, local optimistic update.
- Bulk subtree reset: Implemented via ResetSubtreeAsync and bound to "Reset Subtree" button.

Status Summary
Service: Complete (ordering, move, rename, subtree reset APIs).
UI: Hierarchy display, tri-state visuals, expanded persistence, DnD reorder/move, hover expand, polling, pre-drag hold, bottom insertion gap, keyboard navigation & reorder, child creation, inline rename, subtree reset.

Resolved Issues
1) Dark theme readability.
2) Expand glyph clarity & semantics.
3) Hierarchy visual accents (badges/indent/colors).
4) Root reorder stability.
5) Pre-drag visual emphasis (500ms hold).
6) Drop position clarity (single bottom gap).
7) Removed dual-gap flicker.
8) Keyboard expand/collapse & sibling navigation.
9) Child creation with depth enforcement.
10) Focus retention after list switch.
11) Prompt rename replaced by inline editor.
12) Bulk subtree reset implemented.

Active Issues
- None.

Implementation Notes (latest)
- Inline rename: Button toggles editing; Save performs concurrency check; Cancel restores original.
- Name property setter public for in-place update; EditableName used as edit buffer.
- Entry visibility bound to IsRenaming; label visibility inverted.
- Drag logic unaffected by rename state.
- Subtree reset ensures all ancestors marked incomplete to preserve gating semantics.

Test Plan Overview
Detailed test cases moved to TestingPlan.md (new). That file enumerates remaining manual/automated tests to implement.

Smoke Test (Condensed)
A) Auth
B) Create list & items
C) Add child & grandchild
D) Completion gating
E) Reorder by drag & keyboard
F) Inline rename cycle (start, save, cancel)
G) Theme toggle
H) Concurrency (second instance rename vs first)
I) Delete parent with descendants
J) Stress add children
K) Navigation keys after list switch
L) Subtree reset behavior

Future Feature Candidates
- Search/filter (text, hide completed, depth filter).
- Offline queue.
- Completed items collapse toggle.
- Multi-select / bulk operations.
- Undo stack for item operations.

Maintenance Rules
- Preserve depth limit (MaxDepth=3).
- Always refresh after structural operations to avoid stale hierarchy.
- UI converters remain lightweight/no heavy allocation.

Next Focus
- Implement automated tests per TestingPlan.md (unit, integration, UI) and CI verification.
