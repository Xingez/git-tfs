# Migrate from TFS/TFVC to Git

This guide covers the current git-tfs workflow for moving a TFS/TFVC project
and its history into a Git repository.

> The supported migration flow is a full REST-based clone of one TFVC
> subfolder. It does not create or use a TFVC workspace.

## Prerequisites

Install and verify the following on the Windows machine that performs the
migration:

- Git
- `git-tfs.exe`, available on `PATH`
- .NET 10 runtime

The executable uses the TFVC REST API to scan changesets, download file
contents, and create Git commits. The bundled legacy helper is started only if
REST cannot retrieve a file through the normal, previous-version, or
rename-source requests. Install the .NET Framework 4.8 runtime if you need
that fallback. Visual Studio and local TFVC workspaces are not required.

Configure the Git identity that will be written to imported commits:

```powershell
git config --global user.name "Migration User"
git config --global user.email "migration@example.com"
```

Create or use an empty destination Git repository before the final push. Do
not migrate directly into a repository that already contains unrelated
commits.

## Configure the TFS server

The short `clone` command reads the TFS collection URL from
`appsettings.json`. The file is copied next to `git-tfs.exe` when the project
is built. Set `TargetServer` to the collection or Azure DevOps organization
that contains the project:

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

For an on-premises TFS collection, use its collection URL instead, for
example:

```json
{
  "TargetServer": "http://tfs:8080/tfs/DefaultCollection"
}
```

The file is searched for in this order:

1. The path in `GIT_TFS_APPSETTINGS`, if set.
2. The directory containing `git-tfs.exe`.
3. The current working directory.

To use a settings file outside the executable directory:

```powershell
$env:GIT_TFS_APPSETTINGS = 'C:\git-tfs\appsettings.json'
```

Environment variables such as `GIT_TFS_TARGET_SERVER`, `GIT_TFS_API_VERSION`,
`GIT_TFS_USERNAME`, `GIT_TFS_PASSWORD`, `GIT_TFS_PAT`, `GIT_TFS_BATCH_SIZE`,
`GIT_TFS_DEBUG`, and `GIT_TFS_PROXY` override their matching configuration
values. Do not commit real credentials in `appsettings.json`.

## Authenticate to TFS

By default, git-tfs uses the normal Windows or Azure DevOps credential flow.
For a non-interactive migration, set an Azure DevOps personal access token in
`GIT_TFS_PAT`:

```powershell
$env:GIT_TFS_PAT = 'your-token'
```

Avoid putting credentials in scripts or committing them to a repository.

## Clone the TFS history

The TFS subfolder and output path are supplied after `clone`; the server does
not need to be repeated on the command line:

```powershell
git tfs $/Project/Trunk C:\migration\Trunk
```

Clones are always resumable. If a full clone is interrupted, rerun the same command
from the same location and allow it to continue.

For file changes that include TFVC hash metadata, an existing local file is
verified before download. Matching files are reused; missing or mismatched
files are downloaded again.

### Map TFS users to Git identities

To preserve useful author information, create an authors file with one mapping
per line:

```text
DOMAIN\jane.doe = Jane Doe <jane.doe@example.com>
```

Pass it to `clone`:

```powershell
git tfs $/Project/Trunk C:\migration\Trunk --authors 'C:\migration\authors.txt'
```

### Clone settings

The clone reads `debug`, `proxy`, `api-version`, and authentication settings
from `appsettings.json` (environment variables override these values). Cloning
is always resumable. The default proxy is disabled. Use a
local drive for the output rather than a network share; no TFVC workspace path
is needed.

The file fallback helper is packaged in the `legacy-history` directory beside
`git-tfs.exe`. It is not started during normal changeset scanning or successful
REST downloads. If it cannot run, a file that REST could not retrieve will
fail; `GIT_TFS_LEGACY_HISTORY` can point to another helper executable. The
completion summary reports whether the helper was used.

## Verify the migration

Enter the directory created by `clone` and verify the imported content:

```powershell
cd .\Trunk
git status
git log --all --decorate --oneline
```

Review at least the following before publishing:

- The expected branches and latest changeset are present.
- Important files and folder casing are correct.
- Commit authors and messages are acceptable.
- `git status` is clean after reviewing the imported files.

Keep the `git-tfs-id` metadata in commit messages unless there is a strong
reason to remove it. It provides an audit trail back to the original TFS
changeset. Removing it is a one-way history rewrite and prevents further
git-tfs synchronization.

## Publish the Git repository

Add the empty destination repository and push all imported branches:

```powershell
git remote add origin https://git.example.com/team/project.git
git push --all origin
```

If the migration created tags, push them as well:

```powershell
git push --tags origin
```

The migration is complete when the destination Git repository contains the
verified branches and history.

## Cut over from TFS to Git

For a final production cutover:

1. Announce a TFS check-in freeze.
2. Fetch or rerun the migration so the last approved TFS changeset is present.
3. Run `git status` and review the final imported history again.
4. Push the final branches to the Git server.
5. Give developers the Git repository URL and Git workflow.
6. Make the TFS project read-only or retire it according to the team’s
   retention policy.

After cutover, use Git as the source of truth. Do not continue normal
development in both TFS and Git unless a separate synchronization process has
been agreed upon.
