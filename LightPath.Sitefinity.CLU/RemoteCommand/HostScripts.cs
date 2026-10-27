using System.Text;

// ReSharper disable once CheckNamespace

namespace LightPath.Sitefinity.CLU
{
    public static partial class RemoteCommand
    {
        /// <remarks>
        /// Host scripts must stay pure ASCII: the host decodes standard input with its console code page, so every value goes in as Base64 (D) and comes back as a Base64 marker line (M).
        /// Single braces only: a doubled brace would be read as interpolation in the $$ raw strings below.
        /// </remarks>
        private const string ScriptPrologue = """
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            function D([string]$b) { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b)) }
            function M([string]$n, [string]$v) { Write-Output ('clu-' + $n + ' ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($v))) }
            $appcmd = Join-Path $env:windir 'system32\inetsrv\appcmd.exe'

            """;

        private static string B64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        /// <summary>Step 3: shell, physical path and APPDATA checks; for a build job also the mirror init and the run lock.</summary>
        /// <remarks>Exit codes: 10 wrong default shell, 11 physical path, 12 no APPDATA, 13 locked, 14 the mirror is a real clone.</remarks>
        private static string HostCheckScript(RunContext context, bool takeLock, bool readAppData, string? staleLock)
        {
            var script = new StringBuilder(ScriptPrologue);

            script.Append($$"""
                $mirror = D '{{B64(context.Mirror)}}'
                $pool = D '{{B64(context.Profile.Pool!)}}'
                $shell = ''
                $key = Get-ItemProperty -Path 'HKLM:\SOFTWARE\OpenSSH' -Name DefaultShell -ErrorAction SilentlyContinue
                if ($key) { $shell = [Environment]::ExpandEnvironmentVariables([string]$key.DefaultShell) }
                M 'shell' $shell
                if ($shell -and -not [string]::Equals($shell, [Environment]::ExpandEnvironmentVariables('%SystemRoot%\System32\cmd.exe'), [StringComparison]::OrdinalIgnoreCase)) { exit 10 }
                function N([string]$p) { [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($p)).TrimEnd('\') }
                $web = N (D '{{B64(HostWebFolder(context))}}')
                $apps = @(& $appcmd list app "/apppool.name:$pool" /config)
                if ($LASTEXITCODE -ne 0) { $apps = @() }
                [xml]$doc = '<r>' + ($apps -join "`n") + '</r>'
                $found = @($doc.r.application | Where-Object { $null -ne $_ })
                M 'apps' ([string]$found.Count)
                if ($found.Count -ne 1) { exit 11 }
                $vdir = @($found[0].virtualDirectory | Where-Object { $_.path -eq '/' })
                if ($vdir.Count -ne 1) { exit 11 }
                $configured = N $vdir[0].physicalPath
                if (-not [string]::Equals($configured, $web, [StringComparison]::OrdinalIgnoreCase)) { M 'configured' $configured; M 'expected' $web; exit 11 }

                """);

            if (readAppData) script.Append("""
                $pools = @(& $appcmd list apppool $pool /config)
                if ($LASTEXITCODE -ne 0) { exit 12 }
                [xml]$pdoc = '<r>' + ($pools -join "`n") + '</r>'
                $var = @($pdoc.r.add.environmentVariables.add | Where-Object { $_.name -eq 'APPDATA' })
                if ($var.Count -eq 0 -or -not $var[0].value) { exit 12 }
                M 'appdata' ([Environment]::ExpandEnvironmentVariables($var[0].value))

                """);

            if (!takeLock) return script.Append("""
                $lock = Join-Path $mirror '.git\clu.lock'
                if (Test-Path -LiteralPath $lock) { $held = ''; try { $held = [IO.File]::ReadAllText($lock) } catch { }; M 'locked' $held }
                if (Test-Path -LiteralPath (Join-Path $web 'app_offline.htm')) { M 'offline' '' }
                exit 0
                """).ToString();

            return script.Append($$"""
                $git = Join-Path $mirror '.git'
                if (Test-Path -LiteralPath $git) {
                    $origin = & git -C $mirror config --get remote.origin.url
                    if ($origin) { exit 14 }
                } else {
                    & git init -q $mirror
                    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                    & git -C $mirror config core.autocrlf false
                    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                    & git -C $mirror config core.eol lf
                    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                    M 'init' ''
                }
                $lock = Join-Path $git 'clu.lock'
                $stale = D '{{B64(staleLock ?? string.Empty)}}'
                if ($stale -and (Test-Path -LiteralPath $lock) -and ([IO.File]::ReadAllText($lock) -ceq $stale)) {
                    $moved = $lock + '.' + [Guid]::NewGuid().ToString('N')
                    try { [IO.File]::Move($lock, $moved); Remove-Item -LiteralPath $moved -Force } catch { }
                }
                try {
                    $stream = [IO.File]::Open($lock, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                    $bytes = [Convert]::FromBase64String('{{B64(context.LockContent)}}')
                    $stream.Write($bytes, 0, $bytes.Length)
                    $stream.Close()
                } catch {
                    if (-not (Test-Path -LiteralPath $lock)) { throw }
                    $held = ''
                    try { $held = [IO.File]::ReadAllText($lock) } catch { }
                    M 'locked' $held
                    M 'lockage' ([string][int](((Get-Date).ToUniversalTime() - (Get-Item -LiteralPath $lock).LastWriteTimeUtc).TotalMinutes))
                    exit 13
                }
                exit 0
                """).ToString();
        }

        /// <summary>For --init and --profile: marks MSBuild when msbuildPath exists or vswhere finds it.</summary>
        private static string MsbuildCheckScript(RunContext context) => ScriptPrologue + MsbuildLookup(context) + """
            if ($msbuild -and (Test-Path -LiteralPath $msbuild -PathType Leaf)) { M 'msbuild' $msbuild }
            exit 0
            """;

        /// <summary>The <c>--recycle</c> and <c>--status</c> job: the pool state, and for a recycle the recycle itself when the pool is started.</summary>
        private static string PoolScript(RunContext context, bool recycle)
        {
            var script = new StringBuilder(ScriptPrologue);

            script.Append($$"""
                $pool = D '{{B64(context.Profile.Pool!)}}'
                $state = [string](& $appcmd list apppool $pool /text:state)
                if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                M 'state' $state

                """);

            if (recycle) script.Append("""
                if ($state -eq 'Started') {
                    & $appcmd recycle apppool $pool
                    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                    M 'recycled' ''
                }

                """);

            return script.Append("exit 0\n").ToString();
        }
    }
}
