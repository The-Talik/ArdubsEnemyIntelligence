# Publication workflow

- Whenever the user asks to push the latest version to GitHub, also publish a GitHub release for that version with an installable mod ZIP.
- Use the version in `ArdubsEnemyIntelligence/Info.json` for the release tag (`vVERSION`) and package name. Verify that the project version matches.
- Build and test before publishing. Package only this mod's DLL/PDB, `Info.json`, and the current README and MIT license inside an `ArdubsEnemyIntelligence` folder. Never include game assemblies, local settings, backups, or stale build files.
- Preserve the user's README edits. Do not restore removed content or recreate `TECHNICAL_NOTES.md`.
- Verify the pushed commit, release tag, and uploaded ZIP. If that version already has a release, do not overwrite it without explicit authorization.
