---
name: 03 – Test strategy and baseline tests
about: Generate test strategy and baseline automated tests
title: "03 – Define test strategy and generate baseline tests"
labels: ["agent:testing"]
assignees: ["copilot"]
---

## Objective
Establish a test baseline to protect existing behaviour before modernisation.

## Inputs
- /docs/HLD.md
- /docs/LLD.md
- /docs/Migration-Plan.md

## Scope
- Tests only
- Minimal production changes allowed only to enable testability (must be justified)

## Required Outputs (commit to repo)
1) `/docs/Test-Strategy.md`
- Unit vs integration vs smoke tests
- Critical flows covered (explicit list)
- Known gaps (explicit list)

2) Automated tests
- Unit tests for core logic
- Integration tests for key endpoints (at least: health + one domain flow)

3) CI
- Add GitHub Actions workflow to build + run tests on PR

## Acceptance Criteria
- Tests pass on the existing monolith
- CI runs successfully on PR
- No refactor yet

## Review Gate
All tests green before any modernisation work begins.
