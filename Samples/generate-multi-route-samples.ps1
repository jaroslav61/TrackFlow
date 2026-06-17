Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-Loco
{
    param(
        [string]$Id,
        [string]$Name,
        [int]$Address,
        [string]$Description
    )

    [ordered]@{
        Id = $Id
        Name = $Name
        Address = $Address
        Description = $Description
        IconName = ''
        Type = $null
        lengthMm = 0
        WeightT = 0
        DecoderType = $null
        DccSystemName = $null
        Functions = @()
        Number = ''
        HomeDepot = ''
        MaxSpeed = 0
        Power = 0
        MinRadius = 0
        Epoch = ''
        Scale = ''
        TotalKm = 0
        LastRunDate = $null
        LastMaintenanceDate = $null
        TotalOperationTime = '00:00:00'
    }
}

function New-Block
{
    param(
        [string]$Id,
        [double]$X,
        [double]$Y,
        [string]$Label,
        [string]$SignalRightId = $null,
        [string]$SignalLeftId = $null
    )

    [ordered]@{
        '$type' = 'block'
        lengthMm = 220
        BlockLengthCells = 4
        RequestYellow = $false
        MaxSpeedKmh = 120
        ResSpeedKmh = 40
        AllowBackward = $true
        AllowForward = $true
        CriticalSection = $false
        MaxTrainlengthMm = 0
        Indicators = @()
        SignalType = 0
        StopPosition = 0
        SignalLeftId = $SignalLeftId
        SignalRightId = $SignalRightId
        SignalUpId = $null
        SignalDownId = $null
        AllowShunting = $false
        Id = $Id
        X = $X
        Y = $Y
        Rotation = 0
        Label = $Label
        MarkerKey = 'Block'
    }
}

function New-Segment
{
    param(
        [string]$Id,
        [double]$X,
        [double]$Y,
        [double]$Rotation = 0
    )

    [ordered]@{
        '$type' = 'segment'
        LengthMm = 168
        IsOccupied = $false
        AssignedLocoId = $null
        IsLocked = $false
        Id = $Id
        X = $X
        Y = $Y
        Rotation = $Rotation
        Label = ''
        MarkerKey = 'TrackSegment'
    }
}

function New-Signal
{
    param(
        [string]$Id,
        [double]$X,
        [double]$Y,
        [string]$Label,
        [int]$DccAddress,
        [string]$ProtectsBlockId
    )

    [ordered]@{
        '$type' = 'signal'
        DccAddress = $DccAddress
        Aspect = 0
        IsBasicMode = $false
        ProtectsBlockId = $ProtectsBlockId
        SignalSystemId = $null
        SignalProfile = '3-aspect'
        Id = $Id
        X = $X
        Y = $Y
        Rotation = 0
        Label = $Label
        MarkerKey = 'Signal'
    }
}

function New-Turnout
{
    param(
        [string]$Id,
        [double]$X,
        [double]$Y,
        [string]$Label,
        [int]$DccAddress,
        [int]$State = 0,
        [double]$Rotation = 90
    )

    [ordered]@{
        '$type' = 'turnout'
        DccAddress = $DccAddress
        State = $State
        IsThreeWay = $false
        DccAddress2 = 0
        Description = ''
        InitialState = 0
        TurnoutLength = 15
        DccSystemType = $null
        PulseLength = 100
        UseDefaultPulse = $true
        ReverseLogic = $false
        DetectorLinkIds = @()
        MaxSpeed = 60
        LimitedSpeed = 40
        RequestYellow = $false
        Id = $Id
        X = $X
        Y = $Y
        Rotation = $Rotation
        Label = $Label
        MarkerKey = 'Turnout_R'
    }
}

function New-RouteMarker
{
    param(
        [string]$Id,
        [string]$RouteName,
        [string]$Description,
        [string]$SelectedRouteDefinitionId,
        [double]$X,
        [double]$Y
    )

    [ordered]@{
        '$type' = 'route'
        RouteName = $RouteName
        Description = $Description
        RequestYellow = $false
        MaxSpeed = 60
        LimitedSpeed = 40
        IndicatorIds = @()
        StartBlockId = $null
        EndBlockId = $null
        Steps = @()
        SelectedRouteDefinitionId = $SelectedRouteDefinitionId
        Id = $Id
        X = $X
        Y = $Y
        Rotation = 0
        Label = ''
        MarkerKey = 'Route'
    }
}

function New-Text
{
    param(
        [string]$Id,
        [double]$X,
        [double]$Y,
        [string]$Text,
        [int]$WidthInCells = 34,
        [int]$HeightInCells = 2,
        [string]$BackgroundColor = '#FFFDE7',
        [string]$FillColor = '#5D4037',
        [string]$FrameColor = '#E6C200'
    )

    [ordered]@{
        '$type' = 'text'
        Text = $Text
        VisibleInEditModeOnly = $false
        BackgroundColor = $BackgroundColor
        FontName = 'Segoe UI'
        FontSize = 11
        FillColor = $FillColor
        FrameColor = $FrameColor
        FrameThickness = 1
        HorizontalAlignment = 'Left'
        VerticalAlignment = 'Center'
        WidthInCells = $WidthInCells
        HeightInCells = $HeightInCells
        Id = $Id
        X = $X
        Y = $Y
        Rotation = 0
        Label = ''
        MarkerKey = 'Text'
    }
}

function New-RouteDef
{
    param(
        [string]$Id,
        [string]$Name,
        [string]$FromBlockId,
        [string]$ToBlockId,
        [string[]]$RouteSignalIds,
        [object[]]$TurnoutSettings,
        [string[]]$BlockIds,
        [string[]]$PathElementIds,
        [string]$Color,
        [int]$MaxSpeed = 60,
        [string]$FromBlockDirection = 'Right',
        [string]$ToBlockDirection = 'Right',
        [string]$StartNavigationDirection = 'Right'
    )

    [ordered]@{
        Id = $Id
        Name = $Name
        FromBlockId = $FromBlockId
        ToBlockId = $ToBlockId
        FromBlockDirection = $FromBlockDirection
        ToBlockDirection = $ToBlockDirection
        StartNavigationDirection = $StartNavigationDirection
        RouteSignalIds = $RouteSignalIds
        Kind = 1
        SafetyFallbackAspect = 'Stop'
        TurnoutSettings = $TurnoutSettings
        BlockIds = $BlockIds
        PathElementIds = $PathElementIds
        IsAutoGenerated = $false
        IsEnabled = $true
        Color = $Color
        MaxSpeed = $MaxSpeed
    }
}

function New-Project
{
    param(
        [object[]]$Locos,
        [object[]]$Elements,
        [object[]]$Routes,
        [int]$CanvasWidth = 2200,
        [int]$CanvasHeight = 1400
    )

    [ordered]@{
        SchemaVersion = 3
        IsDirty = $false
        Settings = [ordered]@{
            SchemaVersion = 3
            DccCentralType = $null
            DccCentralHost = $null
            DccCentralPort = $null
            AutoConnect = $null
            Scale = $null
            AutoRegenerateRoutes = $false
            MaxPathElements = 15
            MaxTurnoutsInPath = 5
        }
        Locomotives = $Locos
        Wagons = @()
        Layout = [ordered]@{
            SchemaVersion = 3
            CanvasWidth = $CanvasWidth
            CanvasHeight = $CanvasHeight
            ZoomFactor = 1
            PanX = 0
            PanY = 0
            Elements = $Elements
            Routes = $Routes
            SignalSystems = @(
                [ordered]@{
                    Id = 'SK_DEFAULT'
                    Name = 'Slovenská základná sústava'
                    Kind = 0
                    SupportedHeadCounts = @(2, 3, 4, 5)
                    Profiles = @()
                }
            )
            Plans = @()
        }
        CabAssignments = @()
    }
}

function Write-Sample
{
    param(
        [string]$BaseName,
        [object]$Project,
        [string]$Readme
    )

    $jsonPath = Join-Path $PSScriptRoot ("$BaseName.trackflow.json")
    $mdPath = Join-Path $PSScriptRoot ("$BaseName.md")

    $Project | ConvertTo-Json -Depth 30 | Set-Content -Path $jsonPath -Encoding UTF8
    Set-Content -Path $mdPath -Value $Readme -Encoding UTF8
}

$locos = @(
    (New-Loco -Id 'loco_demo_1' -Name 'Demo 754' -Address 3 -Description 'Testovacia lokomotíva 1'),
    (New-Loco -Id 'loco_demo_2' -Name 'Demo 040E' -Address 7 -Description 'Testovacia lokomotíva 2')
)

# A. SharedBlockWaitSample
$sharedBlockElements = @(
    (New-Text -Id 'txt_shared_block' -X 96 -Y 48 -Text 'SharedBlockWaitSample: X je logická zdieľaná kolízna zóna; druhá cesta čaká na uvoľnenie runtime vlastníctva.' -WidthInCells 42),
    (New-Text -Id 'txt_shared_block_steps' -X 96 -Y 96 -Text 'Postup: najprv spusti 1. A → B cez X, potom 2. C → D cez X (WAIT na X).' -BackgroundColor '#E8F5E9' -FillColor '#1B5E20' -FrameColor '#66BB6A'),
    (New-Text -Id 'txt_shared_block_legend' -X 96 -Y 480 -Text 'Legenda: žltý box = význam sample-u | zelený box = odporúčaný postup | 1./2. = poradie spustenia | X = logická zdieľaná kolízna zóna' -WidthInCells 44 -BackgroundColor '#E3F2FD' -FillColor '#0D47A1' -FrameColor '#42A5F5'),
    (New-Text -Id 'txt_shared_block_note' -X 96 -Y 528 -Text 'Poznámka: toto nie je doslovný fyzický 4-smerný blok; sample zámerne zobrazuje abstraktný konflikt runtime vlastníctva okolo X.' -WidthInCells 46 -BackgroundColor '#FFF3E0' -FillColor '#BF360C' -FrameColor '#FB8C00'),
    (New-Signal -Id 'sig_sb_a' -X 96 -Y 216 -Label 'S1 A' -DccAddress 201 -ProtectsBlockId 'blk_sb_shared'),
    (New-Signal -Id 'sig_sb_c' -X 96 -Y 336 -Label 'S2 C' -DccAddress 202 -ProtectsBlockId 'blk_sb_shared'),
    (New-Block -Id 'blk_sb_a' -X 120 -Y 216 -Label 'Blok A - vlak 1' -SignalRightId 'sig_sb_a'),
    (New-Block -Id 'blk_sb_shared' -X 360 -Y 264 -Label 'Blok X - logická kolízna zóna'),
    (New-Block -Id 'blk_sb_b' -X 600 -Y 216 -Label 'Blok B - výstup 1'),
    (New-Block -Id 'blk_sb_c' -X 120 -Y 336 -Label 'Blok C - vlak 2' -SignalRightId 'sig_sb_c'),
    (New-Block -Id 'blk_sb_d' -X 600 -Y 336 -Label 'Blok D - výstup 2'),
    (New-Segment -Id 'seg_sb_a_x' -X 264 -Y 216),
    (New-Segment -Id 'seg_sb_x_b' -X 480 -Y 216),
    (New-Segment -Id 'seg_sb_c_x' -X 264 -Y 336),
    (New-Segment -Id 'seg_sb_x_d' -X 480 -Y 336),
    (New-RouteMarker -Id 'rm_sb_1' -RouteName '1. A → B cez X' -Description 'Zdieľaný blok' -SelectedRouteDefinitionId 'r_sb_1' -X 120 -Y 144),
    (New-RouteMarker -Id 'rm_sb_2' -RouteName '2. C → D cez X (WAIT)' -Description 'Zdieľaný blok' -SelectedRouteDefinitionId 'r_sb_2' -X 120 -Y 384)
)
$sharedBlockRoutes = @(
    (New-RouteDef -Id 'r_sb_1' -Name 'A → X → B' -FromBlockId 'blk_sb_a' -ToBlockId 'blk_sb_b' -RouteSignalIds @('sig_sb_a') -TurnoutSettings @() -BlockIds @('blk_sb_a', 'blk_sb_shared', 'blk_sb_b') -PathElementIds @('seg_sb_a_x', 'seg_sb_x_b') -Color '#00D4AA' -MaxSpeed 50),
    (New-RouteDef -Id 'r_sb_2' -Name 'C → X → D' -FromBlockId 'blk_sb_c' -ToBlockId 'blk_sb_d' -RouteSignalIds @('sig_sb_c') -TurnoutSettings @() -BlockIds @('blk_sb_c', 'blk_sb_shared', 'blk_sb_d') -PathElementIds @('seg_sb_c_x', 'seg_sb_x_d') -Color '#FFB300' -MaxSpeed 50)
)
Write-Sample -BaseName 'SharedBlockWaitSample' -Project (New-Project -Locos $locos -Elements $sharedBlockElements -Routes $sharedBlockRoutes) -Readme @'
# SharedBlockWaitSample

## Čo schéma znázorňuje

Táto schéma je najjednoduchší prípad **shared block WAIT**.

Je zámerne **abstraktná**: `X` tu reprezentuje runtime shared ownership konflikt, nie doslovný fyzický jeden rovný blok so štyrmi vetvami.

- horná cesta ide `A -> X -> B`
- dolná cesta ide `C -> X -> D`
- spoločný konflikt je `Blok X - logická kolízna zóna`

Pointa sample-u:
- prvý vlak si zarezervuje a obsadí `Blok X`
- druhý vlak sa k `X` dostane neskôr a musí prejsť do WAIT
- po uvoľnení `X` má druhý vlak pokračovať bez potreby ručného zásahu

Použi tento sample na block ownership diagnostiku.
Ak chceš fyzicky plausibilnejšiu koľajovú geometriu so zdieľaním infra, použi radšej `SharedTurnoutWaitSample` alebo `TailClearReleaseSample`.

## Odporúčaný testovací postup

1. Otvor `SharedBlockWaitSample.trackflow.json`.
2. Polož prvú lokomotívu do `Blok A - vlak 1`.
3. Spusť route `1. A -> B cez X`.
4. Polož druhú lokomotívu do `Blok C - vlak 2`.
5. Spusť route `2. C -> D cez X (WAIT)`.
6. Sleduj, že druhá cesta čaká práve na `Blok X - logická kolízna zóna` a po jej release pokračuje.

## Očakávané správanie

- v Doctor okne sa objavia udalosti WAIT, block ownership, route lifecycle a signal gating
- druhá cesta dostane WAIT vstup a opakované retry pokusy
- po tail-clear sa zdieľaný blok uvoľní a druhá cesta pokračuje
'@

# B. SharedTurnoutWaitSample
$sharedTurnoutElements = @(
    (New-Text -Id 'txt_shared_turnout' -X 96 -Y 48 -Text 'SharedTurnoutWaitSample: jedna výhybka má iba 3 vetvy, preto druhá cesta ide opačným smerom a čaká na odovzdanie V1.' -WidthInCells 42),
    (New-Text -Id 'txt_shared_turnout_steps' -X 96 -Y 96 -Text 'Postup: najprv spusti 1. A → B, potom 2. D → A; takto je jedna zdieľaná výhybka fyzicky možná.' -WidthInCells 40 -BackgroundColor '#E8F5E9' -FillColor '#1B5E20' -FrameColor '#66BB6A'),
    (New-Text -Id 'txt_shared_turnout_legend' -X 96 -Y 480 -Text 'Legenda: žltý box = význam sample-u | zelený box = odporúčaný postup | 1./2. = poradie spustenia | V1 = shared konfliktové miesto' -WidthInCells 44 -BackgroundColor '#E3F2FD' -FillColor '#0D47A1' -FrameColor '#42A5F5'),
    (New-Signal -Id 'sig_st_a' -X 96 -Y 240 -Label 'S1 A' -DccAddress 211 -ProtectsBlockId 'blk_st_b'),
    (New-Signal -Id 'sig_st_d' -X 432 -Y 288 -Label 'S2 D' -DccAddress 212 -ProtectsBlockId 'blk_st_a'),
    (New-Block -Id 'blk_st_a' -X 120 -Y 240 -Label 'Blok A - štart 1 / cieľ 2' -SignalRightId 'sig_st_a'),
    (New-Turnout -Id 'sw_st_1' -X 288 -Y 240 -Label 'V1 zdieľaná' -DccAddress 301 -State 0),
    (New-Block -Id 'blk_st_b' -X 456 -Y 240 -Label 'Blok B - cieľ 1'),
    (New-Block -Id 'blk_st_d' -X 456 -Y 288 -Label 'Blok D - štart 2' -SignalLeftId 'sig_st_d'),
    (New-Segment -Id 'seg_st_a_sw' -X 216 -Y 240),
    (New-Segment -Id 'seg_st_sw_b' -X 384 -Y 240),
    (New-Segment -Id 'seg_st_sw_d' -X 384 -Y 288),
    (New-RouteMarker -Id 'rm_st_1' -RouteName '1. A → B (získa V1)' -Description 'Zdieľaná výhybka' -SelectedRouteDefinitionId 'r_st_1' -X 120 -Y 168),
    (New-RouteMarker -Id 'rm_st_2' -RouteName '2. D → A (WAIT na V1)' -Description 'Zdieľaná výhybka, opačný smer' -SelectedRouteDefinitionId 'r_st_2' -X 456 -Y 360)
)
$sharedTurnoutRoutes = @(
    (New-RouteDef -Id 'r_st_1' -Name 'A → B cez V1 priamo' -FromBlockId 'blk_st_a' -ToBlockId 'blk_st_b' -RouteSignalIds @('sig_st_a') -TurnoutSettings @([ordered]@{ TurnoutId = 'sw_st_1'; RequiredState = 0 }) -BlockIds @('blk_st_a', 'blk_st_b') -PathElementIds @('seg_st_a_sw', 'sw_st_1', 'seg_st_sw_b') -Color '#42A5F5' -MaxSpeed 50),
    (New-RouteDef -Id 'r_st_2' -Name 'D → A cez V1 odbočka' -FromBlockId 'blk_st_d' -ToBlockId 'blk_st_a' -RouteSignalIds @('sig_st_d') -TurnoutSettings @([ordered]@{ TurnoutId = 'sw_st_1'; RequiredState = 1 }) -BlockIds @('blk_st_d', 'blk_st_a') -PathElementIds @('seg_st_sw_d', 'sw_st_1', 'seg_st_a_sw') -Color '#EF6C00' -MaxSpeed 40 -FromBlockDirection 'Left' -ToBlockDirection 'Right' -StartNavigationDirection 'Left')
)
Write-Sample -BaseName 'SharedTurnoutWaitSample' -Project (New-Project -Locos $locos -Elements $sharedTurnoutElements -Routes $sharedTurnoutRoutes) -Readme @'
# SharedTurnoutWaitSample

## Čo schéma znázorňuje

Táto schéma znázorňuje **jednu zdieľanú výhybku** v topológii, ktorá je fyzicky vytvoriteľná.

- cesta 1 je `A -> B` (sprava / priamo)
- cesta 2 je `D -> A` (opačný smer cez odbočku)
- obe cesty používajú tú istú výhybku `V1 zdieľaná`
- horná cesta potrebuje na `V1` stav **priamo / Straight**
- opačný smer potrebuje na `V1` stav **odbočka / Diverge**

Pointa sample-u:
- jedna jednoduchá výhybka má iba 3 vetvy, preto tu nemôžu existovať dve nezávislé cesty typu `A -> B` a `C -> D` len s jednou výhybkou
- prvá aktivovaná cesta si vezme vlastníctvo výhybky `V1`
- druhá cesta nesmie výhybku prehodiť pod aktívnym vlakom
- preto prejde do WAIT a čaká na odovzdanie vlastníctva `V1`

## Odporúčaný testovací postup

1. Otvor `SharedTurnoutWaitSample.trackflow.json`.
2. V režime Prevádzka polož prvú lokomotívu do `Blok A - štart 1 / cieľ 2`.
3. Spusť route marker `1. A -> B (získa V1)`.
4. Polož druhú lokomotívu do `Blok D - štart 2`.
5. Spusť route marker `2. D -> A (WAIT na V1)`.
6. Sleduj, že druhá cesta čaká, kým prvá nepustí výhybku `V1`.

## Očakávané správanie

- v Doctor okne sa objavia udalosti súvisiace s turnout ownership a WAIT
- pri prvej ceste sa výhybka zarezervuje a preloží do požadovaného stavu
- pri druhej ceste sa najprv objaví odmietnutie / WAIT, potom po uvoľnení úspešné prevzatie výhybky
'@

# C. ParallelIndependentRoutesSample
$parallelElements = @(
    (New-Text -Id 'txt_parallel' -X 96 -Y 48 -Text 'ParallelIndependentRoutesSample: dve nezávislé cesty majú bežať súčasne bez WAIT.'),
    (New-Text -Id 'txt_parallel_steps' -X 96 -Y 96 -Text 'Postup: polož vlaky do A a C, potom spusti 1. aj 2. cestu takmer naraz; WAIT sa nemá objaviť.' -WidthInCells 38 -BackgroundColor '#E8F5E9' -FillColor '#1B5E20' -FrameColor '#66BB6A'),
    (New-Text -Id 'txt_parallel_legend' -X 96 -Y 480 -Text 'Legenda: žltý box = význam sample-u | zelený box = odporúčaný postup | 1./2. = poradie spustenia | bez X/V1 = bez shared konfliktu' -WidthInCells 44 -BackgroundColor '#E3F2FD' -FillColor '#0D47A1' -FrameColor '#42A5F5'),
    (New-Signal -Id 'sig_pi_a' -X 96 -Y 216 -Label 'S1 A' -DccAddress 221 -ProtectsBlockId 'blk_pi_b'),
    (New-Signal -Id 'sig_pi_c' -X 96 -Y 336 -Label 'S2 C' -DccAddress 222 -ProtectsBlockId 'blk_pi_d'),
    (New-Block -Id 'blk_pi_a' -X 120 -Y 216 -Label 'Blok A - linka 1' -SignalRightId 'sig_pi_a'),
    (New-Block -Id 'blk_pi_b' -X 432 -Y 216 -Label 'Blok B - linka 1'),
    (New-Block -Id 'blk_pi_c' -X 120 -Y 336 -Label 'Blok C - linka 2' -SignalRightId 'sig_pi_c'),
    (New-Block -Id 'blk_pi_d' -X 432 -Y 336 -Label 'Blok D - linka 2'),
    (New-Segment -Id 'seg_pi_ab' -X 288 -Y 216),
    (New-Segment -Id 'seg_pi_cd' -X 288 -Y 336),
    (New-RouteMarker -Id 'rm_pi_1' -RouteName '1. A → B (bez konfliktu)' -Description 'Paralelná linka 1' -SelectedRouteDefinitionId 'r_pi_1' -X 120 -Y 144),
    (New-RouteMarker -Id 'rm_pi_2' -RouteName '2. C → D (bez konfliktu)' -Description 'Paralelná linka 2' -SelectedRouteDefinitionId 'r_pi_2' -X 120 -Y 384)
)
$parallelRoutes = @(
    (New-RouteDef -Id 'r_pi_1' -Name 'A → B nezávislá' -FromBlockId 'blk_pi_a' -ToBlockId 'blk_pi_b' -RouteSignalIds @('sig_pi_a') -TurnoutSettings @() -BlockIds @('blk_pi_a', 'blk_pi_b') -PathElementIds @('seg_pi_ab') -Color '#66BB6A' -MaxSpeed 60),
    (New-RouteDef -Id 'r_pi_2' -Name 'C → D nezávislá' -FromBlockId 'blk_pi_c' -ToBlockId 'blk_pi_d' -RouteSignalIds @('sig_pi_c') -TurnoutSettings @() -BlockIds @('blk_pi_c', 'blk_pi_d') -PathElementIds @('seg_pi_cd') -Color '#AB47BC' -MaxSpeed 60)
)
Write-Sample -BaseName 'ParallelIndependentRoutesSample' -Project (New-Project -Locos $locos -Elements $parallelElements -Routes $parallelRoutes) -Readme @'
# ParallelIndependentRoutesSample

## Čo schéma znázorňuje

Táto schéma je kontrolný scenár bez konfliktu.

- horná cesta je `A -> B`
- dolná cesta je `C -> D`
- žiadne bloky ani výhybky nie sú spoločné

## Odporúčaný testovací postup

1. Otvor `ParallelIndependentRoutesSample.trackflow.json`.
2. Polož prvú lokomotívu do `Blok A - linka 1`.
3. Polož druhú lokomotívu do `Blok C - linka 2`.
4. Aktivuj obe route takmer naraz.
5. Sleduj, že ani jedna cesta neprejde do WAIT.

## Očakávané správanie

- v Doctor okne sa objavia route, block a signal udalosti
- nemá sa objaviť WAIT kvôli ownership konfliktu
- obe cesty sa aktivujú, prejdú a dobehnú bez zásahu do seba navzájom
'@

# D. DeadlockPotentialSample
$deadlockElements = @(
    (New-Text -Id 'txt_deadlock' -X 96 -Y 48 -Text 'DeadlockPotentialSample: dve cesty zdieľajú bloky v opačnom poradí, sample slúži čisto na diagnostiku WAIT.'),
    (New-Text -Id 'txt_deadlock_steps' -X 96 -Y 96 -Text 'Postup: spusti obe cesty v krátkom odstupe a sleduj dlhý WAIT; sample úmyselne negarantuje vyriešenie deadlocku.' -WidthInCells 42 -BackgroundColor '#FFF3E0' -FillColor '#BF360C' -FrameColor '#FB8C00'),
    (New-Text -Id 'txt_deadlock_legend' -X 96 -Y 480 -Text 'Legenda: žltý box = význam sample-u | oranžový box = rizikový scenár | 1./2. = poradie spustenia | X/Y = shared konfliktové bloky' -WidthInCells 45 -BackgroundColor '#E3F2FD' -FillColor '#0D47A1' -FrameColor '#42A5F5'),
    (New-Signal -Id 'sig_dl_a' -X 96 -Y 216 -Label 'S1 A' -DccAddress 231 -ProtectsBlockId 'blk_dl_x'),
    (New-Signal -Id 'sig_dl_c' -X 96 -Y 360 -Label 'S2 C' -DccAddress 232 -ProtectsBlockId 'blk_dl_y'),
    (New-Block -Id 'blk_dl_a' -X 120 -Y 216 -Label 'Blok A - štart 1' -SignalRightId 'sig_dl_a'),
    (New-Block -Id 'blk_dl_x' -X 336 -Y 216 -Label 'Blok X - shared 1'),
    (New-Block -Id 'blk_dl_y' -X 552 -Y 216 -Label 'Blok Y - shared 2'),
    (New-Block -Id 'blk_dl_b' -X 768 -Y 216 -Label 'Blok B - cieľ 1'),
    (New-Block -Id 'blk_dl_c' -X 120 -Y 360 -Label 'Blok C - štart 2' -SignalRightId 'sig_dl_c'),
    (New-Block -Id 'blk_dl_d' -X 768 -Y 360 -Label 'Blok D - cieľ 2'),
    (New-Segment -Id 'seg_dl_a_x' -X 240 -Y 216),
    (New-Segment -Id 'seg_dl_x_y' -X 456 -Y 216),
    (New-Segment -Id 'seg_dl_y_b' -X 672 -Y 216),
    (New-Segment -Id 'seg_dl_c_y' -X 240 -Y 360),
    (New-Segment -Id 'seg_dl_y_x' -X 456 -Y 360),
    (New-Segment -Id 'seg_dl_x_d' -X 672 -Y 360),
    (New-RouteMarker -Id 'rm_dl_1' -RouteName '1. A → X → Y → B' -Description 'Potenciálny deadlock 1' -SelectedRouteDefinitionId 'r_dl_1' -X 120 -Y 144),
    (New-RouteMarker -Id 'rm_dl_2' -RouteName '2. C → Y → X → D' -Description 'Potenciálny deadlock 2' -SelectedRouteDefinitionId 'r_dl_2' -X 120 -Y 408)
)
$deadlockRoutes = @(
    (New-RouteDef -Id 'r_dl_1' -Name 'A → X → Y → B' -FromBlockId 'blk_dl_a' -ToBlockId 'blk_dl_b' -RouteSignalIds @('sig_dl_a') -TurnoutSettings @() -BlockIds @('blk_dl_a', 'blk_dl_x', 'blk_dl_y', 'blk_dl_b') -PathElementIds @('seg_dl_a_x', 'seg_dl_x_y', 'seg_dl_y_b') -Color '#00897B' -MaxSpeed 45),
    (New-RouteDef -Id 'r_dl_2' -Name 'C → Y → X → D' -FromBlockId 'blk_dl_c' -ToBlockId 'blk_dl_d' -RouteSignalIds @('sig_dl_c') -TurnoutSettings @() -BlockIds @('blk_dl_c', 'blk_dl_y', 'blk_dl_x', 'blk_dl_d') -PathElementIds @('seg_dl_c_y', 'seg_dl_y_x', 'seg_dl_x_d') -Color '#D81B60' -MaxSpeed 45)
)
Write-Sample -BaseName 'DeadlockPotentialSample' -Project (New-Project -Locos $locos -Elements $deadlockElements -Routes $deadlockRoutes -CanvasWidth 2600 -CanvasHeight 1500) -Readme @'
# DeadlockPotentialSample

## Čo schéma znázorňuje

Táto schéma je zámerne konfliktná a slúži len na diagnostiku.

- horná cesta ide `A -> X -> Y -> B`
- dolná cesta ide `C -> Y -> X -> D`
- obe cesty chcú tie isté shared bloky, ale v opačnom poradí

## Odporúčaný testovací postup

1. Otvor `DeadlockPotentialSample.trackflow.json`.
2. Polož lokomotívy do `Blok A - štart 1` a `Blok C - štart 2`.
3. Aktivuj obe route v krátkom odstupe.
4. Sleduj WAIT správanie a vlastníctvo blokov `X` a `Y`.
5. Ak vznikne dlhý WAIT, skús jednu route zrušiť a sleduj cleanup.

## Očakávané správanie

- v Doctor okne sa majú objaviť výrazné WAIT, BLOCK a ROUTE udalosti
- sample je vhodný na sledovanie starvation / deadlock rizika a cleanupu po zrušení
- aktuálne sa neočakáva inteligentné riešenie patovej situácie
'@

# E. TailClearReleaseSample
$tailClearElements = @(
    (New-Text -Id 'txt_tail_clear' -X 96 -Y 48 -Text 'TailClearReleaseSample: shared blok X a výhybka V1 sa majú uvoľniť až po tail-clear; druhá cesta ide opačným smerom, aby bola topológia možná.' -WidthInCells 48),
    (New-Text -Id 'txt_tail_clear_steps' -X 96 -Y 96 -Text 'Postup: najprv spusti 1. A → X → B, potom 2. D → X → A; druhá cesta čaká na release X aj V1.' -WidthInCells 42 -BackgroundColor '#E8F5E9' -FillColor '#1B5E20' -FrameColor '#66BB6A'),
    (New-Text -Id 'txt_tail_clear_legend' -X 96 -Y 480 -Text 'Legenda: žltý box = význam sample-u | zelený box = odporúčaný postup | 1./2. = poradie spustenia | X/V1 = shared infra s tail-clear release' -WidthInCells 46 -BackgroundColor '#E3F2FD' -FillColor '#0D47A1' -FrameColor '#42A5F5'),
    (New-Signal -Id 'sig_tc_a' -X 96 -Y 240 -Label 'S1 A' -DccAddress 241 -ProtectsBlockId 'blk_tc_mid'),
    (New-Signal -Id 'sig_tc_d' -X 672 -Y 288 -Label 'S2 D' -DccAddress 242 -ProtectsBlockId 'blk_tc_mid'),
    (New-Block -Id 'blk_tc_a' -X 120 -Y 240 -Label 'Blok A - štart 1 / cieľ 2' -SignalRightId 'sig_tc_a'),
    (New-Block -Id 'blk_tc_mid' -X 360 -Y 240 -Label 'Blok X - shared tail-clear'),
    (New-Turnout -Id 'sw_tc_1' -X 528 -Y 240 -Label 'V1 tail-clear' -DccAddress 321 -State 0),
    (New-Block -Id 'blk_tc_b' -X 696 -Y 240 -Label 'Blok B - cieľ 1'),
    (New-Block -Id 'blk_tc_d' -X 696 -Y 288 -Label 'Blok D - štart 2' -SignalLeftId 'sig_tc_d'),
    (New-Segment -Id 'seg_tc_a_mid' -X 264 -Y 240),
    (New-Segment -Id 'seg_tc_mid_sw' -X 480 -Y 240),
    (New-Segment -Id 'seg_tc_sw_b' -X 624 -Y 240),
    (New-Segment -Id 'seg_tc_sw_d' -X 624 -Y 288),
    (New-RouteMarker -Id 'rm_tc_1' -RouteName '1. A → X → B (drží X/V1)' -Description 'Tail-clear priamo' -SelectedRouteDefinitionId 'r_tc_1' -X 120 -Y 168),
    (New-RouteMarker -Id 'rm_tc_2' -RouteName '2. D → X → A (čaká na release)' -Description 'Tail-clear opačný smer' -SelectedRouteDefinitionId 'r_tc_2' -X 696 -Y 360)
)
$tailClearRoutes = @(
    (New-RouteDef -Id 'r_tc_1' -Name 'A → X → B cez V1' -FromBlockId 'blk_tc_a' -ToBlockId 'blk_tc_b' -RouteSignalIds @('sig_tc_a') -TurnoutSettings @([ordered]@{ TurnoutId = 'sw_tc_1'; RequiredState = 0 }) -BlockIds @('blk_tc_a', 'blk_tc_mid', 'blk_tc_b') -PathElementIds @('seg_tc_a_mid', 'seg_tc_mid_sw', 'sw_tc_1', 'seg_tc_sw_b') -Color '#3949AB' -MaxSpeed 45),
    (New-RouteDef -Id 'r_tc_2' -Name 'D → X → A cez V1' -FromBlockId 'blk_tc_d' -ToBlockId 'blk_tc_a' -RouteSignalIds @('sig_tc_d') -TurnoutSettings @([ordered]@{ TurnoutId = 'sw_tc_1'; RequiredState = 1 }) -BlockIds @('blk_tc_d', 'blk_tc_mid', 'blk_tc_a') -PathElementIds @('seg_tc_sw_d', 'sw_tc_1', 'seg_tc_mid_sw', 'seg_tc_a_mid') -Color '#F4511E' -MaxSpeed 40 -FromBlockDirection 'Left' -ToBlockDirection 'Right' -StartNavigationDirection 'Left')
)
Write-Sample -BaseName 'TailClearReleaseSample' -Project (New-Project -Locos $locos -Elements $tailClearElements -Routes $tailClearRoutes -CanvasWidth 2600 -CanvasHeight 1500) -Readme @'
# TailClearReleaseSample

## Čo schéma znázorňuje

Táto schéma testuje, či sa shared infra neuvoľní priskoro.

- cesta 1 ide `A -> X -> B`
- cesta 2 ide `D -> X -> A`
- obe cesty používajú spoločnú časť `Blok X + V1`
- druhá cesta ide opačným smerom, aby topológia s jednou výhybkou ostala fyzicky možná

## Odporúčaný testovací postup

1. Otvor `TailClearReleaseSample.trackflow.json`.
2. Polož lokomotívu 1 do `Blok A - štart 1 / cieľ 2` a spusti route 1.
3. Polož lokomotívu 2 do `Blok D - štart 2` a spusti route 2.
4. Sleduj, že route 2 nedostane shared infra okamžite pri vstupe prvého vlaku do ďalšieho bloku.
5. Sleduj release až po tail-clear.

## Očakávané správanie

- v Doctor okne sa objavia block, turnout a cleanup udalosti súvisiace s tail-clear release
- release nesmie prísť hneď pri vstupe čela vlaku do ďalšieho bloku
- sample je vhodný na sledovanie leakov rezervácií po prejazde
'@

Write-Host 'Hotovo: multi-route samples boli vygenerované.'

