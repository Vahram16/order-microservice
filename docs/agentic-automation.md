# Agentic development automation

This document defines the repository-side contract for a governed Jira -> Codex -> GitHub development flow.

The design intentionally separates probabilistic reasoning from deterministic workflow control:

```text
Jira work item
    |
    v
Deterministic orchestrator/state machine
    |
    +--> Codex Jira intake (read only)
    |
    +--> Codex implementation planning (read only)
    |        |
    |        v
    |    immutable plan.json
    |        |
    |        v
    |    HUMAN APPROVAL
    |        |
    +--------+
    |
    +--> isolated branch/worktree
    |
    +--> Codex approved-plan implementation (workspace write)
    |
    +--> deterministic build/test/architecture checks
    |
    +--> draft GitHub pull request
    |
    +--> CI + Codex PR review + human/CODEOWNERS review
    |
    +--> feedback-fix loop when needed
    |
    +--> HUMAN MERGE / branch-protection policy
    |
    v
Jira link/comment/transition
```

Codex is the reasoning and coding engine. It is not the workflow database, approval authority, CI authority, deployment authority, or merge authority.

## Repository assets

- `AGENTS.md` — durable repository engineering rules and safety boundaries.
- `.codex/config.toml` — repository-scoped optional Atlassian MCP endpoint; no credentials.
- `.agents/skills/order-microservice-architecture/SKILL.md` — architecture/context-routing skill.
- `.agents/skills/jira-work-intake/SKILL.md` — read-only Jira normalization/eligibility skill.
- `.agents/skills/jira-implementation-plan/SKILL.md` — read-only implementation planning skill.
- `.agents/skills/approved-plan-implementation/SKILL.md` — approved workspace-write implementation skill.
- `.agents/skills/pr-review/SKILL.md` — architecture/security/correctness PR review skill.
- `.agents/skills/pr-feedback-fix/SKILL.md` — bounded reviewer/CI feedback loop.
- `.automation/schemas/plan.schema.json` — machine-readable planning contract.
- `.automation/schemas/execution-result.schema.json` — machine-readable execution-report contract.
- `.automation/config.example.json` — organization-specific workflow values that must be configured outside the generic repository contract.

## Trust and responsibility boundaries

### Jira

Jira is the work-system source of truth for issue identity, acceptance criteria, dependencies, eligibility state, and human approval state.

The generic repository does not define what "today's tasks" means. An organization must supply explicit JQL or an exact issue key. A due date alone is not an execution authorization.

Recommended organization policy is a dedicated explicit eligibility state/label, for example a team-defined equivalent of `Ready for AI`, but the exact value is external configuration and is not hard-coded here.

### Orchestrator

The orchestrator is deterministic application code. A production implementation can be a .NET Worker Service, durable workflow engine, or another auditable state-machine host.

It owns:

- state transitions;
- retries and timeouts;
- issue/branch/PR correlation;
- immutable plan storage and plan fingerprinting;
- approval verification;
- process exit codes and deterministic verification evidence;
- GitHub branch/PR operations;
- Jira comments/transitions after successful gates;
- secrets and machine identities;
- audit/event persistence;
- idempotency and recovery after crashes.

The LLM must never decide that its own plan is approved.

### Codex

Codex owns:

- repository/Jira context synthesis;
- implementation planning;
- coding within an isolated workspace after approval;
- agent-assisted diff review and reviewer-feedback interpretation.

Codex does not own merge, deployment, production access, approval persistence, or the authoritative result of CI.

### GitHub and CI

GitHub is the source of truth for branch/commit/PR identity and code review. `.github/workflows/dotnet-ci.yml` remains the authoritative deterministic PR gate.

Agent self-reporting such as "tests pass" is never a substitute for captured command exit codes or GitHub check results.

### Human

At minimum, humans own:

- plan approval or rejection;
- approval of material replans;
- high-risk/manual-only architecture/security/contract decisions;
- final merge through repository branch-protection/review policy.

## State machine

A production orchestrator should persist an explicit state rather than infer progress from conversation text:

```text
Discovered
  -> Validated
  -> Planning
  -> AwaitingApproval
  -> Approved
  -> Implementing
  -> Verifying
  -> PullRequestCreated
  -> AwaitingReview
  -> FixingFeedback       (optional loop)
  -> AwaitingReview
  -> Merged
  -> Completed
```

Terminal/non-happy states:

```text
NeedsClarification
Blocked
Rejected
ReplanRequired
VerificationFailed
AgentFailed
Cancelled
ManualOnly
```

Every transition should record issue key, repository, base revision, branch, plan ID, actor, timestamp, and reason/evidence.

## Atlassian Rovo MCP setup

The repository config points to the current Atlassian remote MCP endpoint:

```text
https://mcp.atlassian.com/v1/mcp/authv2
```

For an interactive developer machine, Codex can register the server with:

```bash
codex mcp add atlassian --url https://mcp.atlassian.com/v1/mcp/authv2
```

Then complete the Atlassian OAuth flow when Codex prompts for authentication.

Do not commit OAuth tokens, API tokens, email/token Basic credentials, or service-account API keys.

For unattended orchestration, use only an organization-approved non-interactive identity. Atlassian supports API-token authentication when enabled by the organization administrator, including service-account bearer keys. Store such credentials in the runtime secret store, never in this repository.

The committed MCP server configuration uses `default_tools_approval_mode = "writes"` so write operations remain approval-gated at the Codex MCP layer. The deterministic orchestrator should additionally restrict which stage is allowed to invoke Jira mutations.

## Jira intake

### Input

The orchestrator supplies one of:

```text
issueKey = ABC-123
```

or:

```text
eligibilityJql = <organization-owned explicit JQL>
```

Do not allow the agent to invent the JQL in production.

### Action

Invoke `$jira-work-intake` in a read-only Codex run. The result classifies work as:

- `ready_for_planning`;
- `blocked`;
- `needs_clarification`;
- `manual_only`.

Only `ready_for_planning` can enter the planning stage.

## Planning stage

Run Codex with filesystem read-only intent and structured output. Example for a single issue:

```bash
mkdir -p .automation/runs/ABC-123

codex exec \
  --sandbox read-only \
  --output-schema .automation/schemas/plan.schema.json \
  -o .automation/runs/ABC-123/plan.json \
  '$jira-implementation-plan Create the implementation plan for Jira ABC-123. Base branch: develop. Do not modify the repository.'
```

The orchestrator must validate the JSON again after Codex returns it.

### Plan acceptance checks

Before presenting the plan for approval, the orchestrator rejects/blocks it when:

- schema validation fails;
- `decision != ready`;
- `blockingQuestions` is not empty;
- base revision no longer matches the intended planning snapshot;
- required issue/acceptance data changed while planning;
- policy marks its risk/change class as manual-only.

### Immutable plan identity

After machine validation, serialize the approved plan canonically and create a cryptographic fingerprint (for example SHA-256). Persist:

```text
Issue key
Plan ID / fingerprint
Plan JSON
Base commit SHA
Planner run ID
Created timestamp
Approval status
Approver identity
Approval timestamp
```

Never let the implementation agent regenerate the approved plan from Jira text. It receives the exact approved artifact.

## Human plan gate

A practical Jira workflow can expose organization-specific equivalents of:

```text
Ready for AI
    -> AI Planning
    -> AI Plan Review
        -> AI Approved
        -> Rejected / Needs Clarification
```

Those names are examples only. Configure the actual Jira transitions externally.

The human review surface should show:

- acceptance-criteria summary;
- in/out of scope;
- proposed files and steps;
- architecture classification;
- migration/security/integration impact;
- verification plan;
- risk reasons;
- assumptions and blocking questions;
- base commit SHA and plan ID.

Approval must bind to the exact plan ID. Editing the plan after approval invalidates approval and returns the workflow to planning/review.

## Branch and workspace isolation

After approval, create one branch/worktree per issue, for example:

```text
agent/abc-123-short-description
```

The production orchestrator should run the implementation in a disposable clone/worktree/container scoped to that branch. Prompt instructions and Codex sandboxing are defense in depth; the outer execution environment should enforce the actual filesystem/repository boundary.

Before implementation, verify:

- branch is based on the approved base revision, or an explicit rebase/replan policy has handled drift;
- worktree is clean;
- issue key and plan ID match;
- no unrelated user changes are present.

## Implementation stage

Example:

```bash
codex exec \
  --sandbox workspace-write \
  --output-schema .automation/schemas/execution-result.schema.json \
  -o .automation/runs/ABC-123/execution-result.json \
  '$approved-plan-implementation Implement Jira ABC-123 using the exact approved plan at .automation/runs/ABC-123/plan.json. Plan ID: <fingerprint>. Do not expand scope.'
```

The outer orchestrator should restrict the process working directory to the disposable issue workspace.

If Codex returns `replan_required`, do not continue to PR creation. Persist the reason and return to `Planning`/`AwaitingApproval` with a new plan ID.

## Deterministic verification

After the agent finishes, the orchestrator independently runs the relevant commands. Do not trust only the agent-generated `checks` array.

The repository CI performs a broad production gate including:

- restore of every project;
- Release builds with analyzers/warnings enforced;
- test projects including architecture and Customer integration tests;
- real PostgreSQL and RabbitMQ reliability tests;
- observability artifact validation;
- production Keycloak image build;
- development-realm/security verification.

For local orchestration, first run affected projects/tests for fast feedback. Before merge, GitHub CI remains authoritative.

A failed deterministic check moves the workflow to `VerificationFailed` or to a bounded implementation/fix attempt according to retry policy. Never weaken tests or architecture/security rules automatically to obtain a green build.

## Commit, push, and draft PR

Only after local verification satisfies policy should the orchestrator commit/push the branch and create a **draft** pull request against `develop`.

The PR body should contain machine-generated evidence, not marketing prose:

```text
Jira: ABC-123
Plan ID: <fingerprint>
Base revision: <sha>

Acceptance criteria
- ...

Implementation
- ...

Verification
- command -> exit code/result

Architecture impact
- ...

Risk / residual risk
- ...
```

Link the Jira issue through the organization's supported Jira/GitHub integration and/or a Jira comment. Do not mark Jira Done merely because a draft PR exists.

## PR review stage

Run `$pr-review` against:

- Jira issue;
- exact approved plan;
- full diff from approved base to PR head;
- available CI/check evidence.

The agent returns one of:

- `approve`;
- `changes_requested`;
- `blocked_on_evidence`;
- `replan_required`.

Agent review is an additional quality gate. It does not replace CODEOWNERS, human review, branch protection, or CI.

## Feedback loop

When a reviewer comment or CI result requires a change:

```text
AwaitingReview
    -> FixingFeedback
    -> deterministic verification
    -> AwaitingReview
```

Use `$pr-feedback-fix` for accepted in-scope comments. Material requirement/architecture/security/contract expansion becomes `ReplanRequired` and must receive a new human-approved plan.

Do not allow an unlimited autonomous retry loop. Configure a bounded attempt count and escalate repeated failures with diagnostics.

## Merge and Jira completion

Merge remains human/branch-protection controlled unless the organization later adopts a separately reviewed automatic-merge policy for narrowly classified low-risk work.

After GitHub confirms merge:

1. capture merge commit SHA;
2. add/link PR and merge evidence to Jira if needed;
3. transition the Jira issue to the configured completed state only when its workflow permits;
4. persist final run status;
5. stop all issue-specific workers/watchers.

The agent itself must not decide that merge occurred or transition Jira based on an expected outcome.

## Risk policy

A sensible starting policy is:

| Change class | Automation policy |
| --- | --- |
| Documentation/test-only/local refactor | plan approval, then execute |
| Normal slice-local business change | plan approval, execute, draft PR, CI + human merge |
| Persistence/concurrency change | medium/high; explicit migration/concurrency review |
| New migration/schema contract | high; explicit human approval |
| Integration message/routing change | high; explicit compatibility/ADR review |
| Authentication/authorization/Keycloak | high; explicit security review |
| Shared platform/library behavior | high; broad regression review |
| Breaking public/integration contract | manual-only unless explicitly approved |
| Destructive database/production operation | manual-only |
| Production secrets/deployment | outside this coding-agent flow |

Risk classification is policy input. The LLM can recommend risk but cannot lower an organization-enforced minimum classification.

## Idempotency and crash recovery

Use durable correlation keys:

```text
(repository, issueKey)
issueKey -> active plan ID
issueKey -> branch
issueKey -> pull request
plan ID -> base SHA
run ID -> execution attempt
```

Before every write, check whether the intended artifact already exists. A retry must reuse/update the known branch/PR rather than create duplicates.

Examples:

- if a branch already exists for the active plan, resume it instead of creating another;
- if a draft PR already exists for that branch, update it rather than opening a second PR;
- if Jira already contains the plan comment for the active plan ID, do not duplicate it;
- if the base branch moved after approval, apply the configured drift policy rather than silently continuing.

## Observability and audit

Persist structured events for at least:

- run/attempt ID;
- issue key;
- repository/base/head SHAs;
- state transition from/to;
- plan ID;
- Codex invocation metadata;
- tool/stage outcome;
- deterministic command + exit code + duration;
- approval/rejection actor and timestamp;
- PR number/URL identity;
- CI status;
- failure category and diagnostics location.

Never log secrets, OAuth/API tokens, authorization headers, or user PII from Jira unnecessarily.

Useful operational metrics include plan approval rate, replanning rate, implementation success rate, CI first-pass rate, review defect rate, reverted PR rate, median cycle time, per-stage latency, and agent cost. Autonomy should increase only after these metrics demonstrate acceptable reliability.

## Organization-specific configuration

Copy `.automation/config.example.json` into the external orchestrator's configuration system and replace every placeholder. Do not commit an environment's credentials or assume the example values are valid Jira workflow names.

Required configuration includes:

- Jira eligibility JQL;
- plan-review/approved/completed transition identifiers or names;
- repository/base branch;
- risk policy/manual-only rules;
- retry limits/timeouts;
- GitHub App/installation identity for automation;
- secret-store references for Codex/OpenAI and Atlassian machine credentials when running unattended.

## Optional GitHub Actions Codex review

If the organization wants Codex review inside GitHub Actions, use the official `openai/codex-action` and a repository/organization secret for the OpenAI API key. Keep the action read-only for review work and do not add it to the required CI path until the secret, permissions, trigger policy, and cost controls are deliberately configured.

The existing `dotnet-ci.yml` remains required regardless of whether agent-assisted review is enabled.

## Recommended rollout

Start with one issue at a time:

```text
explicit Jira issue
-> read-only plan
-> human approval
-> implementation branch
-> deterministic verification
-> draft PR
-> CI + human review/merge
```

Measure reliability before enabling automatic discovery of multiple Jira issues. Do not start with an agent consuming an entire sprint autonomously.
