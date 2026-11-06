param([string]$SqlInstance = '(localdb)\MSSQLLocalDB')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data
$sourcePath = Join-Path $PSScriptRoot '..\..\Moduli\Pagamenti\ProceduraPagamenti\ProceduraPagamenti.PaymentChecks.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw
# Esegue i frammenti SQL di produzione sostituendo solo le tabelle con fixture temporanee.
$cteMatch = [regex]::Match($source, '(?s)string sqlCodiciPagamentoValidi = @"(?<sql>.*?)";')
$queryMatch = [regex]::Match($source, '(?s)string sqlPagam = sqlCodiciPagamentoValidi \+ @"(?<select>.*?)"\s*\+\s*\(IsSimulazione \? ", mce.ID_CAUSALE" : ""\) \+ @"(?<body>.*?)";')
if (!$cteMatch.Success -or !$queryMatch.Success) { throw 'Query dei movimenti non individuata nel codice di produzione.' }
$cte = $cteMatch.Groups['sql'].Value.Replace('Tipologie_pagam', '#Tipologie_pagam')
$query = $cte + $queryMatch.Groups['select'].Value + $queryMatch.Groups['body'].Value
$query = $query.Replace('MOVIMENTI_CONTABILI_ELEMENTARI', '#MOVIMENTI_CONTABILI_ELEMENTARI').Replace('MOVIMENTI_CONTABILI_GENERALI', '#MOVIMENTI_CONTABILI_GENERALI')

$connection = New-Object System.Data.SqlClient.SqlConnection("Server=$SqlInstance;Integrated Security=true;Initial Catalog=master;Connect Timeout=15")
$checks = 0
function Assert-Set($Actual, $Expected, [string]$Message) {
    $actualText = (@($Actual) | Sort-Object) -join ','
    $expectedText = (@($Expected) | Sort-Object) -join ','
    if ($actualText -ne $expectedText) { throw "$Message Attesi: [$expectedText]; ottenuti: [$actualText]." }
    $script:checks++
}
function Read-Values([string]$Sql, [string]$Current = 'BSP0', [string]$Benefit = 'BS', [bool]$Tax = $false) {
    $command = $connection.CreateCommand()
    $command.CommandText = $Sql
    [void]$command.Parameters.AddWithValue('@aa', '20262027')
    [void]$command.Parameters.AddWithValue('@beneficio', $Benefit)
    [void]$command.Parameters.AddWithValue('@codTipoPagamento', $Current)
    $command.Parameters.Add('@isTassaRegionale', [System.Data.SqlDbType]::Bit).Value = $Tax
    try {
        $reader = $command.ExecuteReader()
        try { while ($reader.Read()) { $reader.GetString(0) } }
        finally { $reader.Dispose() }
    }
    finally { $command.Dispose() }
}

try {
    $connection.Open()
    $setup = $connection.CreateCommand()
    $setup.CommandText = @'
CREATE TABLE #Tipologie_pagam (Cod_tipo_pagam varchar(10), categoria_pagamento varchar(2));
INSERT #Tipologie_pagam VALUES
('BSA0','PR'),(' bsa0 ','PR'),('BSA1','PR'),('BSA2','PR'),
('BSP0','PR'),('BSS0','SA'),('BSI0','PR'),('BST0','TR'),
('BSN0','PR'),('BSM0','PR'),('BSZ0','00'),('PLP0','PR');
CREATE TABLE #CFEstrazione (Cod_fiscale varchar(16));
CREATE TABLE #MOVIMENTI_CONTABILI_GENERALI (CODICE_MOVIMENTO int, COD_MANDATO varchar(50));
CREATE TABLE #MOVIMENTI_CONTABILI_ELEMENTARI
(CODICE_FISCALE varchar(16), CODICE_MOVIMENTO int, ANNO_ACCADEMICO varchar(8), STATO varchar(1));
INSERT #CFEstrazione VALUES ('BSA_PENDING'),('BSA_PAID'),('BSA_OTHER_AA'),('BSP_PENDING'),('BSA1_PENDING');
INSERT #MOVIMENTI_CONTABILI_GENERALI VALUES
(1,'BSA0_MANDATO'),(2,'BSA0_MANDATO'),(3,'BSA0_MANDATO'),(4,'BSP0_MANDATO'),(5,'BSA1_MANDATO'),(6,'BSA0_MANDATO');
INSERT #MOVIMENTI_CONTABILI_ELEMENTARI VALUES
('BSA_PENDING',1,'20262027','2'),('BSA_PAID',2,'20262027','1'),
('BSA_OTHER_AA',3,'20252026','2'),('BSP_PENDING',4,'20262027','2'),
('BSA1_PENDING',5,'20262027','2'),('NOT_SELECTED',6,'20262027','2');
'@
    try { [void]$setup.ExecuteNonQuery() }
    finally { $setup.Dispose() }

    Assert-Set (Read-Values $query) @('BSA_PENDING', 'BSP_PENDING') 'BSP0 deve escludere chi ha BSA0 in pagamento nello stesso anno, oltre ai blocchi esistenti.'
    # La simulazione consulta la stessa CTE e confronta i primi quattro caratteri del mandato.
    $codesQuery = $cte + ' SELECT CodTipoPagam FROM CodiciPagamentoValidi'
    $codes = @(Read-Values $codesQuery)
    Assert-Set $codes @('BSA0', 'BSP0', 'BSS0', 'BSI0') 'I codici bloccanti per BSP0 devono includere BSA0.'
    $simulated = @(
        @{ CF = 'SIM_BSA0'; Mandato = 'BSA0_SIMULATO' },
        @{ CF = 'SIM_BSA1'; Mandato = 'BSA1_SIMULATO' },
        @{ CF = 'SIM_BSP0'; Mandato = 'BSP0_SIMULATO' }
    )
    Assert-Set @($simulated | Where-Object { $codes -contains $_.Mandato.Substring(0, 4) } | ForEach-Object { $_.CF }) @('SIM_BSA0', 'SIM_BSP0') 'Anche un BSA0 simulato nel ciclo precedente deve bloccare BSP0.'
    foreach ($current in @('BSA0', 'BSP1', 'BSS0', 'BSI0')) {
        Assert-Set (Read-Values $codesQuery $current) @('BSP0', 'BSS0', 'BSI0') "Conservare i codici bloccanti per $current."
        Assert-Set (Read-Values $query $current) @('BSP_PENDING') "Conservare le esclusioni per $current."
    }
    Assert-Set (Read-Values $codesQuery 'BST0' 'BS' $true) @('BST0') 'Conservare il controllo della tassa regionale.'
    Assert-Set (Read-Values $codesQuery 'PLP0' 'PL') @('PLP0') 'Conservare il controllo degli altri benefici.'
    Write-Output "OK: $checks verifiche SQL su BSA0/BSP0, stati, anno accademico, simulazione e altre tipologie. Solo tabelle temporanee."
}
finally { $connection.Dispose() }
