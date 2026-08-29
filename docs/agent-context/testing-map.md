# Test Ownership Map

Use this file to choose **which test project owns verification**. Load `architecture/testing.md` only when detailed verification strategy/CI semantics are needed.

| Production area | Primary test owner |
| --- | --- |
| Customer domain, slices, API, persistence, concurrency/idempotency | `tests/Customer.Api.Tests` |
| Inventory domain, reservation workflow, API, persistence | `tests/Inventory.Api.Tests` |
| Order domain, slices, workflow/idempotency, API, persistence | `tests/Order.Api.Tests` |
| Payment domain, payment methods, Order-payment/3DS/provider boundary | `tests/Payment.Api.Tests` |
| Product domain, slices, API, persistence, catalog publication | `tests/Product.Api.Tests` |
| Shared CQRS/application pipeline behavior | `tests/Microservices.Application.Tests` |
| Repository-wide dependency/boundary rules | `tests/Microservices.ArchitectureTests` |
| Messaging contracts/topology/outbox/inbox/retry/recovery | `tests/Microservices.Messaging.Tests` |
| Framework-free result/error primitives | `tests/Microservices.Primitives.Tests` |
| Shared token validation / authorization policy plumbing | `tests/Microservices.Security.Tests` |
| Shared framework/service-default behavior | `tests/Microservices.ServiceDefaults.Tests` |

## Routing rules

- Start with the owning component's test project and nearest existing test.
- New Order/Inventory services require service-specific domain/persistence/workflow tests plus repository architecture coverage.
- Product messaging changes require Product tests plus affected messaging/dependency checks.
- Payment Order-payment/3DS changes require Payment domain/provider/webhook tests.
- Add repository architecture tests when dependency direction or project boundaries change.
- Add messaging/security/platform test projects only when those boundaries are actually affected.
- CI remains the final pull-request authority; local verification is evidence, not a substitute for required CI checks.

Do not run or load every suite merely because it exists. Verification should be narrow-to-broad and proportional to affected boundaries.
