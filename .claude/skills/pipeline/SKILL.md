---
name: pipeline
description: The Ats pipeline and candidate board - templates, stages, stage-to-status mapping, the kanban move flow, history, and concurrency. Read before changing pipeline or board behavior.
---

# Ats Pipelines and Board

## Templates and stages
`PipelineTemplate` has ordered `PipelineStage`s (`Order`, `IsTerminal`, `TerminalOutcome`,
`ReferralStatusOverride`). Edited via `IPipelineTemplateService.SaveAsync` which diffs the posted
stage set (add/update/remove by id, reorder by `Order`). A template used by a job cannot be deleted.

## Stage-to-status mapping
`ReferralStatusOverride` (defaults to the stage name) is what Phase 3 will send to ReferralTool as the
`CandidateStatus`. Phase 1 only stores it.

## Adding candidates to a job
Two paths, both ending in `IApplicationService` (shared `CreateApplicationAsync` helper: validates the
job + first stage, dedupes on `(candidate, job)`, creates the application and its initial
`ApplicationEvent`):
- Board `Add candidate`: a picker of existing candidates (`-- New candidate --` default). Picking an
  existing candidate posts `CandidateId` and calls `AddExistingCandidateToJobAsync`; choosing new posts
  the name/email fields and calls `AddCandidateToJobAsync` (which create-or-matches by email).
- Candidates list `Add to job`: a per-row published-job dropdown that posts to
  `CandidatesController.AddToJob` and calls `AddExistingCandidateToJobAsync`, then redirects to the board.

## Board and moves
`BoardController` renders columns (stages in order) with cards (active applications by `CurrentStageId`).
Moves post to `Board/Move` (drag-and-drop via SortableJS + htmx, or the per-card select fallback),
which calls `IApplicationService.MoveStageAsync`. A move appends an `ApplicationEvent`, sets terminal
status when the target stage is terminal, and uses `RowVersion` optimistic concurrency (a conflict
returns a friendly reload message and re-renders the board partial). The card carries `RowVersion` as
base64; `Board/Move` returns the `_Board` partial for htmx requests and redirects otherwise. Every move
(forward or backward) is recorded; nothing is emitted to ReferralTool in Phase 1 (that is Phase 3).

## Board UI (redesign)
The rebuilt board keeps this exact move flow (SortableJS drag + the per-card `move-select` fallback +
htmx swap of `#board-container` + `RowVersion`). Only markup changed: cards now show an avatar, email,
source chip, a days-in-stage chip (neutral / amber >3d / red >7d) and stage-progress dots, and a click
on a card body (not the dropdown) opens the candidate drawer via htmx (`Applications/Card`). Terminal
columns are tinted (Hired green, Rejected red) with a dashed drop hint on an empty Hired column. The
board header carries a stats strip (in process, avg days in stage, from ReferralTool, oldest) built in
`BoardController.BuildBoardAsync` from `IApplicationService.LatestEventTimesForJobAsync`.
