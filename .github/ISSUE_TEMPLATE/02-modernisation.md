---
name: 02 – Modernisation plan
about: Propose target architecture and incremental modernisation strategy
title: "02 – Propose target architecture and migration plan"
labels: ["agent:modernisation"]
assignees: ["copilot"]
---

## Objective
Design a target architecture and a safe, incremental migration plan from the current monolith.

## Inputs
- /docs/HLD.md
- /docs/LLD.md
- Existing ADRs

## Constraints
- Container-based deployment
- Incremental (strangler-style) migration
- Preserve behaviour during migration
- No big-bang rewrite

## Required Outputs (commit to repo)
1) `/docs/Target-Architecture.md`
- Proposed service boundaries
- Deployment model (containers, routing, configuration)
- Comms patterns and data access approach

2) `/docs/Migration-Plan.md`
- Step-by-step phases
- Choose the FIRST slice to extract and justify
- Risk + rollback notes per phase

3) `/docs/ADR/`
- Add ADRs for major new decisions

## Acceptance Criteria
- First slice is minimal and demoable
- Plan is achievable with current stack
- No implementation yet

## Review Gate
Human approval of “Slice 1” before tests/coding begin.
