param(
    [switch]$List,
    [string]$Scene,
    [int]$Ring = 0,
    [int]$Slot = 0,
    [string]$Image,
    [switch]$Reset,
    [string]$DataDirectory = (Join-Path $env:APPDATA 'XY\XY轮盘')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$configPath = Join-Path $DataDirectory 'solidworks-self-plugin.json'
$mapPath = Join-Path $DataDirectory 'radial-icons.json'
$imagesPath = Join-Path $DataDirectory 'RadialIcons'
if (!(Test-Path -LiteralPath $configPath)) { throw "找不到轮盘配置：$configPath" }
$config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
$sceneNames = @{'sw.blank'='空白页';'sw.part'='零件';'sw.assembly'='装配体';'sw.drawing'='工程图';'sw.sketch'='草图'}
$rows = @(foreach ($menu in $config.Menus.PSObject.Properties) {
    foreach ($circle in $menu.Value.Rings) {
        foreach ($item in $circle.Items) {
            if ([string]::IsNullOrWhiteSpace($item.Value)) { continue }
            [pscustomobject]@{Scene=$menu.Name; SceneName=$sceneNames[$menu.Name]; Ring=([int]$circle.Index+1); Slot=([int]$item.Slot+1); Label=$item.Label; Kind=$item.Kind; Value=$item.Value}
        }
    }
})
function Read-IconMap {
    if (!(Test-Path -LiteralPath $mapPath)) { return [pscustomobject]@{Version=1;Icons=@()} }
    $map = Get-Content -LiteralPath $mapPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($map.Version -ne 1) { throw '图标配置版本不支持，未覆盖原文件。' }
    return $map
}
function Same-Cell($entry, $row) { return $entry.Scene -ceq $row.Scene -and $entry.Ring -eq ($row.Ring-1) -and $entry.Slot -eq ($row.Slot-1) }
function Save-Icon($row, [string]$source, [bool]$remove) {
    # Recheck the command at save time so a simultaneous settings edit cannot attach an icon to the wrong action.
    $latest = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $current = @($latest.Menus.($row.Scene).Rings | Where-Object {$_.Index -eq ($row.Ring-1)} | ForEach-Object {$_.Items} | Where-Object {$_.Slot -eq ($row.Slot-1)})
    if ($current.Count -ne 1 -or $current[0].Kind -cne $row.Kind -or $current[0].Value -cne $row.Value) { throw '该位置的命令已改变，请关闭脚本后重新打开。' }
    $map = Read-IconMap
    $remaining = @($map.Icons | Where-Object {!(Same-Cell $_ $row)})
    if (!$remove) {
        $source = (Resolve-Path -LiteralPath $source).Path
        if ([IO.Path]::GetExtension($source).ToLowerInvariant() -notin @('.png','.jpg','.jpeg','.bmp','.ico')) { throw '支持 PNG、JPG、BMP 和 ICO 图片。' }
        if ((Get-Item -LiteralPath $source).Length -gt 8MB) { throw '图片不能超过 8 MB。' }
        $bitmap = [Drawing.Image]::FromFile($source)
        try { if ($bitmap.Width -gt 2048 -or $bitmap.Height -gt 2048) { throw '图片宽高不能超过 2048 像素。' } }
        finally { $bitmap.Dispose() }
        [IO.Directory]::CreateDirectory($imagesPath) | Out-Null
        $name = [Guid]::NewGuid().ToString('N') + [IO.Path]::GetExtension($source).ToLowerInvariant()
        Copy-Item -LiteralPath $source -Destination (Join-Path $imagesPath $name)
        $remaining += [pscustomobject]@{Scene=$row.Scene;Ring=($row.Ring-1);Slot=($row.Slot-1);Kind=$row.Kind;Value=$row.Value;Image=$name}
    }
    $map.Icons = @($remaining)
    $temporary = Join-Path $DataDirectory ('radial-icons.'+[Guid]::NewGuid().ToString('N')+'.tmp')
    [IO.File]::WriteAllText($temporary, ($map | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $mapPath) {
        $backup = Join-Path $DataDirectory ('radial-icons.'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'.bak.json')
        [IO.File]::Replace($temporary, $mapPath, $backup)
    } else { [IO.File]::Move($temporary, $mapPath) }
    # Original configuration and previously imported images are never removed.
}
if ($List) { $rows | Select-Object Scene,Ring,Slot,Label; exit }
if ($Scene) {
    $selected = @($rows | Where-Object {$_.Scene -eq $Scene -and $_.Ring -eq $Ring -and $_.Slot -eq $Slot})
    if ($selected.Count -ne 1) { throw '请先使用 -List 查询场景、圈层和序号（圈层、序号均从 1 开始）。' }
    if (!$Reset -and !$Image) { throw '请选择 -Image 图片路径，或 -Reset 恢复默认图标。' }
    Save-Icon $selected[0] $Image ([bool]$Reset)
    Write-Output '图标已保存。重新打开轮盘或设置预览即可生效。'
    exit
}
[Windows.Forms.Application]::EnableVisualStyles()
$form = [Windows.Forms.Form]::new()
$form.Text = 'XY轮盘 · 自定义图标'
$form.Size = [Drawing.Size]::new(940,620)
$form.MinimumSize = [Drawing.Size]::new(800,480)
$form.StartPosition = 'CenterScreen'
$form.Font = [Drawing.Font]::new('Microsoft YaHei UI',9)
$layout = [Windows.Forms.TableLayoutPanel]::new()
$layout.Dock='Fill'; $layout.ColumnCount=2; $layout.RowCount=3
$layout.ColumnStyles.Add([Windows.Forms.ColumnStyle]::new('Percent',100)) | Out-Null
$layout.ColumnStyles.Add([Windows.Forms.ColumnStyle]::new('Absolute',210)) | Out-Null
$layout.RowStyles.Add([Windows.Forms.RowStyle]::new('Absolute',48)) | Out-Null
$layout.RowStyles.Add([Windows.Forms.RowStyle]::new('Percent',100)) | Out-Null
$layout.RowStyles.Add([Windows.Forms.RowStyle]::new('Absolute',54)) | Out-Null
$form.Controls.Add($layout)
$hint = [Windows.Forms.Label]::new()
$hint.Text='选中命令后导入图片。透明 PNG 效果最佳；图片会复制保存，原文件可移走。'
$hint.Dock='Fill'; $hint.TextAlign='MiddleLeft'
$layout.Controls.Add($hint,0,0); $layout.SetColumnSpan($hint,2)
$grid = [Windows.Forms.DataGridView]::new()
$grid.Dock='Fill'; $grid.ReadOnly=$true; $grid.AllowUserToAddRows=$false; $grid.AllowUserToDeleteRows=$false
$grid.MultiSelect=$false; $grid.SelectionMode='FullRowSelect'; $grid.RowHeadersVisible=$false
$grid.AutoSizeColumnsMode='Fill'; $grid.BackgroundColor=[Drawing.SystemColors]::Window
foreach ($heading in @('场景','圈层','序号','命令')) { [void]$grid.Columns.Add($heading,$heading) }
$grid.Columns[0].FillWeight=65; $grid.Columns[1].FillWeight=35; $grid.Columns[2].FillWeight=35; $grid.Columns[3].FillWeight=180
foreach ($row in $rows) { $index=$grid.Rows.Add($row.SceneName,$row.Ring,$row.Slot,$row.Label); $grid.Rows[$index].Tag=$row }
$layout.Controls.Add($grid,0,1)
$preview=[Windows.Forms.PictureBox]::new(); $preview.Dock='Fill'; $preview.SizeMode='CenterImage'; $preview.BackColor=[Drawing.Color]::WhiteSmoke
$layout.Controls.Add($preview,1,1)
$buttons=[Windows.Forms.FlowLayoutPanel]::new(); $buttons.Dock='Fill'; $buttons.Padding=[Windows.Forms.Padding]::new(0,8,0,0)
$layout.Controls.Add($buttons,0,2); $layout.SetColumnSpan($buttons,2)
function Refresh-Preview {
    if ($preview.Image) { $preview.Image.Dispose(); $preview.Image=$null }
    if (!$grid.CurrentRow) { return }
    $row=$grid.CurrentRow.Tag
    $entry=@((Read-IconMap).Icons | Where-Object {(Same-Cell $_ $row) -and $_.Kind -ceq $row.Kind -and $_.Value -ceq $row.Value}) | Select-Object -Last 1
    if ($entry) {
        $iconPath=[IO.Path]::GetFullPath((Join-Path $imagesPath $entry.Image))
        $safeRoot=[IO.Path]::GetFullPath($imagesPath)+[IO.Path]::DirectorySeparatorChar
        if ($iconPath.StartsWith($safeRoot,[StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $iconPath)) {
            $imageFile=[Drawing.Image]::FromFile($iconPath)
            try { $preview.Image=[Drawing.Bitmap]::new($imageFile,96,96) } finally { $imageFile.Dispose() }
        }
    }
}
$choose=[Windows.Forms.Button]::new(); $choose.Text='选择图片…'; $choose.Width=130
$resetButton=[Windows.Forms.Button]::new(); $resetButton.Text='恢复默认图标'; $resetButton.Width=130
$close=[Windows.Forms.Button]::new(); $close.Text='关闭'; $close.Width=90
$buttons.Controls.AddRange(@($choose,$resetButton,$close))
$choose.Add_Click({
    if (!$grid.CurrentRow) { return }
    $dialog=[Windows.Forms.OpenFileDialog]::new(); $dialog.Filter='图片|*.png;*.jpg;*.jpeg;*.bmp;*.ico'
    try { if ($dialog.ShowDialog($form) -eq 'OK') { Save-Icon $grid.CurrentRow.Tag $dialog.FileName $false; Refresh-Preview; $hint.Text='已保存：'+$grid.CurrentRow.Tag.Label+'。重新打开轮盘或设置预览即可生效。' } }
    catch { [Windows.Forms.MessageBox]::Show($form,$_.Exception.Message,'图标未保存') | Out-Null }
    finally { $dialog.Dispose() }
})
$resetButton.Add_Click({
    if (!$grid.CurrentRow) { return }
    try { Save-Icon $grid.CurrentRow.Tag '' $true; Refresh-Preview; $hint.Text='已恢复默认图标。重新打开轮盘或设置预览即可生效。' }
    catch { [Windows.Forms.MessageBox]::Show($form,$_.Exception.Message,'图标未保存') | Out-Null }
})
$grid.Add_SelectionChanged({try { Refresh-Preview } catch { $hint.Text=$_.Exception.Message }})
$close.Add_Click({$form.Close()})
try { [void]$form.ShowDialog() } finally { if ($preview.Image) { $preview.Image.Dispose() }; $form.Dispose() }
