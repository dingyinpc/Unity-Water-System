param(
    [Parameter(Mandatory=$true)][string]$Path,
    [Parameter(Mandatory=$true)][string]$Boxes,   # "x0,y0,x1,y1;x0,y0,x1,y1;..."  normalized 0-100 percent
    [string[]]$Labels = @()
)

Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap($Path)
$W = $bmp.Width; $H = $bmp.Height
Write-Output "image ${W}x${H}"

$parts = $Boxes -split ';'
$i = 0
foreach ($p in $parts) {
    if ($p.Trim() -eq '') { continue }
    $c = $p -split ','
    $x0 = [int]([double]$c[0] / 100 * $W); $y0 = [int]([double]$c[1] / 100 * $H)
    $x1 = [int]([double]$c[2] / 100 * $W); $y1 = [int]([double]$c[3] / 100 * $H)
    $sr = 0.0; $sg = 0.0; $sb = 0.0; $n = 0
    $rs = New-Object System.Collections.Generic.List[int]
    $gs = New-Object System.Collections.Generic.List[int]
    $bs = New-Object System.Collections.Generic.List[int]
    for ($y = $y0; $y -lt $y1; $y++) {
        for ($x = $x0; $x -lt $x1; $x++) {
            $px = $bmp.GetPixel($x, $y)
            $sr += $px.R; $sg += $px.G; $sb += $px.B; $n++
            $rs.Add([int]$px.R); $gs.Add([int]$px.G); $bs.Add([int]$px.B)
        }
    }
    if ($n -eq 0) { continue }
    $rs.Sort(); $gs.Sort(); $bs.Sort()
    $mid = [int]($n / 2)
    $r = [int]($sr / $n); $g = [int]($sg / $n); $b = [int]($sb / $n)
    $lab = if ($i -lt $Labels.Length) { $Labels[$i] } else { "box$i" }
    Write-Output ("{0,-22} mean rgb({1,3},{2,3},{3,3})  med rgb({4,3},{5,3},{6,3})" -f $lab, $r, $g, $b, $rs[$mid], $gs[$mid], $bs[$mid])
    $i++
}
$bmp.Dispose()
