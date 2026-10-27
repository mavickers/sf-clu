using System.Net;
using System.Text;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        /// <summary>The MSBuild step for <c>--build</c>, or the Clean of <c>--rebuild</c>; both use the same configuration so the Clean removes the build's outputs.</summary>
        private static string BuildScript(RunContext context, bool clean)
        {
            var extra = string.Join(", ", SplitArguments(context.Profile.Msbuild).Select(argument => $"(D '{B64(argument)}')"));

            return ScriptPrologue + MsbuildPrologue(context) + $$"""
                $extra = @({{extra}})
                & $msbuild (D '{{B64(WindowsPath(context.Source.Solution))}}') /p:Configuration=Debug /p:DebugType=full /m /v:m /nologo /clp:Summary @extra{{(clean ? " /t:Clean" : string.Empty)}}
                if ($LASTEXITCODE -eq $null) { exit 1 }
                exit $LASTEXITCODE
                """;
        }

        /// <summary>Step 5: offline page, the list of host changes the checkout discards, the checkout, and the private package folders the mirror lacks.</summary>
        private static string CheckoutScript(RunContext context, string sha, IEnumerable<string> privatePackages) => ScriptPrologue + TouchLock(context) + $$"""
            $mirror = D '{{B64(context.Mirror)}}'
            Set-Location -LiteralPath $mirror
            $web = D '{{B64(HostWebFolder(context))}}'
            $indexLock = Join-Path $mirror '.git\index.lock'
            if (Test-Path -LiteralPath $indexLock) { Remove-Item -LiteralPath $indexLock -Force; M 'indexlock' '' }
            [IO.Directory]::CreateDirectory($web) | Out-Null
            [IO.File]::WriteAllBytes((Join-Path $web 'app_offline.htm'), [Convert]::FromBase64String('{{B64(OfflinePage("Build in progress", $"sf-clu -remote {context.Job} started at {context.Started:yyyy-MM-dd HH:mm} on {Environment.MachineName}."))}}'))
            M 'offline' ''
            $tracked = @(& git status --porcelain --untracked-files=no)
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            foreach ($line in $tracked) { M 'tracked' $line }
            $config = @(& git status --porcelain --untracked-files=all -- (D '{{B64(WebPath(context, "App_Data/Sitefinity/Configuration"))}}'))
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            foreach ($line in $config) { if ($line.StartsWith('??')) { M 'hostonly' $line } }
            & git -c advice.detachedHead=false checkout --quiet --force --detach {{sha}}
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            $packages = Join-Path $mirror (D '{{B64(WindowsPath(SolutionPath(context, "packages")))}}')
            if (Test-Path -LiteralPath $packages) { Get-ChildItem -LiteralPath $packages -Directory -Filter '*.clu-partial-*' | Remove-Item -Recurse -Force }
            foreach ($name in (D '{{B64(string.Join("\n", privatePackages))}}').Split("`n")) { if ($name -and -not (Test-Path -LiteralPath (Join-Path $packages $name))) { M 'missing' $name } }
            exit 0
            """;

        /// <summary>Step 9: on success remove the offline page; on a failure after step 5 wrote it, replace it with the failure; release the lock only if it is this run's.</summary>
        private static string FinishScript(RunContext context, bool success, string? failurePage)
        {
            var script = new StringBuilder(ScriptPrologue);

            script.Append($$"""
                $mirror = D '{{B64(context.Mirror)}}'
                $offline = Join-Path (D '{{B64(HostWebFolder(context))}}') 'app_offline.htm'

                """);

            if (success) script.Append("if (Test-Path -LiteralPath $offline) { Remove-Item -LiteralPath $offline -Force }\n");
            if (failurePage != null) script.Append($"[IO.File]::WriteAllBytes($offline, [Convert]::FromBase64String('{B64(failurePage)}'))\n");

            return script.Append($$"""
                $lock = Join-Path $mirror '.git\clu.lock'
                $held = ''
                if (Test-Path -LiteralPath $lock) { $held = [IO.File]::ReadAllText($lock) }
                if ($held -ceq (D '{{B64(context.LockContent)}}')) { Remove-Item -LiteralPath $lock -Force } elseif ($held) { M 'otherlock' $held }
                exit 0
                """).ToString();
        }

        /// <summary>Sets $msbuild to the profile's msbuildPath, or to what vswhere finds; empty when vswhere finds no matching Build Tools.</summary>
        private static string MsbuildLookup(RunContext context) => $$"""
            $msbuild = D '{{B64(context.Profile.MsbuildPath ?? string.Empty)}}'
            if (-not $msbuild) {
                $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
                if (Test-Path -LiteralPath $vswhere) { $msbuild = & $vswhere -latest -products {{VsWhereProduct}} -version '{{VsWhereVersionRange}}' -requires Microsoft.Component.MSBuild {{VsWhereWorkload}} -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1 }
            }

            """;

        /// <summary>Finds MSBuild with vswhere unless the profile gives msbuildPath; exit code 20 means no matching Build Tools.</summary>
        private static string MsbuildPrologue(RunContext context) => TouchLock(context) + $$"""
            Set-Location -LiteralPath (D '{{B64(context.Mirror)}}')

            """ + MsbuildLookup(context) + """
            if (-not $msbuild) { exit 20 }

            """;

        private static string OfflinePage(string title, string text) => $"<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\"><title>{title}</title></head><body><h1>{title}</h1><p>{WebUtility.HtmlEncode(text)}</p></body></html>\n";

        /// <summary>Step 7: the solution restore by MSBuild's own NuGet tasks, so the host needs no nuget.exe; RestorePackagesConfig includes the packages.config projects.</summary>
        /// <remarks>A package folder that step 6 copied is already installed, so the restore never asks a private feed for it. NuGetAudit=false skips the vulnerability check, which signs in to CoreV2 and fails over SSH.</remarks>
        private static string RestoreScript(RunContext context)
        {
            var configFile = context.Source.NuGetConfig == null ? string.Empty : $" (D '{B64($@"/p:RestoreConfigFile={context.Mirror}\{WindowsPath(context.Source.NuGetConfig)}")}')";

            return ScriptPrologue + MsbuildPrologue(context) + $$"""
                & $msbuild (D '{{B64(WindowsPath(context.Source.Solution))}}') /t:Restore /p:RestorePackagesConfig=true /p:NuGetAudit=false /v:m /nologo{{configFile}}
                if ($LASTEXITCODE -eq $null) { exit 1 }
                exit $LASTEXITCODE
                """;
        }

        private static string SecretsScript(string target, byte[] content) => ScriptPrologue + $$"""
            $target = D '{{B64(target)}}'
            [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
            [IO.File]::WriteAllBytes($target, [Convert]::FromBase64String('{{Convert.ToBase64String(content)}}'))
            exit 0
            """;

        /// <summary>Splits the profile's extra MSBuild arguments on spaces outside double quotes, removing the quotes.</summary>
        private static List<string> SplitArguments(string? value)
        {
            var arguments = new List<string>();
            var current = new StringBuilder();
            var quoted = false;

            foreach (var character in value ?? string.Empty)
            {
                if (character == '"') quoted = !quoted;
                else if (char.IsWhiteSpace(character) && !quoted)
                {
                    if (current.Length > 0) arguments.Add(current.ToString());

                    current.Clear();
                }
                else current.Append(character);
            }

            if (current.Length > 0) arguments.Add(current.ToString());

            return arguments;
        }

        /// <summary>Sets this run's lock to the current time, so the lock age that LockHost compares with LockMaxAgeMinutes is the time since the last host step started.</summary>
        /// <remarks>The push (step 4) and the package copy (step 6) run no host script, so one of them longer than LockMaxAgeMinutes still makes the lock look left over.</remarks>
        private static string TouchLock(RunContext context) => $$"""
            $cluLock = Join-Path (D '{{B64(context.Mirror)}}') '.git\clu.lock'
            if ((Test-Path -LiteralPath $cluLock) -and ([IO.File]::ReadAllText($cluLock) -ceq (D '{{B64(context.LockContent)}}'))) { try { [IO.File]::SetLastWriteTimeUtc($cluLock, [DateTime]::UtcNow) } catch { } }

            """;
    }
}
