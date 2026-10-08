# `git tfs` (clone workflow)

Creates a Git repository from a TFS/TFVC path and imports its changeset
history. The main process uses the TFVC REST API to scan changesets and
download file content, then creates the corresponding Git commits without a
TFVC workspace. A bundled legacy TFVC helper is started only when the REST
content, previous-version, and rename-source downloads all fail for a file.
For the complete migration process, see
[Migrate from TFS/TFVC to Git](../usecases/migrate_tfs_to_git.md).

## Syntax

Configure the server in `appsettings.json` first:

```json
{
  "TargetServer": "https://dev.azure.com/your-organization",
  "api-version": "7.1",
  "resumable": true,
  "no-parallel": true,
  "debug": true,
  "proxy": null
}
```

Then pass both the TFS subfolder and the output path:

```powershell
git tfs $/Project/Trunk C:\migration\Project
```

The server and the values in `appsettings.json` are applied automatically.
The default workflow accepts the TFVC subfolder and Git output path, followed
optionally by a target Git URL and target branch.

Clones are resumable. If a clone is interrupted, rerun the same command from
the same location.

When TFVC supplies a file hash, the clone compares it with an existing local
file and reuses the file when it matches. A missing or mismatched file is
downloaded again, so rerunning a clone can repair incomplete or modified
output safely.

Source-side rename records are metadata for the old path, not downloadable
file content, and are skipped.

Use a local drive for the clone rather than a network share. No TFVC workspace
path is needed.

The file fallback helper is bundled beside the executable. The .NET Framework
4.8 runtime is required only if that fallback is activated; Visual Studio is
not required. If the helper is unavailable, normal REST cloning still works,
but a file that REST cannot retrieve will fail. Set `GIT_TFS_LEGACY_HISTORY`
to an alternate helper executable when troubleshooting or packaging the
application. The completion summary reports whether the helper was used.
