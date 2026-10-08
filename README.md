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
     "resumable": true,
     "no-parallel": true,
     "debug": true,
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

The settings control the REST API version, whether a clone can resume, request
parallelism, debug logging, and proxy use. A null `proxy` disables proxies; set
it to an absolute HTTP(S) proxy URL when your network requires one. The
`--no-fallback` option stops on a REST download error instead of trying the
legacy TFVC helper.

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

For CI, configure `GIT_TFS_PAT` as a masked secret environment variable. Do
not put the token in `appsettings.json` or commit it to a script.

The settings file is read from the executable directory or current working
directory. To select a file stored elsewhere, set `GIT_TFS_APPSETTINGS`:

```powershell
$env:GIT_TFS_APPSETTINGS = 'C:\git-tfs\appsettings.json'
```

## Building

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for building

The executable uses the TFVC REST API for file downloads and Git commits. A
bundled .NET Framework helper uses the legacy TFVC client object model only to
ask the server for recursive history of the selected subfolder; it does not
create a workspace or require the Visual Studio IDE. The machine must have
the .NET Framework 4.8 runtime available for that helper.

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
