# RecotteStudio.Core diagnostic codes

Codes, rather than English messages, are the stable machine-readable contract. Existing codes keep their prior meaning.

| Category | Codes | Meaning |
| --- | --- | --- |
| JSON syntax/load | RC1002 | I/O or JSON parse/load failure |
| Required structure | RC1101, RC1102, RC2001 | app version or required typed structure missing/invalid |
| Registry/reference integrity | RC2101, RC2102 | unregistered file or speaker reference |
| Timeline | RC2201, RC2202, RC2203 | invalid range or duplicate/cross-layer object key |
| Layout | RC2401 | warning that an Image or Video not filling the screen is shown while a Speaker Voice telop is drawn over it; the message names the telop frame and its bounds, and the project stays valid |
| New-project creation | RC2301-RC2307 | required root/settings, GUID, name, directory, layers, styles, time, or template sanitization failure |
| Unknown retained structure | RC3001, RC3002 | unknown layer/object preserved |
| Version compatibility | RC3101, RC3102 | unknown or read-only version |
| Layer creation and order | RC4001-RC4005 | RC4001: the project has no layers array or the verified Video, Speaker, or Annot layer template is unavailable; RC4002: the requested layer name is blank or already used by another layer; RC4003: the requested insertion index is outside 0 to the layer count; RC4004: the project does not define the text style or telop frame a new Speaker layer uses; RC4005: the layer to move does not exist or is locked |
| Editing/duration | RC4101-RC4102, RC4201-RC4204, RC4207-RC4211, RC4301, RC4401-RC4405 | unsupported edit, lock, duration, missing or incompatible clone template, or key allocation; RC4208 is a warning that an audio-backed Speaker Voice's text changed, or that it was moved to a Speaker layer of a different speaker, and its audio must be regenerated in Recotte Studio; RC4209-RC4211 reject moving a Speaker Voice to a layer that is not another unlocked Speaker layer, when no new object key can be allocated, or when it overlaps a voice on the target layer |
| Character import | RC4601-RC4607 | donor or target lacks a speaker registry or layers, the speaker or its Speaker layer is not unique in the donor, the speaker is already registered, the donor speaker has no model, the time range is invalid or the donor layer is locked, the donor and target are incompatible (a target speaker, text style, or telop frame the target uses is missing from the donor, or a character already in the target is placed differently in the donor), or the imported layer name is blank or already used |
| Figure (stamp) placement | RC4801-RC4805 | figure shape not verified for placement (only SVG figures with the plain `figure` style are), invalid time range or size, target is not an unlocked Annot layer, template or file-item registry unavailable, or key allocation |
| Save planning/validation | RC5101-RC5107 | capability, edit state, validity, path, overwrite, or required safety option |
| Fade editing | RC4901-RC4905 | unsupported target type, malformed sub-object array, invalid fade index, unsupported fade key, or duration outside the clip |
| External change | RC5201 | source state differs from load/latest Save |
| Backup/recovery | RC5301-RC5303 | unsafe, non-adjacent, or colliding backup path |
| Cleanup | RC5401 | non-fatal temporary-file cleanup failure |
| Lookup/reference resolution | RC6001, RC6002 | target not found or ambiguous; ambiguous candidates are never selected implicitly |
| Batch editing | RC6101, RC6104 | unsupported operation/profile or rollback-only session commit attempt |
| Batch SaveCopy | RC6202 | verified SaveCopy failed after a successful committed batch; inspect `ProjectSaveFailureKind` |

Major save failures are represented by `RecotteProjectSaveException.Kind`, including backup/write/reload/post-validation/semantic comparison/replacement/restoration stages. Successful operation warnings remain in `ProjectSaveResult.Warnings`.
