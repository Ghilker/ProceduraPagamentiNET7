using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ProceduraVerifica = ProcedureNet7.Verifica;

namespace ProcedureNet7.Verifica
{
    internal class Verifica : BaseProcedure<ArgsVerifica>
    {
        DatiVerifica _datiVerifica;
        string selectedAA = "20232024";

        public Verifica(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }

        public override void RunProcedure(ArgsVerifica args)
        {
            AddDatiVerifica();
        }

        void AddDatiVerifica()
        {
            string dataQuery = $"SELECT top(1) Cod_tipo_graduat from graduatorie where anno_accademico = '{selectedAA}' and Cod_beneficio = 'bs' order by Cod_tipo_graduat desc";
            SqlCommand readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - estrazione dati generali");
            bool graduatoriaDefinitiva = false;

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    graduatoriaDefinitiva = Utilities.SafeGetInt(reader, "Cod_tipo_graduat") == 1;
                }
            }

            dataQuery = $@"
                select 
                Soglia_Isee,
                Soglia_Ispe, 
                Importo_borsa_A, 
                Importo_borsa_B, 
                Importo_borsa_C,
                quota_mensa  
                from DatiGenerali_con 
                where Anno_accademico = '{selectedAA}'
            ";

            readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - estrazione dati generali");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    _datiVerifica = new DatiVerifica()
                    {
                        sogliaISEE = Utilities.SafeGetDouble(reader, "Soglia_Isee"),
                        sogliaISPE = Utilities.SafeGetDouble(reader, "Soglia_Ispe"),
                        importoBorsaA = Utilities.SafeGetDouble(reader, "Importo_borsa_A"),
                        importoBorsaB = Utilities.SafeGetDouble(reader, "Importo_borsa_B"),
                        importoBorsaC = Utilities.SafeGetDouble(reader, "Importo_borsa_C"),
                        quotaMensa = Utilities.SafeGetDouble(reader, "quota_mensa"),
                        graduatoriaDefinitiva = graduatoriaDefinitiva
                    };
                }
            }
        }

    }
}