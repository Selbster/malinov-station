# Rejected Snippets (Documentation)

| Zone | What's found | Why rejected | Signal |
|---|---|---|---|
| XML docs | Invalid closing tag `</summarY>` | Breaks XML documentation and auto-generation of tooltips | Tag error |
| XML docs | Malformed attribute value `<param name=isClient">` in `BaseContainer.cs:114` | Missing opening quote breaks the XML tag; the file is cited as a pattern, so it is a live counter-example | Tag error |
| Doc text | Copy-paste remarks in `SharedContainerSystem.Remove.cs:16-19` ("If the **insertion** is successful..." in a Remove method) + typo "rigger" (line 22) | Describes the wrong operation; a reader trusts a wrong contract | Wrong contract |
| Partial files | A partial file without a role header — `SharedContainerSystem.Insert.cs` / `Remove.cs` (unlike `Validation.cs`) | Every partial of a split system must say what part it is | Missing header |
| Language | Doc comments in Russian/mixed language in code | English is accepted for project documentation | Violation of locale standard |
| Callsite docs | Non-standard API call without explanation (`ignore*` flags, `force: true`, `quiet: true` — e.g. `SharedDoorSystem.cs:730`, `ChatUIController.cs:692`, `WieldedPrecondition.cs:43`, `OutfitSystem.cs:68`, `DisposalHolderSystem.cs:141,147`, `CargoSystem.Orders.cs:632`) | Behavior is not obvious during review and refactoring | Hidden special-case |
| Code comments | TODO comment as a replacement for behavior documentation | This is technical debt, not a reliable contract | `TODO/HACK/FIXME` |
| YAML comments | Long multi-line "prologues" at the beginning of the prototype file | Prototypes require brevity, maximum 1 sentence of explanation | Wall of text |
| YAML comments | Edit markers (`Start/End`) instead of describing behavior | The system is not explained and quickly becomes outdated | Noise marks |
| FTL headings | Title without space after `##` (`##bombs`) | Breaks format consistency and degrades support | Invalid format |
| FTL headings | Decorative dividers (`##########`) | They do not provide information about the group of transfers | Noise instead of structure |
| Code comments | Step-by-step retelling of obvious operations | Impairs readability and hides really important places | Over-commenting |

## How to use this list

1. Do not copy these fragments as a "style example".
2. When reviewing, mark such places as technical risks in the quality of documentation.
3. If it cannot be fixed immediately, leave a narrow TODO in the task, but do not turn it into permanent documentation.
