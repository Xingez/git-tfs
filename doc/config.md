# Configuration for migration

## TFS server

The default `git tfs` workflow reads the TFS collection URL from
`appsettings.json`:

```json
{
  "TargetServer": "https://dev.azure.com/your-organization",
  "api-version": "7.1",
  "Username": "",
  "Password": "",
  "pat": "",
  "debug": false,
  "proxy": null
}
```

For an on-premises installation, use the collection URL, for example
`http://tfs:8080/tfs/DefaultCollection`.

The file is loaded from the first existing location in this order:

1. The file named by `GIT_TFS_APPSETTINGS`.
2. `appsettings.json` beside `git-tfs.exe`.
3. `appsettings.json` in the current directory.

Example:

```powershell
$env:GIT_TFS_APPSETTINGS = 'C:\git-tfs\appsettings.json'
```

`TargetServer` is trimmed and trailing slashes are removed when it is loaded.
Non-empty environment variables override matching settings in the file:

- `GIT_TFS_TARGET_SERVER`: overrides `TargetServer`.
- `GIT_TFS_API_VERSION`: overrides `api-version` (default `7.1`).
- `GIT_TFS_USERNAME` and `GIT_TFS_PASSWORD`: override the credentials.
- `GIT_TFS_PAT`: overrides `pat`; prefer the environment variable for secrets.
- `GIT_TFS_BATCH_SIZE`: overrides the optional `batch-size` setting.
- `GIT_TFS_DEBUG`: overrides `debug`; true enables detailed logging and disables Spectre progress.
- `GIT_TFS_PROXY`: overrides `proxy`; null, empty, or `none` uses direct connections.

Clones are always resumable. No setting can disable resuming.

## Git identity

Git-tfs requires a Git user name and email before it creates imported commits:

```powershell
git config --global user.name "Migration User"
git config --global user.email "migration@example.com"
```

## Authentication

Use the normal Windows/Azure DevOps credential flow, set `pat` in this file,
or set `GIT_TFS_PAT` for non-interactive authentication. The environment
variable overrides the file value. Do not commit real credentials to
configuration files or scripts.

See [Migrate from TFS/TFVC to Git](usecases/migrate_tfs_to_git.md) for the
complete migration procedure.
