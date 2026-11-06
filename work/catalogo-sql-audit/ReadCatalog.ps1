param([Parameter(Mandatory=$true)][string]$OutputDirectory, [string[]]$Only)
$ErrorActionPreference = 'Stop'
$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$builder['Data Source'] = 'tcp:10.9.20.70,1433'
$builder['Initial Catalog'] = 'ADISU'
$builder['User ID'] = 'verifica'
$builder['Password'] = $env:VERIFICA_AUDIT_PASSWORD
$builder['ApplicationIntent'] = [System.Data.SqlClient.ApplicationIntent]::ReadOnly
$builder['Application Name'] = 'VerificaCatalogoAudit'
$builder['Pooling'] = $false
$builder['Connect Timeout'] = 10
$connection = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
try {
    $connection.Open()
    $queries = [ordered]@{
        anni = 'SELECT Anno_accademico, COUNT(*) AS Righe, COUNT(DISTINCT Cod_segnalazione) AS Codici FROM dbo.Decod_segnalazioni GROUP BY Anno_accademico ORDER BY Anno_accademico DESC'
        catalogo20262027 = "SELECT RTRIM(Cod_segnalazione) AS Codice, RTRIM(Anno_accademico) AS Anno, RTRIM(Tipo_segnalazione) AS Tipo, Descrizione, RTRIM(Iter_elaborativo) AS Iter, Data_validita, DescrizioneGrad, RTRIM(categoria) AS Categoria, Id_Decod_segnalazioni AS Id FROM dbo.Decod_segnalazioni WHERE Anno_accademico='20262027' ORDER BY Cod_segnalazione,Data_validita DESC,Id_Decod_segnalazioni DESC"
        storicoCodici = 'SELECT RTRIM(Cod_segnalazione) AS Codice, MIN(Anno_accademico) AS PrimoAnno, MAX(Anno_accademico) AS UltimoAnno, COUNT(*) AS Righe FROM dbo.Decod_segnalazioni GROUP BY Cod_segnalazione ORDER BY Cod_segnalazione'
        ultimeDescrizioni = 'SELECT Codice,Anno,Tipo,Descrizione,Iter,DescrizioneGrad,Categoria,Data_validita FROM (SELECT RTRIM(Cod_segnalazione) AS Codice,RTRIM(Anno_accademico) AS Anno,RTRIM(Tipo_segnalazione) AS Tipo,Descrizione,RTRIM(Iter_elaborativo) AS Iter,DescrizioneGrad,RTRIM(categoria) AS Categoria,Data_validita,ROW_NUMBER() OVER (PARTITION BY Cod_segnalazione ORDER BY Anno_accademico DESC,Data_validita DESC,Id_Decod_segnalazioni DESC) AS rn FROM dbo.Decod_segnalazioni) AS C WHERE rn=1 ORDER BY Codice'
        schema = "SELECT c.name AS Colonna,t.name AS Tipo,c.max_length AS Lunghezza,c.is_nullable AS Nullable,c.is_identity AS Identita,dc.definition AS DefaultSQL FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id LEFT JOIN sys.default_constraints dc ON c.default_object_id=dc.object_id WHERE c.object_id=OBJECT_ID('dbo.Decod_segnalazioni') ORDER BY c.column_id"
        vincoli = "SELECT i.name AS Indice,i.is_unique AS Univoco,i.is_primary_key AS PK,c.name AS Colonna,ic.key_ordinal AS Posizione FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON ic.object_id=c.object_id AND ic.column_id=c.column_id WHERE i.object_id=OBJECT_ID('dbo.Decod_segnalazioni') ORDER BY i.index_id,ic.key_ordinal"
        tipologieSchema = "SELECT TABLE_NAME,COLUMN_NAME,DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME LIKE '%TIP%STUD%' OR TABLE_NAME LIKE '%TIP%COR%' ORDER BY TABLE_NAME,ORDINAL_POSITION"
        tipologie = 'SELECT Cod_tipologia_studi AS Codice,Descrizione FROM dbo.Tipologie_studi ORDER BY Cod_tipologia_studi'
        variazioni = 'SELECT RTRIM(Cod_tipo_variaz) AS Codice,Descrizione FROM dbo.Tipologie_variaz ORDER BY Cod_tipo_variaz'
        variazioniSchema = "SELECT TABLE_NAME,COLUMN_NAME,DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME LIKE '%VARIAZ%' AND (COLUMN_NAME LIKE '%descr%' OR COLUMN_NAME LIKE '%tipo%' OR COLUMN_NAME LIKE '%cod%') ORDER BY TABLE_NAME,ORDINAL_POSITION"
    }
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    foreach ($entry in $queries.GetEnumerator()) {
        if ($Only -and $entry.Key -notin $Only) { continue }
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 30
        $command.CommandText = $entry.Value
        $data = [System.Data.DataTable]::new()
        $data.Load($command.ExecuteReader())
        $records = @($data.Rows | ForEach-Object {
            $record = [ordered]@{}
            foreach ($col in $data.Columns) {
                $value = $_[$col.ColumnName]
                $record[$col.ColumnName] = if ($value -is [DBNull]) { $null } else { $value }
            }
            [pscustomobject]$record
        })
        ConvertTo-Json -InputObject $records -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory ($entry.Key + '.json')) -Encoding utf8
        [pscustomobject]@{ Dataset=$entry.Key; Rows=$data.Rows.Count }
        $command.Dispose()
    }
} finally {
    $connection.Dispose()
    $builder.Clear()
}
