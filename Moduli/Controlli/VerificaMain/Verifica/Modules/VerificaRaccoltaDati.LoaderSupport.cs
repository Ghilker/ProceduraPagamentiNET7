using ProcedureNet7.Verifica;
using System;
using System.Data;
using System.Data.SqlClient;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        private SqlCommand CreatePopulationCommand(string sql, VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            string resolvedSql = (sql ?? string.Empty).Replace("{TEMP_TABLE}", ResolveTempTableName(context.TempPipelineTable));
            var cmd = new SqlCommand(resolvedSql, context.Connection)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999
            };

            AddAaParameter(cmd, context.AnnoAccademico);
            return cmd;
        }

        private void ReadAndMergeByStudentKey(SqlCommand command, Action<SqlDataReader, StudenteInfo> merge)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (merge == null)
                throw new ArgumentNullException(nameof(merge));

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!TryGetStudentInfo(reader, out var info, "CodFiscale", "NumDomanda"))
                    continue;

                merge(reader, info);
            }
        }

        private static DomicilioSnapshot ReadDomicilioSnapshot(SqlDataReader reader, string prefix)
        {
            prefix ??= string.Empty;
            return new DomicilioSnapshot
            {
                ComuneDomicilio = reader.SafeGetString(prefix + "ComuneDomicilio").Trim(),
                TitoloOneroso = reader.SafeGetBool(prefix + "TitoloOneroso"),
                ContrattoEnte = reader.SafeGetBool(prefix + "ContrattoEnte"),
                TipoEnte = reader.SafeGetString(prefix + "TipoEnte").Trim().ToUpperInvariant(),
                SerieContratto = reader.SafeGetString(prefix + "SerieContratto").Trim(),
                DataRegistrazione = reader.SafeGetDateTime(prefix + "DataRegistrazione"),
                DataDecorrenza = reader.SafeGetDateTime(prefix + "DataDecorrenza"),
                DataScadenza = reader.SafeGetDateTime(prefix + "DataScadenza"),
                DurataContratto = reader.SafeGetInt(prefix + "DurataContratto"),
                Prorogato = reader.SafeGetBool(prefix + "Prorogato"),
                DurataProroga = reader.SafeGetInt(prefix + "DurataProroga"),
                SerieProroga = reader.SafeGetString(prefix + "SerieProroga").Trim(),
                DenomEnte = reader.SafeGetString(prefix + "DenomEnte").Trim(),
                ImportoRataEnte = reader.SafeGetDouble(prefix + "ImportoRataEnte")
            };
        }

        private static void ApplyCurrentDomicilioSnapshot(StudenteInfo info, DomicilioSnapshot snapshot)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            info.InformazioniSede.Domicilio.codComuneDomicilio = snapshot.ComuneDomicilio;
            info.InformazioniSede.Domicilio.titoloOneroso = snapshot.TitoloOneroso;
            info.InformazioniSede.Domicilio.contrEnte = snapshot.ContrattoEnte;
            info.InformazioniSede.Domicilio.TipoEnte = snapshot.TipoEnte;
            info.InformazioniSede.Domicilio.codiceSerieLocazione = snapshot.SerieContratto;
            info.InformazioniSede.Domicilio.dataRegistrazioneLocazione = snapshot.DataRegistrazione;
            info.InformazioniSede.Domicilio.dataDecorrenzaLocazione = snapshot.DataDecorrenza;
            info.InformazioniSede.Domicilio.dataScadenzaLocazione = snapshot.DataScadenza;
            info.InformazioniSede.Domicilio.durataMesiLocazione = snapshot.DurataContratto;
            info.InformazioniSede.Domicilio.prorogatoLocazione = snapshot.Prorogato;
            info.InformazioniSede.Domicilio.durataMesiProrogaLocazione = snapshot.DurataProroga;
            info.InformazioniSede.Domicilio.codiceSerieProrogaLocazione = snapshot.SerieProroga;
            info.InformazioniSede.Domicilio.denominazioneIstituto = snapshot.DenomEnte;
            info.InformazioniSede.Domicilio.importoMensileRataIstituto = snapshot.ImportoRataEnte;
            info.InformazioniSede.ContrattoEnte = snapshot.ContrattoEnte;
        }
    }
}
