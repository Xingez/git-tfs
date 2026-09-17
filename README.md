# git-tfs

This repository is a personal fork of [git-tfs](https://github.com/git-tfs/git-tfs/),
a bridge between TFS/TFVC and Git. The documentation in this fork is focused on
the current workflow for migrating a TFS project and its history to Git.

## Migration guide

Follow [Migrate from TFS/TFVC to Git](doc/usecases/migrate_tfs_to_git.md) for
the complete process, including authentication, verification, and publishing
the result to a Git server.

The guide follows the current implementation: the supported executable flow is
the REST-based full clone described below.

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
   git tfs clone $/Project/Trunk C:\migration\Project
   ```

5. Enter the created directory, verify the content, and push it to the empty
   destination Git repository:

   ```powershell
   cd C:\migration\Project
   git status
   git remote add origin https://git.example.com/team/project.git
   git push --all origin
   ```

For a settings file stored elsewhere, set `GIT_TFS_APPSETTINGS` before running
git-tfs. Credentials are not stored in `appsettings.json`; see the migration
guide for the supported authentication options.

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
