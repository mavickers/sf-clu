# sf-clu

`sf-clu` is a command-line tool for Progress Sitefinity CMS projects. It is a `net10.0` console application that uses System.CommandLine 2.0. It has three commands:

| Command | Purpose |
|---|---|
| `-remote` | Build and serve a Sitefinity site on a Windows build host over SSH |
| `-genkeys` | Generate an RSA key pair |
| `-gensync` | Generate a Sitefinity site-sync configuration |

## Install

With the .NET 10 SDK, install the tool from nuget.org:

```
dotnet tool install -g LightPath.Sitefinity.CLU
dotnet tool update -g LightPath.Sitefinity.CLU
```

Without the .NET SDK, download the archive for your system from the [releases page](https://github.com/mavickers/sf-clu/releases): `sf-clu-<version>-osx-arm64.tar.gz` or `sf-clu-<version>-win-x64.zip`. The binary in the archive runs without .NET. Check the archive against `SHA256SUMS` on the same page. Put the binary in a folder on `PATH`.

The release binaries are not signed. The first run therefore needs one extra step:

- **Windows:** SmartScreen shows a warning. Select "More info", then "Run anyway".
- **macOS:** Gatekeeper blocks a binary that a browser downloaded. Run `xattr -d com.apple.quarantine sf-clu`, or allow it in System Settings > Privacy & Security > Open Anyway. A file downloaded with `curl` has no quarantine attribute and runs directly.

## The `-remote` command

`-remote` lets a developer on macOS or Windows build and serve a Sitefinity web project on a Windows build host. A Sitefinity web project targets .NET Framework and builds only on Windows, so the tool sends the work to the host:

1. It records the working tree of the clone as a git commit (a snapshot) without changing the branch, the index or the stash.
2. It pushes the snapshot over SSH to a mirror repository on the host and checks it out there.
3. It restores packages and builds with MSBuild on the host.
4. IIS on the host serves the web folder of the mirror.

The mirror is a git repository at `<root>\<clone folder name>` on the host. The tool creates it on the first run with `core.autocrlf=false` and `core.eol=lf`. The mirror must have no `origin` remote; if it has one, the run stops with exit code 14.

The source flows in one direction only, from the client to the host. A change made on the host is replaced by the next checkout.

The tool can run from any folder. The profile names the solution, so the current folder has no effect on a run.

### Requirements

Client:

- `git`, `ssh` and `sftp` on `PATH`
- An SSH key, and a `Host` entry in the SSH config file for the build host
- A clone whose `packages/` folder, next to the solution, holds a folder `<Id>.<Version>` for every entry in the `packages.config` of each project that the solution lists. Restore on the client first, for example with `nuget restore <solution>.sln`. Step 1 stops the run when a folder is missing.
- For `--deploy`, a `secrets.xml` for the web project's `<UserSecretsId>`. On Windows the path is `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.xml`. On macOS and Linux it is `~/.microsoft/usersecrets/<UserSecretsId>/secrets.xml`.

Host:

- Windows with OpenSSH Server. If the registry value `HKLM:\SOFTWARE\OpenSSH\DefaultShell` is set, it must be `cmd.exe`.
- The SSH user must be an administrator, because the tool calls `appcmd.exe` and writes into folders that only the pool identity uses.
- Git for Windows.
- Visual Studio 2026 Build Tools (MSBuild 18) with the Web development build tools workload. The tool finds `MSBuild.exe` with `vswhere` for product `Microsoft.VisualStudio.Product.BuildTools`, version range `[18.0,19.0)` and workload `Microsoft.VisualStudio.Workload.WebBuildTools`. The profile key `msbuildPath` replaces this lookup.
- The .NET Framework Developer Pack that the web project targets.
- IIS with ASP.NET 4.x.
- An IIS site and application pool whose root application has the physical path `<root>\<clone folder name>\<web folder>`. The tool never changes the IIS configuration.
- For `--deploy`, an `APPDATA` environment variable on the application pool, or the profile key `secretsPath`.

MSBuild 18 compiles C# 14 by default. A web project that does not compile with C# 14 needs a `LangVersion` setting in the branch that is built, for example in a `Directory.Build.targets` at the clone root.

The host needs no `nuget.exe` and stores no NuGet credential. MSBuild restores the packages (step 7), and the tool copies the private packages from the client first (step 6).

### Host setup

1. Install the software listed under Requirements.
2. Create the folder for the mirror root, for example `D:\builds`. The mirror is created inside it by the first run.
3. Create the IIS site and application pool. Set the physical path of the site's root application to `<root>\<clone folder name>\<web folder>`. The web folder is the folder of the Sitefinity web project, relative to the clone root; `--init` prints it. Use an application pool on .NET 4.x in integrated mode. Give the pool identity read access to the mirror and write access to `App_Data`.
4. For `--deploy`, add an `APPDATA` variable to the pool and give the pool identity read access to that folder:

   ```
   appcmd set apppool "<pool>" /+environmentVariables.[name='APPDATA',value='<folder>']
   ```

5. Verify that `vswhere` finds MSBuild 18 in an SSH session on the host:

   ```
   & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products Microsoft.VisualStudio.Product.BuildTools -version "[18.0,19.0)" -requires Microsoft.Component.MSBuild Microsoft.VisualStudio.Workload.WebBuildTools -find "MSBuild\**\Bin\MSBuild.exe"
   ```

   An empty result means that Build Tools 2026 or its web workload is missing.

The physical path check runs before the mirror exists, so `--init` works against a new host. Each developer needs a separate `root`, or a separate IIS site, on a shared host, because the mirror is also the served folder.

### SSH configuration on the client

The tool runs `ssh` and `sftp` with the profile's `host` value as the target. Add an entry to the SSH config file (`~/.ssh/config` on macOS, `%USERPROFILE%\.ssh\config` on Windows):

```
Host build-host
    HostName <host name or address>
    User <user>
    IdentityFile ~/.ssh/<key>
    ControlMaster auto
    ControlPath ~/.ssh/cm-%r@%h:%p
    ControlPersist 10m
```

The last three lines reuse one SSH connection for the steps of a run. The Windows OpenSSH client does not support them. On Windows, keep the first four lines only; every SSH call then opens a new connection.

### Profiles

A profile describes one site on one build host: the local solution, the host, and the IIS site that serves it. Profiles are stored in `remote.json` in the `sf-clu` folder of the user's local application data folder (`%LOCALAPPDATA%` on Windows, `~/Library/Application Support` on macOS).

Create a profile with `--init`:

```
sf-clu -remote --init name=dev solution=/path/to/clone/MySite.sln host=build-host root='D:\builds' pool=<pool> site=https://<site>/
```

`--init` runs these checks and prints one line for each check that passes. It saves the profile only when every check passes.

1. The solution is inside a git clone and has one Sitefinity web project (see below).
2. SSH to `host` works, and the host's SSH default shell is `cmd.exe`.
3. The pool has one IIS application, and its physical path is `<root>\<clone folder name>\<web folder>`.
4. `msbuildPath` exists on the host, or `vswhere` finds MSBuild 18.

Two conditions give a warning and do not stop the save, because only `--deploy` needs them. A web project without `<UserSecretsId>`: `--deploy` stops until the project has one. A pool without `APPDATA` while the profile has no `secretsPath`: `--deploy` stops in step 3 with exit code 12. `--init` refuses a name that already exists.

| Key | Required | Meaning |
|---|---|---|
| `solution` | yes | Absolute local path of the `.sln` file. |
| `project` | no | Name of the Sitefinity web project in the solution, as the `.sln` lists it. Needed only when the solution has zero or several Sitefinity web projects. |
| `host` | yes | SSH alias of the build host. No spaces, and it must not start with `-`. |
| `root` | yes | Absolute Windows path of the mirror root on the host, for example `D:\builds`. It must not contain `'` `"` `%` `&` `\|` `^` `<` `>`. |
| `pool` | yes | Name of the IIS application pool. |
| `site` | yes | `http` or `https` URL of the site. |
| `msbuild` | no | Extra MSBuild arguments, separated by spaces. Double quotes group words. It must not set `OutDir`, because a Clean can delete the output folder. |
| `msbuildPath` | no | Full path of `MSBuild.exe` on the host. Replaces the `vswhere` lookup. The tool does not check its version; it must be MSBuild 16.5 or later, because step 7 uses `RestorePackagesConfig`. |
| `secretsPath` | no | Full path of `secrets.xml` on the host (the file, not its folder). Without it, a `--deploy` reads the `APPDATA` environment variable of the pool on the host. |

Key names are not case sensitive. A value is everything after the first `=`. Bash and zsh remove a backslash, so quote Windows paths: `root='D:\builds'`.

| Command | Effect |
|---|---|
| `sf-clu -remote --init name=<name> key=value ...` | Creates a profile after the checks above. `name` may contain letters, digits, `.`, `_` and `-`. |
| `sf-clu -remote --profile name=<name> key=value ...` | Updates a profile. It runs the same checks as `--init` and saves only when they pass. |
| `sf-clu -remote --profiles` | Lists the profiles, marks the default, and shows the secret names of each profile. |
| `profile=<name>` on a job | Runs the job with that profile. Without it, the job uses the default profile. |
| `key=value` on a job | Overrides that profile key for this run only. Nothing is saved. The `--init` checks do not run for the override; only step 3 checks the host. |

The first saved profile becomes the default. `default=true` on `--init` or `--profile` makes that profile the default. An empty value (`msbuild=`) clears an optional key. An unknown key is an error that lists the valid keys.

Every job first prints the profile name, the full path of the solution and the web project, each on its own line (`Profile:`, `Solution:`, `Project:`). A job started from inside another clone still builds the profile's solution.

A profile without `solution` stops every job with a usage error. Add the key with `--profile`; the profile does not have to be created again:

```
sf-clu -remote --profile name=dev solution=/path/to/clone/MySite.sln
```

#### How the Sitefinity web project is found

The tool reads the `.sln` and selects each C# project (`.csproj`) that meets both conditions:

- It imports `Microsoft.WebApplication.targets`, so it is a web application project.
- It references Sitefinity: its `packages.config` has a `Telerik.Sitefinity*` or `Progress.Sitefinity*` package, or the project has a `<Reference>` to a `Telerik.Sitefinity` assembly.

Class libraries that reference Sitefinity do not count. Zero or several matches stop the run with a list of the candidates, unless `project=` names the project. A project named with `project=` is used even when it fails a condition; the run then prints a warning that names the failed condition.

### Derived values

Every run (and every `--init` and `--profile`) reads the solution, the web project and its `Web.config` again from the working tree of the clone. Nothing derived is stored in the profile, so a renamed web project or a changed key on a branch takes effect on the next run.

| Value | Source |
|---|---|
| Clone root | `git rev-parse --show-toplevel` in the solution's folder. The mirror name is the folder name of the clone root. The solution may be in any folder of the clone. |
| Web folder | The folder of the Sitefinity web project, relative to the clone root |
| Search exclusion | `<web folder>/App_Data/Sitefinity/Search`, left out of the snapshot |
| Secrets ID | The web project's `<UserSecretsId>` |
| Health path | `sf:HealthCheckApiEndpoint` in the `<appSettings>` of the web project's `Web.config`. Sitefinity has no default health path. |
| Startup path | `sf:appStatusUrl` in the same `<appSettings>`, or Sitefinity's default `/sitefinity/status` |

`packages/` is read from the solution's folder. The `NuGet.Config` is the first one in the solution's folder or a parent folder up to the clone root; the name match is not case sensitive. Only the `packages.config` files of the projects that the solution lists are checked.

The tool reads `Web.config` in the clone, not on the host. A key changed after the last build is therefore used against the previous build. A value that a configuration builder sets at runtime is not seen.

### Secrets

A `--deploy` delivers `secrets.xml` to the host. A profile can hold overrides: entries that replace the entry of the same name in the client's `secrets.xml`, or are added to it. The comparison of names is not case sensitive. Use overrides for values that must differ on the host.

```
sf-clu -remote --add-secret profile=dev key=<entry name> value=<value> key=<entry name> value=<value>
sf-clu -remote --remove-secret profile=dev key=<entry name>
```

- A command can hold several `--add-secret` and `--remove-secret` groups. Each group needs one `profile=` and at least one `key=`.
- In an `--add-secret` group, each `key=` is followed directly by its `value=`. A `--remove-secret` group has no `value=`.
- An entry name can contain colons. A `value=` with nothing after it sets an empty string.
- Overrides are stored in `remote.json` encrypted with AES-GCM under a key derived from an identifier of the machine. A profile with overrides therefore cannot be copied to another machine. If an override does not decrypt, `--deploy` stops in step 1 and prints the `--add-secret` command that sets it again. This protects the value from being read in the file; it is not a security boundary, because any process on the machine can read the identifier.
- Error messages never print a `value=` token.

### Jobs

Give exactly one job marker.

| Job | Effect |
|---|---|
| `--build` | Takes the site offline, sends a snapshot, restores and builds on the host, and brings the site back. |
| `--rebuild` | `--build` with a Clean between the restore and the build. |
| `--deploy` | `--build`, then delivers `secrets.xml`, then waits until Sitefinity has started. |
| `--recycle` | Recycles the application pool, if the pool is started. |
| `--status` | Reports the pool state, the result of the health path, a running job and the offline page. |

`--init`, `--profile`, `--profiles`, `--add-secret` and `--remove-secret` are not jobs and cannot be combined with a job marker. `--verbose` is accepted with every command.

### Steps of a build job

A build job (`--build`, `--rebuild`, `--deploy`) runs these steps and stops at the first failure. The output shows `-- step N (name)` before each step.

1. **Checks (client).** Checks that `packages/` has a folder for every entry in the `packages.config` of each project that the solution lists. Finds the private packages: those that the package source mapping in `NuGet.Config` sends to no nuget.org source. For `--deploy`, merges `secrets.xml` with the profile's overrides.
2. **Snapshot (client).** Commits the working tree as it is on disk, with the current `HEAD` as the parent. The search exclusion `<web folder>/App_Data/Sitefinity/Search` is left out of the snapshot, so the search indexes on the host remain.
3. **Host check (host).** Checks the SSH default shell, that the pool's application serves the mirror's web folder, and for `--deploy` the pool's `APPDATA`. Creates the mirror on the first run. Takes the run lock. No site change happens before this step succeeds.
4. **Push.** Force-pushes the snapshot to the ref `refs/clu/snapshot` of the mirror. The site keeps serving the previous build during the push.
5. **Checkout (host).** Writes `app_offline.htm` into the web folder, removes a leftover `.git\index.lock`, and runs `git checkout --force --detach` on the snapshot. It prints the tracked files that the checkout discards and the Sitefinity configuration files that exist only on the host.
6. **Private packages.** Copies the private package folders that the mirror lacks, with `sftp`. Each folder goes to a temporary name and is renamed when complete. If none is missing, no connection is made.
7. **Restore (host).** Runs `MSBuild.exe <solution> /t:Restore /p:RestorePackagesConfig=true` with the MSBuild that step 8 uses, so `packages.config` projects are restored too. It adds `/p:RestoreConfigFile` with the `NuGet.Config` described under Derived values, when one exists. A package folder that step 6 copied is already installed, so the restore does not request it from a feed. NuGet audit is turned off on the host only. For `--rebuild`, step 7b runs MSBuild with `/t:Clean` and the same arguments as the build.
8. **Build (host).** Runs `MSBuild.exe <solution> /p:Configuration=Debug /p:DebugType=full /m /v:m /nologo /clp:Summary` and the profile's `msbuild` arguments. The error and warning counts come from the summary lines of MSBuild. For `--deploy`, step 8b writes the merged `secrets.xml` to `<APPDATA>\Microsoft\UserSecrets\<UserSecretsId>\secrets.xml`, or to `secretsPath`.
9. **Finish (host).** After a success, deletes `app_offline.htm`. After a failure from step 5 onward, replaces it with a page that names the failed step. Releases the run lock. This step runs after a failure or a cancel too.
10. **Startup (client, `--deploy` only).** Requests the profile's `site` every 2 seconds, for at most 10 minutes. A response of HTTP 503, or a redirect to the startup path, counts as "still starting". A redirect to another scheme or server is followed once. Any other response ends the wait: a status below 500 is success, 500 or above is a failure. A certificate validation error is a failure.

The host scripts are PowerShell scripts sent over standard input to `powershell` on the host, one SSH call per step.

A final line shows the result, for example `sf-clu -remote --build succeeded: 0 error(s), 120 warning(s), 3:41 elapsed`.

### `--recycle` and `--status`

Both jobs run the host check without the run lock, then the pool job:

- `--recycle` recycles the pool if its state is `Started`. If the pool is not started, it prints the `appcmd start apppool` command and exits with code 1.
- `--status` prints the pool state and requests the health path. It expects HTTP 200, reads the `Healthy` value in the response body and lists the failed checks. A site that sets `sf:HealthCheckUnhealthyStatusCode` to 200 answers 200 also when it is unhealthy, so the body is the verdict. Any other status code counts as no verdict. The exit code is 1 when the site is unhealthy, gives no verdict, or the pool is not started.

When the web project sets no health path, `--status` prints `Healthy: not configured`, and health has no effect on the exit code. The health service also needs `healthCheckConfig enabled="True"` in Sitefinity's `SystemConfig.config`. The tool does not read that file, so a health path with the service turned off gives no verdict and exit code 1.

Both jobs also report a running job (the holder of the run lock) and an existing `app_offline.htm`.

### Run lock

Step 3 creates the file `<mirror>\.git\clu.lock` with an exclusive create. The file holds the machine name and process ID of the client, the job and the start time. Step 9 deletes it. Every host step refreshes the time of the file, so a long build does not look like a leftover lock. The push (step 4) and the package copy (step 6) run no host script and do not refresh it.

If the lock exists, the run stops with exit code 13 and names the holder. A lock counts as left over, and the new run takes it over, in these cases:

- It is older than 60 minutes (`LockMaxAgeMinutes` in `RemoteCommand/Properties.cs`).
- Its client machine name is the current machine and its process no longer runs.

If another run holds a lock that is not left over, delete `<mirror>\.git\clu.lock` on the host only when that run no longer exists.

### The offline page and Ctrl+C

While `app_offline.htm` exists in the web folder, ASP.NET keeps the application unloaded and every request gets the page. After a failed run, the file remains and describes the failure, so a browser shows why the site is down. The next successful run removes it. A failure in steps 1 to 4 does not write the page; if an earlier failed run left a page, it remains.

Ctrl+C stops the run after the current step. Step 9 still runs to release the lock. The exit code is 130.

### Exit codes

A failed step prints `step N (name): message` to the error stream and returns an exit code. `--init` and `--profile` report a failed host check as `step 3 (host check)`.

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Usage error, a failed client-side check (for example a solution without one Sitefinity web project, missing package folders or `secrets.xml`), a site that did not start after `--deploy`, an unhealthy site or a stopped pool, or an unexpected error |
| 10 | The SSH default shell of the host is not `cmd.exe` |
| 11 | The pool has no single IIS application with a root virtual directory, or its physical path is not the mirror's web folder |
| 12 | `--deploy` needs `secrets.xml`, but the pool has no `APPDATA` variable and the profile has no `secretsPath` |
| 13 | Another run holds the run lock |
| 14 | The mirror folder has an `origin` remote, so it is a clone and not a mirror created by the tool |
| 20 | MSBuild was not found on the host: `vswhere` found no matching Build Tools, or `msbuildPath` does not exist (`--init`, `--profile`) |
| 127 | A program (`git`, `ssh`, `sftp`) could not be started on the client |
| 130 | The run was cancelled with Ctrl+C |
| 255 | `ssh` failed: key, alias or network |

Any other code is the exit code of the failed command: MSBuild, `git`, `sftp`, or the host script.

### Output and `--verbose`

The tool prints the output of each step as it arrives. The mirror path is removed from host output lines, so a reported file path is relative to the clone root. Errors are printed in red.

`--verbose` prints each command that the tool runs on the client (`> ssh ...`, `> git ...`) and the time of each step. It never prints the host scripts.

## `-genkeys`

`sf-clu -genkeys` takes no options. It creates an RSA key pair (`RSACryptoServiceProvider`) and prints to the standard output:

1. The private key as XML, then the same XML encoded as Base64.
2. The public key as XML, then the same XML encoded as Base64.

The tool does not save the keys. The keys authenticate API calls.

## `-gensync`

`sf-clu -gensync` prints a site-sync configuration as JSON to the standard output. The JSON has three parts: `configs` (the `auth` configuration and one configuration per target, each encoded as Base64), `auth` and `targets`.

```
sf-clu -gensync [--sitekey <1-250>] [--secret <text>] [--verbose] [--target hostname=<host> [name=<name>] [secret=<text>] [sitekey=<1-250>] [id=<guid>]] ...
```

| Option | Default | Rule |
|---|---|---|
| `--sitekey` | 20 | Integer from 1 to 250. The site key of this site. |
| `--secret` | 64 random characters | At least 16 characters. |
| `--verbose` | off | Prints the stack trace of an error. |

Each `--target` starts a group of `key=value` tokens:

| Key | Default | Rule |
|---|---|---|
| `hostname` | none | Required. A domain name. The output `host` is `https://<hostname>`. |
| `name` | the host name | Name of the target. |
| `secret` | random | At least 16 characters. When no target gives a secret, all targets share one random secret. Otherwise, each target without a secret gets its own random secret. |
| `sitekey` | 50 | Integer from 1 to 250, greater than `--sitekey`. |
| `id` | random GUID | Target IDs must be unique. |

An invalid value prints an error and returns exit code 1.

## Building from source

Build with `dotnet build` at the repository root. The binary is `LightPath.Sitefinity.CLU/bin/Debug/net10.0/sf-clu`. `LightPath.Sitefinity.CLU/Properties/launchSettings.json` has IDE launch profiles for the five `-remote` jobs; they pass `profile=dev`, so change that to the name of your profile.

`dotnet pack -c Release` creates the `dotnet tool` package. The publish profiles `osx-arm64` and `win-x64` in `Properties/PublishProfiles` create the self-contained single-file binaries:

```
dotnet publish LightPath.Sitefinity.CLU -p:PublishProfile=osx-arm64
```

## Releasing

Work happens on `develop`. A pull request from `develop` into `main` releases: every merge to `main` publishes a new version. There is no manual publish step and no tag to push.

`.github/workflows/ci.yml` builds and packs on every push to `develop` and every pull request into `develop` or `main`. It publishes nothing. A pull request into `main` cannot be merged until this check passes.

`.github/workflows/release.yml` runs on each push to `main`:

1. Nerdbank.GitVersioning computes the version. The major and minor parts come from the `version` field in `version.json`. The patch part is the number of commits since that field last changed, so patch numbers can skip values. Only builds from `main` get a clean version number; builds from other branches get a `-g<sha>` prerelease suffix.
2. The publish profiles create the two self-contained archives, and `dotnet pack` creates the `dotnet tool` package.
3. `NuGet/login` exchanges the workflow's GitHub OIDC token for a nuget.org key that is valid for one hour. This is NuGet Trusted Publishing; the repository holds no NuGet API key.
4. `dotnet nuget push` uploads the package. A version that already exists on nuget.org is skipped, so a rerun of a failed run does not fail on it.
5. A GitHub Release named with the version (for example `1.0.3`) is created with both archives and `SHA256SUMS`. Creating the release also creates the git tag.

A manual run of `release.yml` from another branch builds the archives and the package as workflow artifacts and publishes nothing. Use it before merging a change to the publish settings, because `ci.yml` does not run the self-contained builds.

The Trusted Publishing policy on nuget.org is bound to the repository owner, the repository name, the workflow file name and the package ID. Renaming the repository or `release.yml` stops the push until the policy is changed to match.

To change the major or minor version, edit the `version` field in `version.json` in the same pull request as the change that needs it.

## License

MIT. See [LICENSE](LICENSE).
