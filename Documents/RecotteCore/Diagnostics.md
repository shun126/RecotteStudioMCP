# Recotte.Core diagnostic codes

Codes, rather than English messages, are the stable machine-readable contract. Existing codes keep their prior meaning.

| Category | Codes | Meaning |
| --- | --- | --- |
| JSON syntax/load | RC1002 | I/O or JSON parse/load failure |
| Required structure | RC1101, RC1102, RC2001 | app version or required typed structure missing/invalid |
| Registry/reference integrity | RC2101, RC2102 | unregistered file or speaker reference |
| Timeline | RC2201, RC2202, RC2203 | invalid range or duplicate/cross-layer object key |
| New-project creation | RC2301-RC2307 | required root/settings, GUID, name, directory, layers, styles, time, or template sanitization failure |
| Unknown retained structure | RC3001, RC3002 | unknown layer/object preserved |
| Version compatibility | RC3101, RC3102 | unknown or read-only version |
| Editing/duration | RC4101-RC4102, RC4201-RC4207, RC4301, RC4401-RC4405 | unsupported edit, lock, duration, missing or incompatible clone template, or key allocation |
| Save planning/validation | RC5101-RC5107 | capability, edit state, validity, path, overwrite, or required safety option |
| External change | RC5201 | source state differs from load/latest Save |
| Backup/recovery | RC5301-RC5303 | unsafe, non-adjacent, or colliding backup path |
| Cleanup | RC5401 | non-fatal temporary-file cleanup failure |
| Lookup/reference resolution | RC6001, RC6002 | target not found or ambiguous; ambiguous candidates are never selected implicitly |
| Batch editing | RC6101, RC6104 | unsupported operation/profile or rollback-only session commit attempt |
| Batch SaveCopy | RC6202 | verified SaveCopy failed after a successful committed batch; inspect `ProjectSaveFailureKind` |

Major save failures are represented by `RecotteProjectSaveException.Kind`, including backup/write/reload/post-validation/semantic comparison/replacement/restoration stages. Successful operation warnings remain in `ProjectSaveResult.Warnings`.
