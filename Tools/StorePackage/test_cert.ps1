# Test certificate for installing the Jelly Survival MSIX on this PC (docs/work/store.md section 3).
# NOT needed for the Store: Partner Center re-signs the package with a Microsoft certificate.
#
#   test_cert.ps1 -Action Create -Subject "CN=JellySurvivalDev" -PfxPath artifacts\store\test-cert\JellySurvivalTest.pfx
#       makes (or reuses) a self-signed code-signing certificate in CurrentUser\My, exports it to the .pfx
#       (password: jelly-test) and a .cer next to it.
#   test_cert.ps1 -Action Trust  -PfxPath ...   (admin) adds the .cer to LocalMachine\TrustedPeople so Windows
#       accepts packages signed with it.
#   test_cert.ps1 -Action Remove -Subject "CN=JellySurvivalDev"   (admin) removes it from CurrentUser\My and
#       LocalMachine\TrustedPeople again.
param(
    [Parameter(Mandatory = $true)][ValidateSet('Create', 'Trust', 'Remove')][string]$Action,
    [string]$Subject = 'CN=JellySurvivalDev',
    [string]$PfxPath = 'artifacts\store\test-cert\JellySurvivalTest.pfx'
)
$ErrorActionPreference = 'Stop'
$FriendlyName = 'Jelly Survival MSIX test signing (local only)'
$Password = ConvertTo-SecureString -String 'jelly-test' -Force -AsPlainText

function Find-Mine([string]$store) {
    Get-ChildItem $store | Where-Object { $_.Subject -eq $Subject -and $_.FriendlyName -eq $FriendlyName }
}

switch ($Action) {
    'Create' {
        $cert = Find-Mine 'Cert:\CurrentUser\My' | Select-Object -First 1
        if (-not $cert) {
            $cert = New-SelfSignedCertificate -Type Custom -Subject $Subject -KeyUsage DigitalSignature `
                -FriendlyName $FriendlyName -CertStoreLocation 'Cert:\CurrentUser\My' `
                -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(1)
            Write-Output "created test certificate $($cert.Thumbprint)"
        } else {
            Write-Output "reusing test certificate $($cert.Thumbprint)"
        }
        $dir = Split-Path -Parent $PfxPath
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $Password | Out-Null
        Export-Certificate -Cert $cert -FilePath ([IO.Path]::ChangeExtension($PfxPath, '.cer')) | Out-Null
    }
    'Trust' {
        Import-Certificate -FilePath ([IO.Path]::ChangeExtension($PfxPath, '.cer')) -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
        Write-Output 'trusted in LocalMachine\TrustedPeople'
    }
    'Remove' {
        foreach ($store in 'Cert:\CurrentUser\My', 'Cert:\LocalMachine\TrustedPeople') {
            foreach ($c in (Get-ChildItem $store | Where-Object { $_.Subject -eq $Subject -and ($_.FriendlyName -eq $FriendlyName -or $store -like '*TrustedPeople') })) {
                Remove-Item -Path ($store + '\' + $c.Thumbprint)
                Write-Output "removed $($c.Thumbprint) from $store"
            }
        }
    }
}
