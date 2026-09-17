# `git tfs quick-clone`

Creates a Git repository from the latest TFS/TFVC changeset without importing
the complete history. Use [clone](clone.md) for a full migration.

## Current syntax

Set `TargetServer` in `appsettings.json`, then run:

```powershell
git tfs quick-clone $/Project/Trunk
```

The repository is created in a directory named after the last part of the TFS
path. To start from a specific changeset:

```powershell
git tfs quick-clone $/Project/Trunk --changeset=3245
```

## Temporary compatibility syntax

The server-first form remains supported while the current implementation is
being replaced:

```powershell
git tfs quick-clone <server-url> <tfs-path> [destination-folder]
```

Use a local drive rather than a network share for the clone. Credentials can
be supplied with `--username` and `--password`, or through the normal
Windows/Azure DevOps credential flow.
