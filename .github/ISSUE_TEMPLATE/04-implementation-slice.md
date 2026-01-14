---
name: 04 – Implementation slice
about: Implement a single approved migration slice
title: "04 – Implement migration slice: <slice name>"
labels: ["agent:implementation"]
assignees: ["copilot"]
---

## Objective
Implement ONE approved migration slice from /docs/Migration-Plan.md.

## Inputs
- /docs/Migration-Plan.md
- /docs/Target-Architecture.md
- /docs/Test-Strategy.md

## Scope
- Implement ONE slice only
- Keep PR reviewable (prefer < ~500 LOC net new unless necessary)
- Preserve existing behaviour
- Update docs/tests as needed

## Required Outputs
- Code changes for the slice
- Updated/added tests covering the slice
- Any needed doc updates (Runbook, ADR)

## Acceptance Criteria
- All tests pass (existing + new)
- App builds and runs locally
- Clear instructions to run slice (if separate service)

## Review Gate
Human review + green CI required before merge.
