#Requires -Version 5.1
<#
.SYNOPSIS
    Phase 29A: reads what Carowinds Operations actually put on the Windows clipboard and checks it.

.DESCRIPTION
    Reads the real Windows clipboard (not the app's strings): the list of formats, CF_UNICODETEXT and the
    raw bytes of the registered "HTML Format" (CF_HTML). Checks the CF_HTML header, byte offsets, fragment
    markers, strict UTF-8, no byte-order mark, allowed markup only (p, br, b, i, ul, li, a href; Phase 29B:
    the only style attributes are font-weight:bold on b and font-style:italic on i), HTML Format listed
    before CF_UNICODETEXT,
    safe links, and that the HTML and the plain text carry the same words in the same order. With
    -Kind MorningMessage it also checks the section order, that the Daily Feature is last and that OD Notes
    are absent; with -Kind OdNotes it checks the separate OD Notes message.

    Read-only: never changes the clipboard. Needs no administrator rights. Writes a report and copies of
    the raw payloads to a "Phase29A-results" folder on the Desktop (synthetic test data only).

    Windows PowerShell 5.1 or PowerShell 7 on Windows. This file is ASCII on purpose (5.1 reads a .ps1
    without a byte-order mark as the ANSI code page); non-ASCII characters are built from code points.

.PARAMETER Kind
    MorningMessage, OdNotes, or Any (the original Copy options, a single section, an image).

.PARAMETER Label
    Short name for this capture, used in the result file names (for example "formatted" or "ops-summary").

.PARAMETER FromHtmlFile
    Check a saved CF_HTML payload (a "-html-format.bin" file this script wrote) instead of the clipboard.

.PARAMETER FromTextFile
    Check a saved plain-text capture (a "-unicode-text.txt" file this script wrote) instead of the clipboard.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Inspect-TeamsClipboard.ps1 -Kind MorningMessage -Label formatted

.EXAMPLE
    .\Inspect-TeamsClipboard.ps1 -Kind MorningMessage -FromHtmlFile .\x-html-format.bin -FromTextFile .\x-unicode-text.txt
#>
[CmdletBinding()]
param(
    [ValidateSet('MorningMessage', 'OdNotes', 'Any')]
    [string]$Kind = 'Any',
    [string]$Label = 'capture',
    [string]$DailyFeatureEnd = 'and why?',
    [string]$OdNotesMarker = 'SYNTHETIC OD note',
    [string]$FromHtmlFile,
    [string]$FromTextFile,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$fromFiles = [bool]$FromHtmlFile -or [bool]$FromTextFile
if (-not $fromFiles -and $env:OS -ne 'Windows_NT') {
    Write-Error 'This script reads the Windows clipboard and only runs on Windows (or use -FromHtmlFile / -FromTextFile).'
    exit 2
}

if (-not $fromFiles) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class Phase29AClipboard
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClipboardFormatNameW(uint format, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormatW(string name);
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr handle);

    static readonly Dictionary<uint, string> Standard = new Dictionary<uint, string> {
        {1, "CF_TEXT"}, {2, "CF_BITMAP"}, {7, "CF_OEMTEXT"}, {8, "CF_DIB"}, {13, "CF_UNICODETEXT"},
        {16, "CF_LOCALE"}, {17, "CF_DIBV5"}, {15, "CF_HDROP"} };

    public static uint HtmlFormat { get { return RegisterClipboardFormatW("HTML Format"); } }

    static void Open()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero)) return;
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("The clipboard is in use by another program. Close it and try again.");
    }

    public static string[] Formats()
    {
        var names = new List<string>();
        Open();
        try
        {
            uint format = 0;
            while ((format = EnumClipboardFormats(format)) != 0)
            {
                string name;
                if (!Standard.TryGetValue(format, out name))
                {
                    var sb = new StringBuilder(256);
                    name = GetClipboardFormatNameW(format, sb, sb.Capacity) > 0 ? sb.ToString() : ("#" + format);
                }
                names.Add(name);
            }
        }
        finally { CloseClipboard(); }
        return names.ToArray();
    }

    // Raw bytes of one format (GlobalSize may be rounded up past the data; callers use the payload's own lengths).
    public static byte[] Read(uint format)
    {
        Open();
        try
        {
            IntPtr handle = GetClipboardData(format);
            if (handle == IntPtr.Zero) return null;
            IntPtr pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return null;
            try
            {
                int size = (int)GlobalSize(handle).ToUInt64();
                var bytes = new byte[size];
                Marshal.Copy(pointer, bytes, 0, size);
                return bytes;
            }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }
}
'@
}

$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$Check, [bool]$Ok, [string]$Detail = '') {
    $status = if ($Ok) { 'PASS' } else { 'FAIL' }
    $results.Add([pscustomobject]@{ Result = $status; Check = $Check; Detail = $Detail })
}
function Add-Info([string]$Check, [string]$Detail) {
    $results.Add([pscustomobject]@{ Result = 'INFO'; Check = $Check; Detail = $Detail })
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = if ($OutputDirectory) { $OutputDirectory } else { Join-Path ([Environment]::GetFolderPath('Desktop')) 'Phase29A-results' }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$prefix = Join-Path $outDir "$stamp-$Label"

$emDash = [string][char]0x2014
$bullet = [string][char]0x2022
$nbsp = [string][char]0x00A0

# 1. Formats ------------------------------------------------------------------------------------------
if ($fromFiles) {
    $formats = @()
    if ($FromHtmlFile) { $formats += 'HTML Format' }
    if ($FromTextFile) { $formats += 'CF_UNICODETEXT' }
    Add-Info 'Source' "saved files: $FromHtmlFile $FromTextFile"
} else {
    $formats = [Phase29AClipboard]::Formats()
}
Add-Info 'Clipboard formats' ($formats -join ', ')
$hasHtml = $formats -contains 'HTML Format'
$hasText = $formats -contains 'CF_UNICODETEXT'
$hasImage = ($formats -contains 'CF_DIB') -or ($formats -contains 'CF_DIBV5') -or ($formats -contains 'CF_BITMAP') -or ($formats -contains 'PNG')
Add-Info 'HTML Format present' ([string]$hasHtml)
Add-Info 'Image format present' ([string]$hasImage)
if ($hasHtml -and $hasText -and -not $fromFiles) {
    # Phase 29B: richest format first, so readers that take the first format they understand get the HTML.
    Add-Result 'HTML Format listed before CF_UNICODETEXT' ([Array]::IndexOf($formats, 'HTML Format') -lt [Array]::IndexOf($formats, 'CF_UNICODETEXT')) ($formats -join ', ')
}

# 2. Plain text ---------------------------------------------------------------------------------------
$plain = $null
if ($hasText) {
    if ($FromTextFile) {
        $plain = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $FromTextFile))
    } else {
        $plain = [Text.Encoding]::Unicode.GetString([Phase29AClipboard]::Read(13))
    }
    $nul = $plain.IndexOf([char]0)
    if ($nul -ge 0) { $plain = $plain.Substring(0, $nul) }
    [IO.File]::WriteAllText("$prefix-unicode-text.txt", $plain, (New-Object Text.UTF8Encoding($true)))
    Add-Info 'Plain text length (characters)' ([string]$plain.Length)

    Add-Result 'Plain text has no generated HTML tags or entities' (-not ($plain -match '</?(p|br|b|i|strong|em|ul|li|a)[ >]' -or $plain -match '&(amp|lt|gt|nbsp|#39);'))
    $loneLf = [regex]::Matches($plain, '(?<!\r)\n').Count
    Add-Result 'Plain text uses Windows line breaks (CRLF)' ($loneLf -eq 0) "$loneLf bare LF"
    Add-Result 'Plain text has no Markdown bold (**)' (-not $plain.Contains('**'))
} elseif (-not $hasImage) {
    Add-Result 'CF_UNICODETEXT present' $false 'No text on the clipboard'
}

# 3. CF_HTML ------------------------------------------------------------------------------------------
$fragment = $null
if ($hasHtml) {
    $bytes = if ($FromHtmlFile) { [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $FromHtmlFile)) } else { [Phase29AClipboard]::Read([Phase29AClipboard]::HtmlFormat) }
    [IO.File]::WriteAllBytes("$prefix-html-format.bin", $bytes)
    $headerText = [Text.Encoding]::ASCII.GetString($bytes, 0, [Math]::Min(300, $bytes.Length))
    $offsets = @{}
    foreach ($name in 'StartHTML', 'EndHTML', 'StartFragment', 'EndFragment') {
        $m = [regex]::Match($headerText, "$name`:(\d+)\r\n")
        if ($m.Success) { $offsets[$name] = [int]$m.Groups[1].Value }
    }
    $version = [regex]::Match($headerText, '^Version:([0-9.]+)\r\n')
    Add-Result 'CF_HTML header starts with Version:' $version.Success $version.Groups[1].Value
    Add-Result 'CF_HTML header has all four offsets' ($offsets.Count -eq 4) (($offsets.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ' ')
    Add-Result 'No UTF-8 byte-order mark' (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF))

    if ($offsets.Count -eq 4) {
        $s = $offsets['StartHTML']; $e = $offsets['EndHTML']; $sf = $offsets['StartFragment']; $ef = $offsets['EndFragment']
        Add-Result 'Offsets in order (StartHTML <= StartFragment <= EndFragment <= EndHTML)' ($s -le $sf -and $sf -le $ef -and $ef -le $e)
        Add-Result 'EndHTML inside the clipboard block' ($e -le $bytes.Length) "EndHTML=$e, block=$($bytes.Length) bytes"
        if ($e -lt $bytes.Length) {
            $tail = @($bytes[$e..($bytes.Length - 1)] | Where-Object { $_ -ne 0 })
            Add-Result 'Only zero padding after EndHTML' ($tail.Count -eq 0) "$($tail.Count) non-zero bytes"
        }
        $strict = New-Object Text.UTF8Encoding($false, $true)
        try {
            $html = $strict.GetString($bytes, $s, $e - $s)
            $fragment = $strict.GetString($bytes, $sf, $ef - $sf)
            Add-Result 'HTML is valid UTF-8' $true
        } catch {
            Add-Result 'HTML is valid UTF-8' $false $_.Exception.Message
        }
        $before = [Text.Encoding]::ASCII.GetString($bytes, [Math]::Max(0, $sf - 20), [Math]::Min(20, $sf))
        $after = [Text.Encoding]::ASCII.GetString($bytes, $ef, [Math]::Min(18, $bytes.Length - $ef))
        Add-Result 'StartFragment points just after <!--StartFragment-->' ($before -eq '<!--StartFragment-->')
        Add-Result 'EndFragment points at <!--EndFragment-->' ($after -eq '<!--EndFragment-->')
        if ($html) { Add-Result 'HTML document wraps the fragment in <html><body>' ($html.StartsWith('<html>') -and $html.TrimEnd().EndsWith('</html>')) }
    }

    if ($fragment) {
        [IO.File]::WriteAllText("$prefix-fragment.html", $fragment, (New-Object Text.UTF8Encoding($false)))
        $tags = @([regex]::Matches($fragment, '<\s*/?\s*([A-Za-z0-9]+)') | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() } | Sort-Object -Unique)
        $allowed = @('p', 'br', 'b', 'i', 'ul', 'li', 'a')
        $bad = @($tags | Where-Object { $allowed -notcontains $_ })
        Add-Result 'Only p, br, b, i, ul, li, a elements' ($bad.Count -eq 0) ("found: " + ($tags -join ','))
        Add-Result 'No script, style sheet, event handler, image or iframe' (-not ($fragment -match '(?i)<script|<style|<img|<iframe|\son[a-z]+\s*=|class\s*='))
        $styles = @([regex]::Matches($fragment, '(?i)<([a-z]+)\s+style\s*=\s*"([^"]*)"') | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() + ':' + $_.Groups[2].Value })
        $badStyles = @($styles | Where-Object { $_ -ne 'b:font-weight:bold' -and $_ -ne 'i:font-style:italic' })
        $looseStyles = [regex]::Matches($fragment, '(?i)style\s*=').Count - $styles.Count
        Add-Result 'Only fixed bold/italic style attributes' (($badStyles.Count -eq 0) -and ($looseStyles -eq 0)) ("$($styles.Count) style attribute(s)")
        $hrefs = @([regex]::Matches($fragment, '<a\s+([^>]*)>') | ForEach-Object { $_.Groups[1].Value })
        $unsafe = @($hrefs | Where-Object { $_ -notmatch '^href="https?://[^"@\s]+"$' })
        Add-Result 'Every link is a plain http/https href' ($unsafe.Count -eq 0) ("$($hrefs.Count) link(s)")
        Add-Result 'javascript: never becomes a link' (-not ($fragment -match '(?i)href="\s*javascript:'))
        Add-Result 'No Teams mention markup (<at>)' (-not ($fragment -match '(?i)<at[\s>]'))
        Add-Info 'Bold elements' ([string]([regex]::Matches($fragment, '<b style="font-weight:bold">').Count))
        Add-Info 'Bullet items' ([string]([regex]::Matches($fragment, '<li>').Count))
        Add-Info 'Italic elements' ([string]([regex]::Matches($fragment, '<i style="font-style:italic">').Count))
        Add-Result 'Section headings are explicitly bold' ([regex]::Matches($fragment, '<b style="font-weight:bold">[^<]*:</b>').Count -gt 0)

        if ($plain) {
            # Same words, same order: tags stripped, entities decoded; the plain text's bullet glyphs are not words.
            $htmlText = [regex]::Replace(($fragment -replace '<br>', ' ' -replace '</(p|li)>', ' '), '<[^>]+>', '')
            $htmlText = [Net.WebUtility]::HtmlDecode($htmlText).Replace($nbsp, ' ')
            $htmlWords = @($htmlText -split '\s+' | Where-Object { $_ })
            $plainWords = @($plain.Replace($nbsp, ' ') -split '\s+' | Where-Object { $_ -and $_ -ne $bullet })
            $same = ($htmlWords.Count -eq $plainWords.Count) -and (@(Compare-Object $htmlWords $plainWords -SyncWindow 0).Count -eq 0)
            Add-Result 'HTML and plain text have the same words in the same order' $same "$($htmlWords.Count) vs $($plainWords.Count) words"
        }
    }
} else {
    Add-Info 'HTML Format' 'Not present (expected after Copy Plain Text, or for the original options when formatted copy is not the default)'
}

# 4. Message-specific checks --------------------------------------------------------------------------
if ($plain -and $Kind -eq 'MorningMessage') {
    $trimmed = $plain.TrimEnd()
    Add-Result 'Greeting is the first line' ($trimmed.StartsWith('Gooooood Morning!')) ($trimmed.Split("`r")[0])
    Add-Result 'Daily Feature is last' ($trimmed.EndsWith($DailyFeatureEnd)) ('ends with: ...' + $trimmed.Substring([Math]::Max(0, $trimmed.Length - 40)))
    Add-Result 'OD Notes absent from the main message' (-not ($plain.Contains('OD Notes') -or $plain.Contains($OdNotesMarker)))
    Add-Result 'Em dash kept' ($plain.Contains($emDash))
    Add-Result '@Name stays plain text' ($plain.Contains('@Jordan Testperson'))
    Add-Result 'Passdown notes not included automatically' (-not $plain.Contains('SYNTHETIC private passdown note'))
    $headings = @(
        @{ Name = 'Operations'; Pattern = "(?m)^Today's Operations:" },
        @{ Name = 'DAP / Placement'; Pattern = '(?m)^DAP / Placement:' },
        @{ Name = 'Reminders'; Pattern = '(?m)^Reminders:' },
        @{ Name = 'Ticket Prices'; Pattern = '(?m)^Ticket Prices:' },
        @{ Name = 'Tentative Break Plan'; Pattern = '(?m)^Tentative Break Plan:' })
    $positions = @()
    foreach ($h in $headings) {
        $m = [regex]::Match($plain, $h.Pattern)
        $pos = if ($m.Success) { $m.Index } else { -1 }
        $positions += [pscustomobject]@{ Name = $h.Name; Index = $pos }
    }
    $present = @($positions | Where-Object { $_.Index -ge 0 })
    $ordered = ($present.Count -le 1) -or (@(0..($present.Count - 2) | Where-Object { $present[$_].Index -gt $present[$_ + 1].Index }).Count -eq 0)
    Add-Info 'Section order in this copy' (($present | ForEach-Object { $_.Name }) -join ' > ')
    Add-Info 'Sections not in this copy' ((@($positions | Where-Object { $_.Index -lt 0 }) | ForEach-Object { $_.Name }) -join ', ')
    Add-Info 'Default order' 'Operations > DAP / Placement > Reminders > Ticket Prices > Tentative Break Plan (after Move Up/Down, compare with the composer instead)'
    Add-Info 'Sections already in default order' ([string]$ordered)
    Add-Info 'Stale-price line present' ([string]$plain.Contains('Some prices could not be re-verified today'))
}
if ($plain -and $Kind -eq 'OdNotes') {
    Add-Result 'Starts with "OD Notes:"' ($plain.StartsWith("OD Notes:`r`n"))
    Add-Result 'Contains only OD Notes (no greeting or Daily Feature)' (-not ($plain.Contains('Gooooood Morning!') -or $plain.Contains($DailyFeatureEnd)))
}

# 5. Report -------------------------------------------------------------------------------------------
$report = "$prefix-report.txt"
$lines = @(
    "Phase 29A clipboard inspection",
    "Time:    $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",
    "Kind:    $Kind",
    "Label:   $Label",
    "OS:      $([Environment]::OSVersion.VersionString)",
    "")
$lines += $results | ForEach-Object { '{0,-5} {1}{2}' -f $_.Result, $_.Check, $(if ($_.Detail) { "  [$($_.Detail)]" } else { '' }) }
$failCount = @($results | Where-Object { $_.Result -eq 'FAIL' }).Count
$lines += ''
$lines += $(if ($failCount -eq 0) { 'OVERALL: no clipboard check failed' } else { "OVERALL: $failCount check(s) FAILED" })
[IO.File]::WriteAllLines($report, $lines, (New-Object Text.UTF8Encoding($true)))

$lines | ForEach-Object { Write-Host $_ }
Write-Host ''
Write-Host "Saved: $report"
Write-Host "(and the raw clipboard copies next to it in $outDir)"
if ($failCount -gt 0) { exit 1 }
exit 0
