---
name: jira-work-intake
description: Read and normalize Jira work for the agentic development flow without changing Jira or source code. Use when discovering candidate Jira issues, validating explicit eligibility criteria, summarizing acceptance criteria/dependencies, or deciding whether an issue is ready for implementation planning.
---

# Jira Work Intake

This is a read-only intake workflow. Do not edit Jira, transition issues, create branches, or modify repository files.

## Inputs

The orchestrator must provide either:

- an exact Jira issue key; or
- an explicit JQL eligibility query.

Do not invent a Jira project, board, sprint, assignee, status, label, or due-date policy. In particular, do not equate "today's tasks" with tasks that are safe for autonomous execution. Eligibility is an organizational contract supplied by the orchestrator.

## Procedure

1. Use the configured Atlassian MCP server to read the issue(s).
2. For each issue, collect only context needed to understand implementation scope:
   - key and summary;
   - description and acceptance criteria;
   - status, priority, assignee, labels/components when available;
   - linked/blocking issues;
   - relevant recent comments or attached design decisions when they materially affect scope.
3. Detect missing or conflicting acceptance criteria rather than filling gaps with assumptions.
4. Identify whether the repository/bounded context is known from the issue. Do not infer a new Order domain from the repository name alone.
5. Classify each issue:
   - `ready_for_planning`: scope is sufficiently testable and no blocker is known;
   - `blocked`: a dependency or explicit blocker prevents planning/execution;
   - `needs_clarification`: material business/acceptance information is missing;
   - `manual_only`: the requested work is inherently outside safe automated implementation boundaries.
6. Mark work `manual_only` when it requires production deployment/secret operations, destructive production data work, unapproved breaking contracts, or another operation prohibited by `AGENTS.md`.

## Output

Return a concise normalized intake object or table containing:

- issue key;
- summary;
- classification;
- acceptance criteria summary;
- known dependencies/blockers;
- likely bounded context/repository area only when supported by evidence;
- missing information;
- recommended next action.

Do not create an implementation plan in this skill. A `ready_for_planning` issue is handed to `$jira-implementation-plan`.