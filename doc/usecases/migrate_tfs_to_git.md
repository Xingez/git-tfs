# Migrate from TFS/TFVC to Git

This guide covers the current git-tfs workflow for moving a TFS/TFVC project
and its history into a Git repository.

> The command flow in this guide follows the current implementation. The
> migration workflow is expected to change when the pending implementation
> work is complete, so update this guide together with that work.

## Prerequisites

Install and verify the following on the Windows machine that performs the
migration:

- Git
- `git-tfs.exe`, available on `PATH`
- .NET Framework 4.8
- A supported Visual Studio/TFS client installation

The executable targets .NET Framework 4.8 because the TFVC client object model
used by Visual Studio 2022 is not compatible with the .NET runtime.

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
  "TargetServer": "https://dev.azure.com/your-organization"
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

Do not put passwords or personal access tokens in `appsettings.json`.

## Authenticate to TFS

By default, git-tfs uses the normal Windows or Azure DevOps credential flow.
For a non-interactive migration, set an Azure DevOps personal access token in
`GIT_TFS_PAT`:

```powershell
$env:GIT_TFS_PAT = 'your-token'
```

Alternatively, use the existing command-line options when required:

```powershell
git tfs clone $/Project/Trunk --username 'DOMAIN\user' --password 'password'
```

Avoid putting credentials in scripts or committing them to a repository.

## Clone the TFS history

The TFS path is supplied after `clone`; the server does not need to be
repeated on the command line:

```powershell
git tfs clone $/Project/Trunk
```

This creates a Git repository in a directory named after the final part of
the TFS path, such as `Trunk`. To migrate all branches, use the trunk path and
`--branches=all`:

```powershell
git tfs clone $/Project/Trunk --branches=all
```

Use the following strategies depending on the source repository:

- `--branches=all` imports all recognized TFS branches and merge changesets.
- `--branches=auto` is the default; it imports the main branch and branches
  discovered through merges.
- `--branches=none` imports only the requested TFS path. Use it when branch
  history is too complex for automatic branch handling.

If the full history is too large or contains unsupported TFS history, try a
bounded migration:

```powershell
git tfs clone $/Project/Trunk --changeset=3245
```

As a last resort, import only the current state without the full history:

```powershell
git tfs quick-clone $/Project/Trunk
```

This creates the repository in a directory named after the TFS path. The
legacy server-first syntax remains supported while the current implementation
is being replaced:

```powershell
git tfs quick-clone <server-url> $/Project/Trunk <destination-folder>
```

Clones are resumable. If a full clone is interrupted, rerun the same command
from the same location and allow it to continue.

### Map TFS users to Git identities

To preserve useful author information, create an authors file with one mapping
per line:

```text
DOMAIN\jane.doe = Jane Doe <jane.doe@example.com>
```

Pass it to `clone`:

```powershell
git tfs clone $/Project/Trunk --branches=all --authors 'C:\migration\authors.txt'
```

### Optional clone settings

For large or unusual repositories, these options may help:

```powershell
git tfs clone $/Project/Trunk `
  --branches=all `
  --batch-size=50 `
  --workspace='C:\w' `
  --gitignore='C:\migration\.gitignore'
```

Use a local drive for the clone. A short `--workspace` path can avoid Windows
path-length problems. A supplied `.gitignore` excludes unwanted files from
the imported tree; review it carefully because ignored content will not be
available in Git.

## Verify the migration

Enter the directory created by `clone` and verify the imported content:

```powershell
cd .\Trunk
git status
git log --all --decorate --oneline
git tfs verify --all
```

Review at least the following before publishing:

- The expected branches and latest changeset are present.
- Important files and folder casing are correct.
- Commit authors and messages are acceptable.
- `git tfs verify --all` reports no content differences.

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
3. Run `git tfs verify --all` again.
4. Push the final branches to the Git server.
5. Give developers the Git repository URL and Git workflow.
6. Make the TFS project read-only or retire it according to the team’s
   retention policy.

After cutover, use Git as the source of truth. Do not continue normal
development in both TFS and Git unless a separate synchronization process has
been agreed upon.
