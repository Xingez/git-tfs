# git-tfs

This repository is a personal fork of [git-tfs](https://github.com/git-tfs/git-tfs/),
a bridge between TFS/TFVC and Git. The documentation in this fork is focused on
the current workflow for migrating a TFS project and its history to Git.

## Migration guide

Follow [Migrate from TFS/TFVC to Git](doc/usecases/migrate_tfs_to_git.md) for
the complete process, including authentication, verification, and publishing
the result to a Git server.

The main migration workflow is the REST-based full clone described below. A
separate `changeset` command can append one selected changeset to an existing
clone for focused troubleshooting.

## Quick start

1. Install Git and the .NET 10 runtime.
2. Edit `appsettings.json` next to `git-tfs.exe`:

   ```json
   {
     "TargetServer": "https://dev.azure.com/your-organization",
     "api-version": "7.1",
     "pat": "",
     "debug": false,
     "proxy": null
   }
   ```

3. Configure your Git identity:

   ```powershell
   git config --global user.name "Migration User"
   git config --global user.email "migration@example.com"
   ```

4. Clone the TFS subfolder:

   ```powershell
   git tfs $/Project/Trunk C:\migration\Project
   ```

5. Enter the created directory, verify the content, and push it to the empty
   destination Git repository:

   ```powershell
   cd C:\migration\Project
   git status
   git remote add origin https://git.example.com/team/project.git
   git push --all origin
   ```

Normal runs show one changeset list with progress bars and elapsed seconds, plus a
live tree of the output folder on the right (stacked in narrow terminals). The active
changeset and the next fifteen are visible; queued changesets stay at 0% without spinners.
History is counted before importing so the overall bar uses the complete total,
including skipped changesets. The bar animates during scanning.
Redirected output prints changeset IDs and percentage updates.
Short phase labels distinguish scanning, importing, verification, and completion.
Changesets reach 100% after their Git commit is written; partial failures show a cross.
The compact footer shows HTTP attempts (`Req`) and file download attempts (`DL`), each
with its count and latest duration in milliseconds, plus rate budget, throttle and
retry counts. Counts and the folder tree refresh once per second; retry backoff is
excluded from request timings. The tree shows directories with their direct file
count and combined file size, omitting individual files, `.git`, and linked folders.
Folders with no files and a single subfolder collapse into paths such as `src/Core/Import`.
It shows up to 20 directories, fitting fewer in short terminals. Redirected output
prints a final footer.
Retry and throttle counters, the next-attempt countdown, and the last server delay
make API pauses visible. When supplied, `Retry-After` and `X-RateLimit` headers show
the server's delay and remaining rate budget in TSTUs
([Azure DevOps rate-limit documentation](https://learn.microsoft.com/en-us/azure/devops/integrate/concepts/rate-limits)).
With `debug` set to `false`, console logging is disabled and output uses Spectre.
Help, version information, and command errors remain visible.
Use `--debug` to disable the progress display and print full diagnostic logs;
the same behavior can be enabled with `"debug": true` in `appsettings.json`.

### Import one changeset

To isolate a specific TFVC change, use a disposable copy of an existing
git-tfs clone whose `HEAD` represents the state immediately before that
changeset. The command imports only the requested changeset and appends its
commit; it does not scan the intervening history:

```powershell
git tfs changeset $/Project/Trunk C:\migration\Project 12345 --no-fallback
```

The output path must contain an existing git-tfs clone, and the requested
changeset ID must be newer than its current `HEAD` changeset.

Clones are always resumable. Settings can also be supplied through environment
variables, which override matching values in `appsettings.json`:
`GIT_TFS_TARGET_SERVER`, `GIT_TFS_API_VERSION`, `GIT_TFS_PAT`,
`GIT_TFS_BATCH_SIZE`, `GIT_TFS_DEBUG`, and
`GIT_TFS_PROXY`. A null `proxy` disables proxies; set it to an absolute HTTP(S)
proxy URL when your network requires one. The `--no-fallback` option stops on a
REST download error instead of trying the legacy TFVC helper.

Changeset scanning and normal file downloads use REST. The legacy helper
process starts only when REST cannot retrieve a file and fallback is enabled;
the completion summary reports whether it was used.

For HTTP replay and troubleshooting, set `GIT_TFS_HTTP_CAPTURE` to a directory
outside your source checkout. Each run saves numbered exchanges containing
request/response JSON metadata and complete `.body` files, including binary
downloads, HTTP errors, retries, and transport failures. Authentication and
cookie headers are redacted. These captures contain repository content; keep
them local. Capture is disabled by default and applies to REST traffic, not
the legacy TFVC helper's protocol. Use `--no-fallback` for a REST-only run.

After verification, downloaded files receive the TFVC item's `changeDate` as
their local last-write time. Git does not store filesystem timestamps, so this
is preserved in the working tree, not in commits or future checkouts.

### TFS authentication

Git-TFS uses the current Windows credentials by default. For Azure DevOps or
another non-interactive run, provide a personal access token through the
`GIT_TFS_PAT` environment variable. This PowerShell example prompts without
displaying the token:

```powershell
$securePat = Read-Host 'Azure DevOps PAT' -AsSecureString
$env:GIT_TFS_PAT = [System.Net.NetworkCredential]::new('', $securePat).Password
Remove-Variable securePat
try {
    git tfs $/Project/Trunk C:\migration\Project
}
finally {
    Remove-Item Env:GIT_TFS_PAT -ErrorAction SilentlyContinue
}
```

For CI, configure `GIT_TFS_PAT` as a masked secret environment variable. A PAT
can also be set in `appsettings.json`, but avoid committing a file containing
a real token.

The settings file is read from the executable directory or current working
directory. To select a file stored elsewhere, set `GIT_TFS_APPSETTINGS`:

```powershell
$env:GIT_TFS_APPSETTINGS = 'C:\git-tfs\appsettings.json'
```

## Building

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for building

The executable uses the TFVC REST API to scan changesets, download file
contents, and create Git commits. The bundled .NET Framework helper is started
only if REST cannot retrieve a file through its normal, previous-version, and
rename-source requests. The .NET Framework 4.8 runtime is needed only when
that fallback is used; the helper does not create a workspace or require the
Visual Studio IDE.

### Build and test

```powershell
git clone https://github.com/Xingez/git-tfs.git
cd git-tfs\src
dotnet build .\GitTfs.slnx --configuration Release
dotnet test .\GitTfsTest\GitTfsTest.csproj --configuration Release --no-build
```

The built `git-tfs.exe` and its `appsettings.json` are placed in the build
output directory. Add that directory to `PATH` before using the executable.

## Disclaimer

This fork is maintained as a personal project. Parts of the code and
documentation may have been created or updated with the assistance of AI.
Review and test changes carefully before using them with production
repositories or Azure DevOps.
