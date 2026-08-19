$scriptPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\scripts\Fetch-KataGoAssets.ps1'))

Describe 'Fetch-KataGoAssets custom download directory cleanup' {
    It 'preserves unrelated sentinel files in a caller-provided directory' {
        $root = Join-Path ([System.IO.Path]::GetTempPath()) ('yijing-fetch-pester-' + [guid]::NewGuid().ToString('N'))
        $downloads = Join-Path $root 'caller-downloads'
        $manifest = Join-Path $root 'engine-manifest.json'
        $sentinel = Join-Path $downloads 'sentinel.keep'
        [System.IO.Directory]::CreateDirectory($downloads) | Out-Null
        [System.IO.File]::WriteAllText($sentinel, 'preserve me')
        [System.IO.File]::WriteAllText($manifest, '{"kataGoVersion":"fixture","downloads":[],"candidates":[]}')

        try {
            & $scriptPath -Manifest $manifest -DownloadDirectory $downloads | Out-Null

            (Test-Path -LiteralPath $downloads -PathType Container) | Should Be $true
            (Test-Path -LiteralPath $sentinel -PathType Leaf) | Should Be $true
            (Get-Content -Raw -LiteralPath $sentinel) | Should Be 'preserve me'
        }
        finally {
            if (Test-Path -LiteralPath $root) {
                [System.IO.Directory]::Delete($root, $true)
            }
        }
    }
}
