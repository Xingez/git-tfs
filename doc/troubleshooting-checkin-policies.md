Checkin policies require Visual Studio 2022 and the `enable_checkin_policies_support.bat`
setup step. The setup runs `git-tfs.exe` as a 32-bit process so it can read Visual Studio's
private registry hive.

If a policy fails with an error such as:

```
[ERROR] Policy: Internal error in Changeset Comments Policy.
```

first verify that the same policy works during a normal TFS check-in from Visual Studio 2022.

## Verify the selected TFS client

`git-tfs` uses the Visual Studio 2022 client library. To select it explicitly, set:

```
set GIT_TFS_CLIENT=2022
```

Then open a new command prompt and run `git tfs info`. The output should include:

```
Supported version: 2022
```

## Verify the private registry hive

Visual Studio stores check-in policy registration in a private registry hive. The hive is
located below:

```
C:\Users\<Username>\AppData\Local\Microsoft\VisualStudio\<VSMajor_Suffix>\privateregistry.bin
```

For Visual Studio 2022, `<VSMajor_Suffix>` starts with `17.0`. Close all Visual Studio
instances before loading the hive in `regedit.exe`, and unload it again when finished.

The check-in policy entries are under:

```
Software\Microsoft\VisualStudio\<VSMajor_Suffix>_Config\TeamFoundation\SourceControl\Checkin Policies
```

The registered policy assemblies must match the Visual Studio 2022 installation used by
`git-tfs`.
