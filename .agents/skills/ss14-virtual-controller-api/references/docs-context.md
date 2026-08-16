# Docs Context (VirtualController API)

## The role of docs for the API

1. Docs are needed for concepts and general rules, but do not replace code.
2. Record API facts based on the current implementation of controllers and physics system.
3. If there is a conflict between docs and runtime code, choose runtime code.

## Relevant pages

The shared pages on the physics pipeline and prediction/networking (`robust-toolbox/transform/physics.md`, `ss14-by-example/prediction-guide.md`, `ss14-by-example/basic-networking-and-you.md`) are owned by `ss14-virtual-controller-core/references/docs-context.md`.

1. `general-development/codebase-info/conventions.md` — date `2026-01-19` `[unverified]`.
What to take: rules for reusable API, dirty/field-delta, shared naming conventions.
Restriction: Use as style-guideline, not runtime behavior.

## Application practice

1. First select the method by `fresh-pattern-catalog.md`.
2. Then check the docs only to clarify the intent.
3. If the docs suggest an old pattern, move it to `rejected-snippets.md`, not to the working rules.
