# `git tfs clone`

Creates a Git repository from a TFS/TFVC path and imports its changeset
history. For the complete migration process, see
[Migrate from TFS/TFVC to Git](../usecases/migrate_tfs_to_git.md).

## Current syntax

Configure the server in `appsettings.json` first:

```json
{
  "TargetServer": "https://dev.azure.com/your-organization"
}
```

Then pass the TFS path:

```powershell
git tfs clone $/Project/Trunk
```

The repository is created in a directory named after the last part of the TFS
path. To migrate all recognized branches:

```powershell
git tfs clone $/Project/Trunk --branches=all
```

## Temporary compatibility syntax

The server-first form remains supported while the current implementation is
being replaced:

```powershell
git tfs clone <server-url> <tfs-path> [destination-folder]
```

## Branch strategies

- `--branches=all` imports all recognized branches and merge changesets.
- `--branches=auto` is the default and follows branches discovered through
  merges.
- `--branches=none` imports only the requested path and ignores branch merges.

Use the trunk path with `--branches=all` for a normal full migration. If the
TFS branch history is too complex, retry with `--branches=none`.

## Useful options

```text
--changeset=N             Start importing at changeset N
--up-to=N                 Stop importing at changeset N
--branches=STRATEGY       all, auto, or none
--authors=FILE             Map TFS users to Git identities
--gitignore=FILE           Exclude files using a .gitignore template
--ignore-regex=REGEX       Exclude matching TFS paths
--batch-size=N             Change the number of changesets fetched per batch
--workspace=PATH           Use a short local TFS workspace path
--username=USER            TFS username when required
--password=PASSWORD        TFS password when required
--no-parallel              Serialize TFS requests (the safe default)
```

Clones are resumable. If a clone is interrupted, rerun the same command from
the same location.

Use a local drive for the clone rather than a network share. A short
`--workspace` path can help with Windows path-length limits.
