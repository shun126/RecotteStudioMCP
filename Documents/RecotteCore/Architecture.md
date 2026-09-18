# Recotte.Core architecture and verification status

Confirmed by repository samples and automated tests: the 1.8.5.0 structural reader/validator, unknown-field retention, transactional constrained Speaker Voice edits, semantic SaveCopy, backed-up Save, SHA-256 conflict detection, CRLF/BOM-free output, and exact profile lookup.

Inferred and deliberately constrained: text-only Speaker Voice creation clones an existing same-layer template and uses maximum `objkey` plus one. No proprietary identifier or derived value is guessed.

Unconfirmed: Recotte Studio 1.7.1.2 write compatibility and application-level acceptance of generated files. Consequently 1.7.1.2 is read-only, unknown versions cannot save, and no migration exists.

The mutable JSON tree remains internal. Public typed views are snapshots. A format registry selects only exact version profiles; the document and editor consume explicit capabilities. Save Preview and execution share a plan builder, and execution rechecks the plan to reduce preview/use races. File I/O remains internal so future MCP, CLI, and GUI layers can call the SDK without the SDK depending on them.
