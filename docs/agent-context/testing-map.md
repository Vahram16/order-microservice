# Test Ownership Map

Use this file to choose **which test project owns verification**. Load `architecture/testing.md` only when detailed verification strategy/CI semantics are needed.

| Production area | Primary test owner |
| --- | --- |
| Customer domain, slices, API, persistence, concurrency/idempotency | `tests/Customer.Api.Tests` |
| Shared CQRS/application pipeline behavior | `tests/Microservices.Application.Tests` |
| Repository-wide dependency/boundary rules | `tests/Microservices.ArchitectureTests` |
| Messaging contracts/topology/outbox/inbox/retry/recovery | `tests/Microservices.Messaging.Tests` |
| Framework-free result/error primitives | `tests/Microservices.Primitives.Tests` |
| Shared token validation / authorization policy plumbing | `tests/Microservices.Security.Tests` |
| Shared framework/service-default behavior | `tests/Microservices.ServiceDefaults.Tests` |

## Routing rules

- Start with the owning component's test project and the nearest existing test.
- Add repository architecture tests when dependency direction or project boundaries change.
- Add messaging/security/platform test projects only when those boundaries are actually affected.
- A new real service needs its own service-specific test ownership plus repository architecture coverage.
- CI remains the final pull-request authority; local verification is evidence, not a substitute for required CI checks.

Do not run or load every test suite merely because it exists. Verification should be narrow-to-broad and proportional to the affected boundaries.