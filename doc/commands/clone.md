# `git tfs clone`

Creates a Git repository from a TFS/TFVC path and imports its changeset
history. A bundled legacy TFVC helper asks the server for recursive history of
the selected subfolder, while the main process uses the TFVC REST API to
download each changeset's file content and create the corresponding Git commit
without a TFVC workspace. For the complete migration process, see
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
git tfs clone $/Project/Trunk C:\migration\Project
```

The server and the values in `appsettings.json` are applied automatically.
The command intentionally has only two positional arguments: the TFVC
subfolder and the Git output path.

Clones are resumable. If a clone is interrupted, rerun the same command from
the same location.

When TFVC supplies a file hash, the clone compares it with an existing local
file and reuses the file when it matches. A missing or mismatched file is
downloaded again, so rerunning a clone can repair incomplete or modified
output safely.

Use a local drive for the clone rather than a network share. No TFVC workspace
path is needed.

The recursive-history helper is bundled beside the executable and requires the
.NET Framework 4.8 runtime, but not the Visual Studio IDE. If it is unavailable
the clone uses a slower REST-only project history scan as a fallback. Set
`GIT_TFS_LEGACY_HISTORY` to an alternate helper executable when troubleshooting
or packaging the application.
