# Updates and release

Current-state guide — see [review baseline](../architecture.md).

Separate Updater entry point delegates installation to Infrastructure; discovery and coordination are in Application. Signed manifests, package and file hashes, safe extraction, installation recovery and rollback protect updates. Portable Windows x64 packaging and existing local-test labels distinguish development artifacts from published releases. Never modify published artifacts, include user data or expose signing keys. Consult latest release records rather than historical version references.

## Evidence and detailed implementation records

- [phase23d-github-updater.md](../phase23d-github-updater.md)
- [release-strategy.md](../release-strategy.md)
- [release-manifest.md](../release-manifest.md)
- [releases/rc14-release-readiness.md](../releases/rc14-release-readiness.md) (latest published release record at the review baseline)
- [releases/rc15-release-preparation.md](../releases/rc15-release-preparation.md) (RC.15 published 2026-11-28 as a prerelease, §21; updater, signing tool, trusted key and manifest format unchanged since RC.14)
