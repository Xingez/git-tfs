# `git tfs clone`

Creates a Git repository from a TFS/TFVC path and imports its changeset
history. The clone uses the TFVC REST API directly: it downloads each
changeset's file content and creates the corresponding Git commit without a
TFVC workspace. For the complete migration process, see
[Migrate from TFS/TFVC to Git](../usecases/migrate_tfs_to_git.md).

## Syntax

Configure the server in `appsettings.json` first:

```json
{
  "TargetServer": "https://dev.azure.com/your-organization",
  "api-version": "7.1",
  "resumable": true,
  "batch-size": 1,
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

Use a local drive for the clone rather than a network share. No TFVC workspace
path is needed.
