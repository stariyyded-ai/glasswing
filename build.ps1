# Сборка Glasswing.exe под .NET Framework 4.8 (он уже есть в Windows 10 и 11).
# RefDir: папка со сборками-описаниями .NET Framework 4.8 (пакет Microsoft.NETFramework.ReferenceAssemblies.net48).
param([string]$RefDir = (Join-Path $PSScriptRoot '../refs/net48/build/.NETFramework/v4.8'))
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$here = $PSScriptRoot
$refDir = $RefDir
$cs = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::new([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp7_3)
$trees = Get-ChildItem (Join-Path $here 'src') -Filter *.cs | ForEach-Object {
    [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_.FullName), $cs, $_.FullName, [Text.Encoding]::UTF8)
}
$refs = New-Object 'System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]'
foreach ($n in 'mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Management','Microsoft.CSharp','System.Xml') {
    $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $refDir "$n.dll")))
}
$opt = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::WindowsApplication).
    WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).
    WithPlatform([Microsoft.CodeAnalysis.Platform]::AnyCpu)
$c = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('Glasswing', [Microsoft.CodeAnalysis.SyntaxTree[]]$trees, $refs, $opt)
$icoBytes = [IO.File]::ReadAllBytes((Join-Path $here 'res/app.ico'))
$ico = New-Object IO.MemoryStream(,$icoBytes)
$man = New-Object IO.MemoryStream(,[IO.File]::ReadAllBytes((Join-Path $here 'res/app.manifest')))
$win32 = $c.CreateDefaultWin32Resources($true, $false, $man, $ico)
$res = New-Object 'System.Collections.Generic.List[Microsoft.CodeAnalysis.ResourceDescription]'
$res.Add([Microsoft.CodeAnalysis.ResourceDescription]::new('Glasswing.app.ico', [Func[IO.Stream]]{ New-Object IO.MemoryStream(,$icoBytes) }.GetNewClosure(), $true))
# Питомцы: res/pets/<персонаж>/<файл> -> Glasswing.pets.<персонаж>.<файл>
$petRoot = Join-Path $here 'res/pets'
if (Test-Path $petRoot) {
    foreach ($f in Get-ChildItem $petRoot -Recurse -File | Sort-Object FullName) {
        $rel = $f.FullName.Substring($petRoot.Length).TrimStart('\', '/') -split '[\\/]'
        $petBytes = [IO.File]::ReadAllBytes($f.FullName)
        $res.Add([Microsoft.CodeAnalysis.ResourceDescription]::new("Glasswing.pets.$($rel[0]).$($f.Name)", [Func[IO.Stream]]{ New-Object IO.MemoryStream(,$petBytes) }.GetNewClosure(), $true))
    }
}
New-Item -ItemType Directory -Force (Join-Path $here 'out') | Out-Null
$outPath = Join-Path $here 'out/Glasswing.exe'
$fs = [IO.File]::Create($outPath)
$r = $c.Emit($fs, $null, $null, $win32, $res)
$fs.Dispose()
$r.Diagnostics | Where-Object { $_.Severity -ne 'Hidden' -and $_.Id -ne 'CS1701' -and $_.Id -ne 'CS1702' } | ForEach-Object { $_.ToString() }
if ($r.Success) { "ok: $((Get-Item $outPath).Length) bytes" } else { Remove-Item $outPath; 'FAILED' }
