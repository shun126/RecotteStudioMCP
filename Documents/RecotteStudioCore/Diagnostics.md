# RecotteStudio.Core diagnostic codes

Codes, rather than English messages, are the stable machine-readable contract. Existing codes keep their prior meaning.

| Category | Codes | Meaning |
| --- | --- | --- |
| JSON syntax/load | RC1002 | I/O or JSON parse/load failure |
| Required structure | RC1101, RC1102, RC2001 | app version or required typed structure missing/invalid |
| Registry/reference integrity | RC2101, RC2102 | unregistered file or speaker reference |
| Timeline | RC2201, RC2202, RC2203 | invalid range or duplicate/cross-layer object key |
| Layout (retired) | RC2401 | no longer reported; the code is not reused |
| New-project creation | RC2301-RC2307 | required root/settings, GUID, name, directory, layers, styles, time, or template sanitization failure |
| Unknown retained structure | RC3001, RC3002 | unknown layer/object preserved |
| Version compatibility | RC3101, RC3102 | unknown or read-only version |
| Layer creation and order | RC4001-RC4005 | RC4001: the project has no layers array or the verified Video, Speaker, or Annot layer template is unavailable; RC4002: the requested layer name is blank or already used by another layer; RC4003: the requested insertion index is outside 0 to the layer count; RC4004: the project does not define the text style or telop frame a new Speaker layer uses; RC4005: the layer to move does not exist or is locked |
| Editing/duration | RC4101-RC4102, RC4201-RC4204, RC4207-RC4211, RC4301-RC4304, RC4401-RC4405 | unsupported edit, lock, duration, missing or incompatible clone template, or key allocation; RC4208 is a warning that an audio-backed Speaker Voice's text changed, or that it was moved to a Speaker layer of a different speaker, and its audio must be regenerated in Recotte Studio; RC4209-RC4211 reject moving a Speaker Voice to a layer that is not another unlocked Speaker layer, when no new object key can be allocated, or when it overlaps a voice on the target layer |
| Character import | RC4601-RC4607 | donor or target lacks a speaker registry or layers, the speaker or its Speaker layer is not unique in the donor, the speaker is already registered, the donor speaker has no model, the time range is invalid or the donor layer is locked, the donor and target are incompatible (a target speaker, text style, or telop frame the target uses is missing from the donor, or a character already in the target is placed differently in the donor), or the imported layer name is blank or already used |
| Figure (stamp) placement | RC4801-RC4805 | figure shape not verified for placement (only SVG figures with the plain `figure` style are), invalid time range or size, target is not an unlocked Annot layer, template or file-item registry unavailable, or key allocation |
| Clip timing | RC4301-RC4304 | `MoveTimelineObject` on an Image, Figure, or Video. RC4301: the start is not before the end; RC4302: a fade lasts longer than the new length, or another effect cannot follow a new length; RC4303: another object on the layer, such as a transition between two clips, is joined to the clip; RC4304: a video has no plain `StartTime`/`EndTime` source range to resize. Locks are checked as in bounds editing, and an unsupported object fails with RC4202 |
| Bounds editing | RC4911-RC4913 | RC4911: the target is not an Image or Figure on an Annot layer or a Video on a Video layer, or has no valid `Bounds`; RC4912: its `Bounds` are animated with keyframes; RC4913: no value is given, or width or height is not positive. A locked layer or a `locked`/`tl-locked` object fails with RC4207, but the preview lock flags `st-locked` and `pv-locked` do not block it |
| Speaker action | RC4921-RC4925 | RC4921: the target is not an unlocked Speaker layer holding a character with actions; RC4922: the action is blank or not one of the layer character's actions (the message lists them); RC4923: the time range is invalid or shorter than the action's 0.2-second blend-in and blend-out together; RC4924: the verified Speaker Action template is unavailable; RC4925: key allocation |
| Save planning/validation | RC5101-RC5107 | capability, edit state, validity, path, overwrite, or required safety option |
| Fade editing | RC4901-RC4905 | unsupported target type, malformed sub-object array, invalid fade index, unsupported fade key, or duration outside the clip |
| External change | RC5201 | source state differs from load/latest Save |
| Backup/recovery | RC5301-RC5303 | unsafe, non-adjacent, or colliding backup path |
| Cleanup | RC5401 | non-fatal temporary-file cleanup failure |
| Lookup/reference resolution | RC6001, RC6002 | target not found or ambiguous; ambiguous candidates are never selected implicitly |
| Batch editing | RC6101, RC6104 | unsupported operation/profile or rollback-only session commit attempt |
| Batch SaveCopy | RC6202 | verified SaveCopy failed after a successful committed batch; inspect `ProjectSaveFailureKind` |

Major save failures are represented by `RecotteProjectSaveException.Kind`, including backup/write/reload/post-validation/semantic comparison/replacement/restoration stages. Successful operation warnings remain in `ProjectSaveResult.Warnings`.
