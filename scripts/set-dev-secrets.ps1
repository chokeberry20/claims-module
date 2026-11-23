<#
.SYNOPSIS
  Sets the local development secrets of the API with `dotnet user-secrets` (outside the repo).

.DESCRIPTION
  The API needs a JWT signing key (at least 32 bytes) at startup, and the demo users sign in with the
  passwords in DemoUserPasswords:{user} (D-32, D-72). In Azure both come from Key Vault. Locally they are
  user secrets, stored in your profile, never in the repository.

  - Jwt:SigningKey: a new random 48-byte key (base64), unless -KeepSigningKey is given.
  - DemoUserPasswords:handler1|handler2|supervisor1|manager1: the password you pass, for all four users
    (local only). It is read as a SecureString, so it doesn't end up in your shell history.
  - Hangfire:Dashboard:Password: optional; without it the dashboard is local-requests-only in Development.

  Nothing is printed except the names of the keys that were set.

.EXAMPLE
  ./scripts/set-dev-secrets.ps1
  ./scripts/set-dev-secrets.ps1 -KeepSigningKey
#>
[CmdletBinding()]
param(
    [SecureString] $DemoPassword,
    [switch] $KeepSigningKey,
    [switch] $SetDashboardPassword
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\ClaimsModule.API'

function ConvertTo-PlainText([SecureString] $secure) {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Set-Secret([string] $name, [string] $value) {
    if ([string]::IsNullOrEmpty($value)) { throw "Refusing to set an empty value for $name." }
    # The value goes to dotnet as an argument of this process only; it is not echoed.
    & dotnet user-secrets set $name $value --project $project | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet user-secrets failed for $name." }
    Write-Host "set $name"
}

if (-not $KeepSigningKey) {
    $bytes = New-Object byte[] 48
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    if (-not ($bytes | Where-Object { $_ -ne 0 })) { throw 'The random generator returned only zeros.' }
    Set-Secret 'Jwt:SigningKey' ([Convert]::ToBase64String($bytes))
}

if (-not $DemoPassword) { $DemoPassword = Read-Host -AsSecureString 'Password for the four local demo users' }
$plain = ConvertTo-PlainText $DemoPassword
if ($plain.Length -lt 8) { throw 'Use at least 8 characters.' }
foreach ($user in 'handler1', 'handler2', 'supervisor1', 'manager1') {
    Set-Secret "DemoUserPasswords:$user" $plain
}

if ($SetDashboardPassword) {
    Set-Secret 'Hangfire:Dashboard:Password' (ConvertTo-PlainText (Read-Host -AsSecureString 'Hangfire dashboard password'))
}
