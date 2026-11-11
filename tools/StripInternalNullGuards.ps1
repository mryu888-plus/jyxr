<#
.SYNOPSIS
  按空值校验约定，删除内部层（Game.Core、Game.Presentation）里独立成行的 ArgumentNullException.ThrowIfNull(...) 语句。

.DESCRIPTION
  内部代码依靠 C# 可空引用类型（nullable 警告视为错误）保证非空；显式运行时校验只留在边界层：
  Godot 宿主、Game.Application 用例入口、Game.Content 内容加载、Game.Expressions 外部表达式解析。
  约定见 docs/runtime-architecture.md「空值约定」。脚本幂等，可在合并其它改动后重复运行；
  tests/Game.Tests/NullGuardPolicyTests 会在内部层重新出现 ThrowIfNull 时失败。
#>
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$internalRoots = @('src/Game.Core', 'src/Game.Presentation')
$guard = '^\s*ArgumentNullException\.ThrowIfNull\([^;]*\);\s*$'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$changedFiles = 0
$removedLines = 0

foreach ($relativeRoot in $internalRoots) {
    $root = Join-Path $RepositoryRoot $relativeRoot
    Get-ChildItem -Path $root -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object {
            $text = [System.IO.File]::ReadAllText($_.FullName)
            $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
            $lines = $text -split "\r?\n"
            $output = New-Object System.Collections.Generic.List[string]
            $removedHere = 0
            for ($i = 0; $i -lt $lines.Length; $i++) {
                if ($lines[$i] -match $guard) {
                    $removedHere++
                    # 删除后如果紧跟一行空行且前一行是块开头，连空行一起删，避免留下空白开头。
                    if ($i + 1 -lt $lines.Length -and $lines[$i + 1].Trim() -eq '' -and
                        $output.Count -gt 0 -and $output[$output.Count - 1].TrimEnd().EndsWith('{')) {
                        $i++
                    }
                    continue
                }
                $output.Add($lines[$i])
            }

            if ($removedHere -gt 0) {
                [System.IO.File]::WriteAllText($_.FullName, ($output -join $newline), $utf8)
                $changedFiles++
                $removedLines += $removedHere
            }
        }
}

Write-Output "Removed $removedLines internal null guards from $changedFiles files."
