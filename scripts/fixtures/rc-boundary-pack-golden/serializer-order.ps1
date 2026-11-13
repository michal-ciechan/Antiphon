# CARD-1873: pwsh 7 ConvertTo-Json is the boundary case-membership order.
# Newtonsoft.Json 13 StringEscapeHandling.Default (the serializer behind
# pwsh 7.5 ConvertTo-Json -Compress) escapes quote, backslash and C0
# (\b \t \n \f \r, otherwise \u00xx), plus U+0085, U+2028 and U+2029.
# U+007F and every other C1 control stay literal. Slash stays literal.
# Windows PowerShell 5.1 also escapes & ' < > as \u0026 \u0027 \u003c \u003e.
# That HTML quirk is not the old packer's pwsh 7 key.
# ASCII-only.

function Get-RcSerializerJsonKey([string]$ClassName, [string]$Method, [string]$Uid) {
    return (ConvertTo-Json -InputObject @($ClassName, $Method, $Uid) -Compress)
}

function Get-RcSerializerOrderSamples {
    $emoji = [char]::ConvertFromUtf32(0x1F600)
    $rows = [Collections.Generic.List[object]]::new()
    $add = {
        param($Name, $Text, $Uid)
        $rows.Add([pscustomobject]@{ Name = [string]$Name; Text = [string]$Text; Uid = [string]$Uid })
    }
    & $add 'nel' ('x' + [char]0x0085) '1'
    & $add 'xa' 'xa' '2'
    & $add 'line' ('x' + [char]0x2028) '3'
    & $add 'para' ('x' + [char]0x2029) '4'
    & $add 'del' ('x' + [char]0x007F) '5'
    & $add 'nul' ('x' + [char]0x0000) '6'
    & $add 'soh' ('x' + [char]0x0001) '7'
    & $add 'bs' ('x' + [char]0x0008) '8'
    & $add 'tab' ('x' + [char]0x0009) '9'
    & $add 'lf' ('x' + [char]0x000A) '11'
    & $add 'vt' ('x' + [char]0x000B) '12'
    & $add 'ff' ('x' + [char]0x000C) '13'
    & $add 'cr' ('x' + [char]0x000D) '14'
    & $add 'us' ('x' + [char]0x001F) '15'
    & $add 'c1' ('x' + [char]0x0080) '16'
    & $add 'c1end' ('x' + [char]0x009F) '17'
    & $add 'quote' ('x' + [char]0x22 + 'y') '18'
    & $add 'backslash' ('x' + [char]0x5C + 'y') '19'
    & $add 'slash' 'x/y' '20'
    & $add 'amp' 'x&y' '21'
    & $add 'apos' ('x' + [char]0x27 + 'y') '22'
    & $add 'lt' 'x<y' '23'
    & $add 'gt' 'x>y' '24'
    & $add 'emoji' ('x' + $emoji) '25'
    & $add 'lone-high' ('x' + [char]0xD800 + 'a') '26'
    & $add 'lone-low' ('xa' + [char]0xDC00) '27'
    & $add 'two-high' ('x' + [char]0xD800 + [char]0xD800) '28'
    & $add 'pair-lone' ('a' + $emoji + [char]0xD800) '29'
    return $rows.ToArray()
}

function New-RcSerializerCtorNode([string]$Argument, [string]$Uid) {
    $type = 'Matrix("' + $Argument + '")'
    return [pscustomobject]@{
        ClassName = 'Antiphon.Tests.Fixture.' + $type
        Namespace = 'Antiphon.Tests.Fixture'
        Type = $type
        Method = 'Run'
        Uid = $Uid
        Categories = @()
        Excluded = $false
    }
}

function New-RcSerializerOrderNodes {
    $nodes = [Collections.Generic.List[object]]::new()
    foreach ($sample in @(Get-RcSerializerOrderSamples)) {
        $nodes.Add((New-RcSerializerCtorNode ([string]$sample.Text) ([string]$sample.Uid)))
    }
    return $nodes.ToArray()
}

function New-RcSerializerNumericUidNodes {
    $nodes = [Collections.Generic.List[object]]::new()
    foreach ($uid in @('10', '2')) {
        $nodes.Add([pscustomobject]@{
            ClassName = 'Antiphon.Tests.Fixture.Matrix'
            Namespace = 'Antiphon.Tests.Fixture'
            Type = 'Matrix'
            Method = 'Run'
            Uid = $uid
            Categories = @()
            Excluded = $false
        })
    }
    return $nodes.ToArray()
}

function New-RcSerializerOrderLeaf([object[]]$Nodes) {
    return [pscustomobject]@{
        id = 'all-profile-1'
        classes = @('Antiphon.Tests.Fixture.Matrix')
        exactFilter = '/*/Antiphon.Tests.Fixture/Matrix/Run'
        nodes = @($Nodes)
    }
}

function Get-RcSerializerOrderMap {
    return @{ 'Antiphon.Tests.Fixture.Matrix' = 'core' }
}

function Get-RcSerializerUidOrder([object[]]$Nodes) {
    $rows = New-Object object[] @($Nodes).Count
    $index = 0
    foreach ($node in @($Nodes)) {
        $rows[$index] = [pscustomobject]@{
            Key = (Get-RcSerializerJsonKey ([string]$node.ClassName) ([string]$node.Method) ([string]$node.Uid))
            Uid = [string]$node.Uid
        }
        $index++
    }
    for ($left = 0; $left -lt $rows.Length; $left++) {
        for ($right = $left + 1; $right -lt $rows.Length; $right++) {
            if ([string]::CompareOrdinal([string]$rows[$right].Key, [string]$rows[$left].Key) -lt 0) {
                $swap = $rows[$left]
                $rows[$left] = $rows[$right]
                $rows[$right] = $swap
            }
        }
    }
    return @($rows | ForEach-Object { [string]$_.Uid })
}

function Get-RcSerializerOracleFailures {
    $failed = [Collections.Generic.List[string]]::new()
    $nel = 'x' + [char]0x0085
    $del = 'x' + [char]0x007F
    $c1 = 'x' + [char]0x0080
    $emoji = [char]::ConvertFromUtf32(0x1F600)
    $lone = 'A' + [char]0xD800
    $pins = @(
        @{ Name = 'nel-escaped'; Actual = (ConvertTo-Json -InputObject @($nel) -Compress); Expected = '["x\u0085"]' }
        @{ Name = 'del-literal'; Actual = (ConvertTo-Json -InputObject @($del) -Compress); Expected = ('["' + $del + '"]') }
        @{ Name = 'c1-literal'; Actual = (ConvertTo-Json -InputObject @($c1) -Compress); Expected = ('["' + $c1 + '"]') }
        @{ Name = 'line-escaped'; Actual = (ConvertTo-Json -InputObject @([string][char]0x2028) -Compress); Expected = '["\u2028"]' }
        @{ Name = 'para-escaped'; Actual = (ConvertTo-Json -InputObject @([string][char]0x2029) -Compress); Expected = '["\u2029"]' }
        @{ Name = 'bs-short'; Actual = (ConvertTo-Json -InputObject @([string][char]0x0008) -Compress); Expected = '["\b"]' }
        @{ Name = 'vt-unicode'; Actual = (ConvertTo-Json -InputObject @([string][char]0x000B) -Compress); Expected = '["\u000b"]' }
        @{ Name = 'slash-literal'; Actual = (ConvertTo-Json -InputObject @('a/b') -Compress); Expected = '["a/b"]' }
        @{ Name = 'html-literal'; Actual = (ConvertTo-Json -InputObject @("&'<>") -Compress); Expected = ('["' + "&'<>" + '"]') }
        @{ Name = 'emoji-raw'; Actual = (ConvertTo-Json -InputObject @($emoji) -Compress); Expected = ('["' + $emoji + '"]') }
        @{ Name = 'lone-raw'; Actual = (ConvertTo-Json -InputObject @($lone) -Compress); Expected = ('["' + $lone + '"]') }
    )
    foreach ($pin in $pins) {
        if ([string]$pin.Actual -cne [string]$pin.Expected) { $failed.Add([string]$pin.Name) }
    }
    $ten = Get-RcSerializerJsonKey 'Antiphon.Tests.Fixture.Matrix' 'Run' '10'
    $two = Get-RcSerializerJsonKey 'Antiphon.Tests.Fixture.Matrix' 'Run' '2'
    if ([string]::CompareOrdinal($ten, $two) -ge 0) { $failed.Add('uid-string-order') }
    return @($failed)
}

function Get-RcSerializerCodeUnitFailures {
    # The encoder decides each UTF-16 code unit alone, so every code unit matching
    # ConvertTo-Json makes every string match. Stop after a handful of misses.
    $failed = [Collections.Generic.List[string]]::new()
    for ($i = 0; $i -le 65535; $i++) {
        $s = [string][char]$i
        $expected = Get-RcSerializerJsonKey $s $s $s
        $actual = [Antiphon.Nightly.NightlyLaunchPlan]::CaseSortKey($s, $s, $s)
        if ($actual -cne $expected) {
            $failed.Add(('{0:x4}' -f $i))
            if ($failed.Count -ge 8) { break }
        }
    }
    return @($failed)
}

function Get-RcSerializerKeyFailures {
    $failed = [Collections.Generic.List[string]]::new()
    foreach ($sample in @(Get-RcSerializerOrderSamples)) {
        $text = [string]$sample.Text
        $positions = @(
            @{ Label = ('class:' + $sample.Name); ClassName = ('Antiphon.Tests.Fixture.Matrix("' + $text + '")'); Method = 'Run'; Uid = '1' }
            @{ Label = ('method:' + $sample.Name); ClassName = 'Antiphon.Tests.Fixture.Matrix'; Method = $text; Uid = '1' }
            @{ Label = ('uid:' + $sample.Name); ClassName = 'Antiphon.Tests.Fixture.Matrix'; Method = 'Run'; Uid = $text }
        )
        foreach ($pos in $positions) {
            $expected = Get-RcSerializerJsonKey ([string]$pos.ClassName) ([string]$pos.Method) ([string]$pos.Uid)
            $actual = [Antiphon.Nightly.NightlyLaunchPlan]::CaseSortKey([string]$pos.ClassName, [string]$pos.Method, [string]$pos.Uid)
            if ($actual -cne $expected) { $failed.Add([string]$pos.Label) }
        }
    }
    return @($failed)
}
