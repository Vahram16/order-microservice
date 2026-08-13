# AppHost Context

Use this document only for changes owned by `src/AppHost/Microservices.AppHost` or when a service change requires local Aspire orchestration updates.

## Purpose

`Microservices.AppHost` is **local development orchestration**, not production application architecture.

It owns local wiring for:

- PostgreSQL resources/databases;
- RabbitMQ resource wiring;
- Keycloak development resource/realm import;
- service API and Migrator startup relationships;
- local dependency readiness/order;
- AppHost development user-secrets.

Current project references include the Customer and ServiceTemplate API/Migrator projects so Aspire can orchestrate them locally.

## What does not belong here

Do not place:

- business/domain logic;
- API endpoint behavior;
- EF model/migrations;
- reusable security primitives;
- messaging abstractions/contracts;
- production deployment logic;
- production secret values.

Production deployments must not depend on AppHost existing.

## When to change AppHost

Typical justified changes:

- a real service is added to local orchestration;
- a new local database/broker/identity dependency is required;
- Migrator-before-API ordering changes because a service topology actually changed;
- development-only Keycloak realm import/resource wiring changes;
- local health/readiness dependency wiring changes.

A normal feature inside an existing service should not modify AppHost unless the feature changes local runtime dependencies.

## New service rule

When an approved new service is introduced, AppHost should orchestrate only the resources/processes needed for local development. The service's production lifecycle, domain behavior, persistence ownership, and security policy stay outside AppHost.

If AppHost changes also affect production infrastructure, load `infrastructure.md` separately; do not infer that local Aspire configuration is production configuration.