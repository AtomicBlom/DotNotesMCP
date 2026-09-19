---
name: deploy-keeps-binaries-out-of-the-notes-folder
description: The install is bin/ inside the product folder, because the folder itself holds settings, locks and the default note store
scope: repository
type: project
repository: dotnotesmcp
tags:
  - deploy
created: 2026-09-19
updated: 2026-09-19
---
The product folder under LOCALAPPDATA (BinaryVibrance/DotNotes) holds settings.json, locks/ and, by default, notes/ -- the user's actual data.

The published binaries go in bin/ under it, never beside those. An install sharing the folder would put a deploy one careless delete away from destroying notes.

**How to apply:** tools/deploy.ps1 must only ever write under bin/. If the publish target is changed, check it is still a subfolder. See [[ide0005-can-disagree-with-the-compiler]] for the other build-time gotcha here.
