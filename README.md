# git-tfs

This repository is a personal fork of [git-tfs](https://github.com/git-tfs/git-tfs/),
a bridge between TFS/TFVC and Git. The documentation in this fork is focused on
the current workflow for migrating a TFS project and its history to Git.

## Migration guide

Follow [Migrate from TFS/TFVC to Git](doc/usecases/migrate_tfs_to_git.md) for
the complete process, including authentication, branch handling, verification,
and publishing the result to a Git server.

The guide follows the current implementation. The command flow is expected to
change when the pending implementation work is complete, so keep the guide in
sync with that work.

## Quick start

1. Install Git, `git-tfs.exe`, the .NET 10 Desktop Runtime, and a supported
   Visual Studio/TFS client installation.
2. Edit `appsettings.json` next to `git-tfs.exe`:

   ```json
   {
     "TargetServer": "https://dev.azure.com/your-organization"
   }
   ```

3. Configure your Git identity:

   ```powershell
   git config --global user.name "Migration User"
   git config --global user.email "migration@example.com"
   ```

4. Clone the TFS trunk. Add `--branches=all` when all recognized branches
   should be migrated:

   ```powershell
   git tfs clone $/Project/Trunk --branches=all
   ```

5. Enter the created directory, verify the content, and push it to the empty
   destination Git repository:

   ```powershell
   cd .\Trunk
   git tfs verify --all
   git remote add origin https://git.example.com/team/project.git
   git push --all origin
   ```

For a settings file stored elsewhere, set `GIT_TFS_APPSETTINGS` before running
git-tfs. Credentials are not stored in `appsettings.json`; see the migration
guide for the supported authentication options.

## Building

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022 with the required TFS client tooling

### Build and test

```powershell
git clone https://github.com/Xingez/git-tfs.git
cd git-tfs\src
dotnet build .\GitTfs.sln --configuration Release
dotnet test .\GitTfsTest\GitTfsTest.csproj --configuration Release --no-build
```

The built `git-tfs.exe` and its `appsettings.json` are placed in the build
output directory. Add that directory to `PATH` before using the executable.

## Disclaimer

This fork is maintained as a personal project. Parts of the code and
documentation may have been created or updated with the assistance of AI.
Review and test changes carefully before using them with production
repositories or Azure DevOps.
