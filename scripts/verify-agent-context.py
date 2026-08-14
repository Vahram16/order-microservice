#!/usr/bin/env python3
"""Validate repository agent context stays small, scoped, and structurally current."""

from __future__ import annotations

import json
import re
import sys
import tomllib
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

REQUIRED_CONTEXT = [
    "docs/agent-context/project-map.md",
    "docs/agent-context/services/customer.md",
    "docs/agent-context/services/product.md",
    "docs/agent-context/services/service-template.md",
    "docs/agent-context/platform/shared-projects.md",
    "docs/agent-context/platform/apphost.md",
    "docs/agent-context/platform/infrastructure.md",
    "docs/agent-context/testing-map.md",
    "docs/agent-context/context-selection-contract.md",
]

EXPLICIT_ONLY_SKILLS = [
    "jira-work-intake",
    "jira-implementation-plan",
    "approved-plan-implementation",
    "pr-review",
    "pr-feedback-fix",
]

CANONICAL_PATHS = [
    "src/Services/Customer/Customer.Api/Features/Customers/UpdatingDetails/V1/UpdateCustomerDetailsEndpoint.cs",
    "src/Services/Customer/Customer.Api/Features/Customers/AddingAddress/V1/AddCustomerAddressHandler.cs",
    "src/Services/Customer/Customer.Api/Persistence/CustomerDbContext.cs",
    "tests/Customer.Api.Tests/CustomerVerticalSliceArchitectureTests.cs",
    "tests/Customer.Api.Tests/CustomerDomainBoundaryTests.cs",
    "tests/Customer.Api.Tests/CustomerDomainTests.cs",
]


def fail(message: str) -> None:
    print(f"[agent-context] ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def require(path: str) -> Path:
    resolved = ROOT / path
    if not resolved.exists():
        fail(f"missing required path: {path}")
    return resolved


def parse_skill_frontmatter(path: Path) -> tuple[str, str]:
    text = path.read_text(encoding="utf-8")
    match = re.match(r"\A---\s*\n(.*?)\n---\s*\n", text, re.DOTALL)
    if not match:
        fail(f"missing YAML frontmatter: {path.relative_to(ROOT)}")

    name_match = re.search(r"^name:\s*(.+?)\s*$", match.group(1), re.MULTILINE)
    description_match = re.search(r"^description:\s*(.+?)\s*$", match.group(1), re.MULTILINE)
    if not name_match or not description_match:
        fail(f"skill frontmatter requires name + description: {path.relative_to(ROOT)}")
    return name_match.group(1).strip().strip('"\''), description_match.group(1).strip().strip('"\'')


def main() -> int:
    for path in REQUIRED_CONTEXT + CANONICAL_PATHS:
        require(path)

    if (ROOT / "docs/agent-context/project-structure.md").exists():
        fail("deprecated monolithic project-structure.md must remain removed")

    agents = require("AGENTS.md")
    project_map = require("docs/agent-context/project-map.md")
    if agents.stat().st_size > 10_000:
        fail(f"AGENTS.md is too large for always-loaded guidance: {agents.stat().st_size} bytes")
    if project_map.stat().st_size > 8_000:
        fail(f"project-map.md is too large for first-hop routing: {project_map.stat().st_size} bytes")

    # Every solution project name must remain discoverable from the compact project map.
    solution = ET.parse(require("Microservices.Boilerplate.slnx")).getroot()
    project_paths = [element.attrib["Path"] for element in solution.iter("Project")]
    map_text = project_map.read_text(encoding="utf-8")
    for project_path in project_paths:
        project_name = Path(project_path).stem
        if project_name not in map_text:
            fail(f"solution project '{project_name}' is missing from project-map.md")

    # Validate skill structure and collect actual names.
    skills_root = require(".agents/skills")
    actual_skills: set[str] = set()
    for skill_dir in sorted(path for path in skills_root.iterdir() if path.is_dir()):
        skill_md = require(str(skill_dir.relative_to(ROOT) / "SKILL.md"))
        name, description = parse_skill_frontmatter(skill_md)
        if name != skill_dir.name:
            fail(f"skill name '{name}' does not match directory '{skill_dir.name}'")
        if len(description) < 20:
            fail(f"skill description is too weak for routing: {skill_dir.name}")
        actual_skills.add(name)

    plan_schema = json.loads(require(".automation/schemas/plan.schema.json").read_text(encoding="utf-8"))
    execution_schema = json.loads(require(".automation/schemas/execution-result.schema.json").read_text(encoding="utf-8"))
    if plan_schema["properties"]["schemaVersion"]["enum"] != ["1.2"]:
        fail("plan schema version must remain 1.2 until the contract intentionally changes")
    if execution_schema["properties"]["schemaVersion"]["enum"] != ["1.2"]:
        fail("execution-result schema version must remain 1.2 until the contract intentionally changes")

    schema_skills = set(
        plan_schema["properties"]["contextSelection"]["properties"]["skills"]["items"]["enum"]
    )
    missing_schema_skills = schema_skills - actual_skills
    if missing_schema_skills:
        fail(f"plan schema references missing skills: {sorted(missing_schema_skills)}")

    for skill in EXPLICIT_ONLY_SKILLS:
        metadata = require(f".agents/skills/{skill}/agents/openai.yaml")
        text = metadata.read_text(encoding="utf-8")
        if not re.search(r"(?m)^\s*allow_implicit_invocation:\s*false\s*$", text):
            fail(f"orchestration skill must be explicit-only: {skill}")

    # Parse project Codex config with stdlib TOML parser.
    with require(".codex/config.toml").open("rb") as stream:
        tomllib.load(stream)

    # Prevent stale references to the removed monolith in the agent harness.
    scanned_roots = [ROOT / "AGENTS.md", ROOT / ".agents", ROOT / ".automation", ROOT / "docs/agent-context"]
    for scanned_root in scanned_roots:
        candidates = [scanned_root] if scanned_root.is_file() else scanned_root.rglob("*")
        for candidate in candidates:
            if not candidate.is_file() or candidate == Path(__file__):
                continue
            if candidate.suffix.lower() not in {".md", ".json", ".toml", ".yaml", ".yml"}:
                continue
            text = candidate.read_text(encoding="utf-8")
            if "project-structure.md" in text:
                fail(f"stale project-structure.md reference: {candidate.relative_to(ROOT)}")

    print(
        "[agent-context] OK: scoped context, solution map, skills, schemas, explicit-only policy, "
        "canonical paths, and Codex config are consistent."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
