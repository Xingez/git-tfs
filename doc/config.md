# Configuration for migration

## TFS server

The current short form of `git tfs clone` reads the TFS collection URL from
`appsettings.json`:

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
The remaining values are applied automatically by `git tfs clone`:

- `resumable`: keep the output repository so an interrupted clone can resume.
- `api-version`: TFVC REST API version; the default is `7.1`.
- `no-parallel`: serialize requests to TFS.
- `debug`: enable detailed console logging.
- `proxy`: proxy URI for all TFS HTTP(S) requests; null, empty, or `none`
  uses direct connections.

Credentials are not read from this file.

## Git identity

Git-tfs requires a Git user name and email before it creates imported commits:

```powershell
git config --global user.name "Migration User"
git config --global user.email "migration@example.com"
```

## Authentication

Use the normal Windows/Azure DevOps credential flow, or set `GIT_TFS_PAT` for
non-interactive Azure DevOps authentication. Do not commit credentials to
configuration files or scripts.

See [Migrate from TFS/TFVC to Git](usecases/migrate_tfs_to_git.md) for the
complete migration procedure.
