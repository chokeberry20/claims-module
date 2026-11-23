# Minimal .http runner for verifying ClaimsModule.API.http: runs each request in order, resolves
# {{var}}, {{$processEnv X}}, {{$timestamp}}, {{$datetime iso8601 [n d]}} and {{name.response.body.$.path}}.
# Prints method, URL (token-free), status and a short body summary. Never prints the password or tokens.
param([Parameter(Mandatory)][string]$File, [string]$BaseUrl)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(60)
$vars = @{}; $responses = @{}
$text = [IO.File]::ReadAllText($File) -replace "`r`n", "`n"
foreach ($line in $text.Split("`n")) { if ($line -match '^@(\w+)\s*=\s*(.*)$') { $vars[$Matches[1]] = $Matches[2].Trim() } }
if ($BaseUrl) { $vars['baseUrl'] = $BaseUrl }

function Get-JsonPath($obj, [string]$path) {
    $cur = $obj
    foreach ($m in [regex]::Matches($path.Substring(1), '\.(\w+)|\[(\d+)\]')) {
        if ($m.Groups[1].Success) { $cur = $cur.($m.Groups[1].Value) } else { $cur = @($cur)[[int]$m.Groups[2].Value] }
    }
    return $cur
}
function Resolve([string]$s) {
    for ($i = 0; $i -lt 5; $i++) {
        $s = [regex]::Replace($s, '\{\{([^}]+)\}\}', {
            param($m) $e = $m.Groups[1].Value.Trim()
            if ($e -match '^\$processEnv (\w+)$') { return [Environment]::GetEnvironmentVariable($Matches[1]) }
            if ($e -eq '$timestamp') { return [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString() }
            if ($e -match '^\$datetime iso8601(?: (-?\d+) d)?$') { $d = [DateTime]::UtcNow; if ($Matches[1]) { $d = $d.AddDays([int]$Matches[1]) }; return $d.ToString('yyyy-MM-ddTHH:mm:ssZ') }
            if ($e -match '^(\w+)\.response\.body\.(\$.*)$') { return [string](Get-JsonPath $responses[$Matches[1]] $Matches[2]) }
            if ($vars.ContainsKey($e)) { return $vars[$e] }
            throw "Unresolved {{$e}}"
        })
    }
    return $s
}

$blocks = $text -split "(?m)^###.*$" | Select-Object -Skip 1
$n = 0; $failed = 0
foreach ($block in $blocks) {
    $lines = $block.Trim("`n").Split("`n")
    $name = $null; $i = 0
    while ($i -lt $lines.Length -and ($lines[$i] -match '^\s*#' -or $lines[$i].Trim() -eq '')) { if ($lines[$i] -match '^#\s*@name\s+(\w+)') { $name = $Matches[1] }; $i++ }
    if ($i -ge $lines.Length) { continue }
    $title = (($text -split "(?m)^###")[$n + 1].Split("`n")[0]).Trim(); $n++
    $parts = (Resolve $lines[$i]).Split(' ', 2); $method = $parts[0]; $url = $parts[1].Trim(); $i++
    $headers = @{}
    while ($i -lt $lines.Length -and $lines[$i].Trim() -ne '') { $h = (Resolve $lines[$i]).Split(':', 2); $headers[$h[0].Trim()] = $h[1].Trim(); $i++ }
    $body = if ($i -lt $lines.Length) { ($lines[$i..($lines.Length - 1)] -join "`n").Trim() } else { "" }
    $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($method), $url)
    if ($body) {
        $body = Resolve $body
        $ct = $headers['Content-Type']; if ($ct -like 'multipart/*') { $body = $body.Replace("`n", "`r`n") + "`r`n" }
        $req.Content = New-Object System.Net.Http.StringContent($body, [Text.Encoding]::UTF8)
        $req.Content.Headers.Remove('Content-Type') | Out-Null
        $req.Content.Headers.TryAddWithoutValidation('Content-Type', $ct) | Out-Null
    }
    foreach ($k in $headers.Keys) { if ($k -ne 'Content-Type') { $req.Headers.TryAddWithoutValidation($k, $headers[$k]) | Out-Null } }
    $res = $client.SendAsync($req).GetAwaiter().GetResult()
    $raw = $res.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $json = $null; try { $json = $raw | ConvertFrom-Json } catch { }
    if ($name) { $responses[$name] = $json }
    $summary = if ($json -and $json.PSObject.Properties['accessToken']) { '(token issued)' } elseif ($raw.Length -gt 160) { $raw.Substring(0, 160) + '...' } else { $raw }
    "{0,-4} {1,3}  {2}`n          {3}" -f $method, [int]$res.StatusCode, $title, $summary
}
