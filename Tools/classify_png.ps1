param(
    [Parameter(Mandatory=$true)][string]$Path
)
# 按"内容类别"分桶统计，而不是靠猜屏幕位置——两张图构图不同，位置框没有可比性。
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap($Path)
$W = $bmp.Width; $H = $bmp.Height

$buckets = @{}
foreach ($k in 'water','glitter','sand','rock','foliage','sky','other') {
  $buckets[$k] = New-Object System.Collections.Generic.List[double]
}
$counts = @{}
foreach ($k in $buckets.Keys) { $counts[$k] = 0 }

for ($y = 0; $y -lt $H; $y += 2) {
  for ($x = 0; $x -lt $W; $x += 2) {
    $p = $bmp.GetPixel($x, $y)
    $r = [double]$p.R; $g = [double]$p.G; $b = [double]$p.B
    $mx = [Math]::Max($r, [Math]::Max($g, $b)); $mn = [Math]::Min($r, [Math]::Min($g, $b))
    $sat = if ($mx -gt 0) { ($mx - $mn) / $mx } else { 0 }
    $key = 'other'
    if ($b -lt 60 -and $g -lt 60 -and $r -lt 60) { $key = 'rock' }          # 近黑
    elseif ($b -ge $r -and $g -ge $r -and $mx -lt 150) { $key = 'rock' }    # 暗蓝灰
    elseif ($r -gt 195 -and $g -gt 195 -and $b -gt 175 -and $r -ge $b) { $key = 'glitter' }
    elseif ($r -gt 150 -and $g -gt 130 -and $b -gt 110 -and $r -gt $b) { $key = 'sand' }
    elseif ($g -gt $r + 12 -and $g -gt 70) { $key = 'foliage' }
    elseif ($b -gt $r + 18 -and $g -gt $r) { $key = 'water' }
    elseif ($b -gt 170 -and $r -gt 140 -and $sat -lt 0.30) { $key = 'sky' }
    $buckets[$key].Add($r); $buckets[$key].Add($g); $buckets[$key].Add($b)
    $counts[$key] += 1
  }
}

$total = 0; foreach ($k in $counts.Keys) { $total += $counts[$k] }
Write-Output ("image {0}x{1}  sampled {2} px" -f $W, $H, $total)
foreach ($k in @('water','glitter','sand','rock','foliage','sky','other')) {
  $n = $counts[$k]
  if ($n -eq 0) { Write-Output ("{0,-9} --" -f $k); continue }
  $arr = $buckets[$k].ToArray()
  [Array]::Sort($arr)
  $m = $arr.Length / 3
  $mr = [int]$arr[[int]($m * 0) + [int]($m/2)]
  $mg = [int]$arr[[int]($m * 1) + [int]($m/2)]
  $mb = [int]$arr[[int]($m * 2) + [int]($m/2)]
  $pct = [Math]::Round(100.0 * $n / $total, 1)
  Write-Output ("{0,-9} cover {1,5}%  median rgb({2,3},{3,3},{4,3})" -f $k, $pct, $mr, $mg, $mb)
}
$bmp.Dispose()
