# Skill Authoring Policy

## Scope

This repository uses four skill trees:

- `.agents/skills` is the source of truth and stores full skill content and resources.
- `.agent/skills` is the Antigravity compatibility layer.
- `.claude/skills` is the Claude Code compatibility layer.
- `.cursor/skills` is the Cursor compatibility layer.

## Required Rule

For every skill in `.agents/skills/<skill-name>`, keep matching bridge files:

- `.agent/skills/<skill-name>/SKILL.md`
- `.claude/skills/<skill-name>/SKILL.md`
- `.cursor/skills/<skill-name>/SKILL.md`

When creating, updating, renaming, or deleting a skill in `.agents/skills`, apply the same bridge
change in `.agent/skills`, `.claude/skills`, and `.cursor/skills` in the same pull request.

## Bridge Contract

Each Antigravity bridge SKILL file must contain:

- `name`: exact `<skill-name>` folder name in hyphen-case.
- `description`: synchronized copy of canonical description from `.agents/skills/<skill-name>/SKILL.md`.
- `metadata.source_skill`: `../../../.agents/skills/<skill-name>/SKILL.md`.
- A reference in the markdown body to `../../../.agents/skills/<skill-name>/SKILL.md`.

Each Claude bridge SKILL file must contain:

- `name`: exact `<skill-name>` folder name in hyphen-case.
- `description`: synchronized copy of canonical description from `.agents/skills/<skill-name>/SKILL.md`.
- A reference in the markdown body to `../../../.agents/skills/<skill-name>/SKILL.md`.

Each Cursor bridge SKILL file must contain:

- `name`: exact `<skill-name>` folder name in hyphen-case.
- `description`: synchronized copy of canonical description from `.agents/skills/<skill-name>/SKILL.md`.
- A reference in the markdown body to `../../../.agents/skills/<skill-name>/SKILL.md`.

## PR Checklist Gate

A PR is incomplete if any skill exists in `.agents/skills` without matching bridges in
`.agent/skills`, `.claude/skills`, and `.cursor/skills`.

Run this check before pushing:

`pwsh ./.agents/skills/check-skill-bridges.ps1`
