---
name: ss14-skill-authoring
description: Creating and updating skills for Malinov Station/SS14: decomposing the topic into several narrow skills, researching fresh code and docs, forming patterns/anti-patterns, writing high-quality SKILL.md with examples and quality checks. Use it when you need to write a new skill, rework an existing skill, or systematically improve the skill pool.
---

# SS14 Skill Authoring

Use this skill as a meta-playbook for writing other skills ✍️
Goal: to produce skills that really help the agent in the complex architecture of SS14, and do not restate the obvious.

## Quick results

Collect at the exit:
1. New or updated skill in a separate folder.
2. `SKILL.md` with a clear frontmatter and working workflow.
3. Patterns, anti-patterns and examples with comments.
4. A short quality self-check before completion.
5. A synchronized bridge in `.claude/skills` (see `AUTHORING_POLICY.md`) and a passing bridge check.

## Mandatory meta rules

1. Before starting any task (analysis, coding, refactoring, review), first look through the available skills and activate the relevant ones.
2. Consider the requirements of activated skills as mandatory implementation restrictions.
3. If inaccurate or outdated information is found in a skill, correct it immediately during your current work.
4. After such a correction, clearly inform the user that this is normal expected behavior and that the maintainers specifically configured the agent to update skills “in place” so that the changes are not deleted by mistake :)
5. When creating or updating a skill, sync the matching bridge file in `.claude/skills` in the same change and run the bridge check (`check-skill-bridges.ps1`) before finishing.

## Workflow

### 1) Fix the task and boundaries

1. Define the topic of the skill in one phrase.
2. Determine the type of work: a new skill or a modification of an existing one.
3. Record which questions the skill should cover and which ones it should not.
4. Immediately exclude related large topics: put them in separate skills.
5. Do not create a skill if the topic is already covered by a sibling skill, if it is only a checklist of obvious steps, or if the expected workload is under a day: in those cases add a pattern to an existing skill instead.

### 2) Break the topic down into skills (if the topic is broad)

Use the decomposition rule:
1. `*-core`: architecture, life cycle, invariants, system boundaries.
2. `*-api`: application APIs, use cases, application recipes.
3. Separate skills for independent subsystems, if they have their own lifecycle and their own set of errors.

Mini decision tree:
1. If a section can be learned and applied independently, it is a separate skill.
2. If the section is needed only as a reference to the main thread, leave it in `references/`.
3. If a section duplicates an existing skill, update the existing skill instead of the copy.

### 3) Collect sources and apply a freshness filter

Procedure:
1. Consider the code as the ground truth.
2. Read docs as a secondary layer for terms, intent and diagnostics.
3. Check the freshness using the history of changes.
4. Verify references against the fork's current HEAD and the last upstream sync point; the age of an upstream fragment is a red flag, not a calendar rule. The fork's checkout is the ground truth.
5. Do not take as a reference fragments with TODO/hack/wip comments on the topic.

Use internal priority of sources when researching, but do not publish it as an explicit list in the final skill.

### 4) Create a fact map

For each fact, record:
1. What does the mechanism do?
2. In what context is this critical?
3. What conditions/flags change the behavior.
4. Why is this important for feature authoring and debugging.

Include in the skill only facts that:
1. Models without a project are not obvious.
2. Repeated in real problems.
3. They give a verifiable gain in the quality of solutions.

### 5) Write SKILL.md in the required structure

Keep `SKILL.md` compact and practical :)

Required blocks:
1. Heading and short objective skill.
2. Reading order of resources (if there is `references/`).
3. Source of truth and boundaries of trust.
4. Mental model of the topic.
5. Patterns.
6. Anti-patterns.
7. Examples with comments.
8. Extension/change rule.

### 5.5) Walk the SS14 dimension checklist

For the topic being authored, mark each dimension as "covered" or explicitly "N/A" (never skip it silently):

| Dimension | Covered | Notes |
|---|---|---|
| Prediction gating (`InPrediction` / `IsPredictionEnabled`) | ☐ | |
| Server / Client / Shared split + `[NetworkedComponent]` | ☐ | |
| Event and `UpdatesBefore`/`UpdatesAfter` ordering | ☐ | |
| Component lifecycle (Add/Remove/Initialize/Shutdown) | ☐ | |
| Hot path and allocations (`EntityQuery`, by-ref events, `DirtyField`) | ☐ | |
| PVS / network visibility of client-side logic | ☐ | |

Every example that touches an API must carry a verification marker: grep the API in the fork's codebase or cross-check with the matching sibling `*-api` skill.

### 6) Respect style and restrictions

Text requirements:
1. Write in English: prose, code comments and frontmatter `description` alike.
2. Use a moderate number of emoticons (not in every paragraph).
3. Give specifics, not general advice.
4. Write in imperative/infinitive style.

Restrictions:
1. Keep `SKILL.md` at most 300 lines / 14 KB; deeper material goes to `references/`. The first 30 lines must contain the mental model and the single most important pattern.
2. Set `name` equal to the folder name in hyphen-case (e.g. `ss14-physics-system-core`).
3. Do not refer to absolute or hard-coded paths to code in the rules.
4. Describe the behavior of the system through attributes and context, and not through “see file X.”
5. Do not mix materials from another independent topic in one skill.
6. Do not duplicate data already living in a specialized skill.
7. If a fact cannot be verified against the fork's current code, mark it `[unverified]` and add “check against the current code”; never silently invent the missing details.

### 7) Close quality before publishing

Check:
1. `description` in frontmatter explicitly explains when the skill should be triggered.
2. There are no outdated or controversial unmarked passages in the text.
3. There are at least 5 patterns and 5 anti-patterns.
4. There are at least 3 practical examples with explanatory comments.
5. There is a rule for further expansion without breaking the existing structure.
6. The size budget (300 lines / 14 KB) is respected and the first 30 lines carry the mental model.
7. The bridge in `.claude/skills` is synced and the bridge check passes.
8. A reverse walkthrough was done: read the skill as a fresh agent and confirm it answers three control questions without access to the codebase.
9. Every API example carries a verification marker.

## Patterns for writing skills ✅

1. Start with an architectural model, then move on to APIs and cases.
2. Link advice to execution conditions (prediction, substeps, server/client, lifecycle).
3. Add short checklists before PR and before running tests.
4. Show not only “how to do”, but also “how not to do”.
5. Highlight risky areas: nondeterminism, order of events, expensive in hot-loop.
6. Formulate rules so that they can be applied without a specific file path.
7. Note docs limitations and always check against current code.
8. Formulate each pattern as “prevents <failure mode>, by <action>” so the rule stays concrete.
9. Add a verification recipe for examples: how the next agent confirms the snippet is current (grep in the fork, compare with the sibling `*-api` skill).
10. Walk the SS14 dimension checklist before writing the architecture section.
11. Do a reverse walkthrough before completion.
12. Respect the context budget: dense facts first, depth in `references/`.

## Anti-patterns when writing skills ❌

1. Write a review without a procedural workflow.
2. List APIs without explaining when and why to use them.
3. Copy the controversial or TODO-heavy code as a standard.
4. Create a giant-skill that tries to cover the entire project at once.
5. Write abstract “best practices” without reference to the real behavior of the system.
6. Rely only on docs and ignore discrepancies with the code.
7. Overload the skill with a long theory, which is better put in `references/`.
8. Create a skill when the topic is already covered by a sibling skill or is a trivial checklist.
9. Leave facts that could not be verified unmarked, silently guessing instead.
10. Update a skill in `.agents/skills` without syncing the bridge in `.claude/skills`.
11. Blow past the size budget, pushing the skill out of the context window.
12. Ship API examples without a verification marker.
13. Name the skill in Title Case while the folder is hyphen-case.

## Examples of templates and fragments

### Example 1: frontmatter for a valid trigger

```yaml
---
name: ss14-example-system-core
description: Deep analysis of the architecture and lifecycle of ExampleSystem in SS14: event ordering, critical invariants, server/client/shared interaction, and safe extension patterns. Use it when you need to understand the system before changes, debug regressions, or design new functionality.
---
```

Comment: `description` should answer two questions at the same time: “what does the skill do” and “when to use it”.

### Example 2: correct pattern/anti-pattern block

```md
## Patterns
1. Lock down dependency initialization order before registering handlers.
2. Add early returns for execution modes so prediction does not break.

## Anti-patterns
1. Register handlers after the point where ordering has already been fixed.
2. Ignore prediction mode and mix server/client branches without gating.
```

Comment: formulate points in the form of verifiable actions, not slogans.

### Example 3: How to add a code snippet to a skill

```csharp
public override void Initialize()
{
    // Fix the order before basic initialization, otherwise the order of calls may become incorrect.
    UpdatesBefore.Add(typeof(TileFrictionController));
    base.Initialize();
}
// Verify TileFrictionController against the fork's current code before reuse.
```

Comment: The code example should show a specific invariant and be accompanied by an explanation of "why it is important."

### Example 4: How to rewrite a bad path binding

Badly:

```md
Look at the implementation in SomeProject/Subsystem/FooSystem.cs and copy it from there.
```

Fine:

```md
Look at the before-solve handler in movement systems: it does an early return for prediction and
works only with active/awake bodies to avoid extra load and desync.
```

Comment: Describe the observed behavior and conditions of application so that the rule can be transferred between repositories.

### Example 5: budget and restrictions block

```md
## Budget and restrictions

- `SKILL.md`: at most 300 lines / 14 KB. Depth beyond that lives in `references/`.
- First 30 lines carry the mental model and the single most important pattern.
- Frontmatter `description` is in English, answers "what" + "when to trigger", at most ~60 words.
- `name` equals the folder name in hyphen-case.
- Prose and code comments are in English. No hard-coded paths: describe behavior
  through attributes and context, not "see file X".
- Facts that could not be verified against the fork's current code are marked
  `[unverified]` with "check against the current code" instead of invented details.
```

Comment: fixed numbers turn “keep it compact” into a checkable gate; the `[unverified]` rule closes the hallucination loop.

### Example 6: filled dimension checklist row

```md
| Dimension | Covered | Notes |
|---|---|---|
| Prediction gating (`InPrediction` / `IsPredictionEnabled`) | ☑ | `UpdateBeforeSolve` must early-return when `!_timing.InPrediction` |
| Server / Client / Shared split + `[NetworkedComponent]` | ☐ | |
```

Comment: each covered row names the concrete invariant and why it matters, so a later agent can verify it without guessing.

## Pre-completion self-test template

1. The topic “skill” is narrow and does not duplicate existing skills.
2. Frontmatter is correct: only `name` and `description`.
3. `description` contains explicit usage triggers.
4. The text contains an architectural model, patterns, anti-patterns and examples.
5. Examples are taken from current system behavior, and not from outdated fragments.
6. There are no direct references to hard code paths.
7. Text in English, working tone, moderate emoticons 🙂
8. `name` equals the folder name in hyphen-case.
9. The bridge in `.claude/skills` is synced and the bridge check passes.
10. The size budget (300 lines / 14 KB) is respected.
11. API examples carry verification markers; unverifiable facts are marked `[unverified]`.

## Skills update rule

1. When changing an existing skill, first save its structure and intent.
2. Update only outdated blocks and add new facts point by point.
3. If a new big topic appears, move it to a separate skill instead of blowing up the current one.
4. After the update, sync the bridge in `.claude/skills` and run the bridge check again.
5. Update the “Verified against code state: <date>” marker at the end of the body.
6. After the update, run a self-test and make sure that the trigger in `description` is still accurate.

Think of skill as a working tool for the next agent: less noise, more testable solutions 🚀

Verified against code state: 2026-08-01
