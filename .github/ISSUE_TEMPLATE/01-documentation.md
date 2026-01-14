---
name: 01 – Document current system (HLD / LLD)
about: Generate accurate system documentation for the existing monolith
title: "01 – Document current monolith (HLD / LLD / ADRs)"
labels: ["agent:documentation"]
assignees: ["copilot"]
---

## Objective
Produce clear, accurate documentation of the current application as it exists today.

## Focus areas
- Identify domain boundaries (Products, Cart, Orders, Checkout)
- Identify data model and EF Core usage
- Identify key endpoints and user flows

## Scope
- Do not change application behaviour
- No refactoring
- Documentation only

## Required Outputs (commit to repo)
Create / update:

1) `/docs/HLD.md`
- System overview, components/modules
- Data stores and external dependencies
- Runtime assumptions (how it runs)

2) `/docs/LLD.md`
- Key classes/services per module
- Main request flows
- Areas of coupling / hotspots

3) `/docs/ADR/`
- 2–4 short ADRs capturing implicit design decisions

4) `/docs/Runbook.md`
- Build/run locally
- Common commands
- Known issues / tech debt observed

## Acceptance Criteria
- Reflects codebase accurately
- No speculative future design
- No code changes except docs

## Review Gate
Human review required before proceeding.
