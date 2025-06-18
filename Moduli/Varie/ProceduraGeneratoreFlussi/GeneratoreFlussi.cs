using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ProceduraGeneratoreFlussi : BaseProcedure<ArgsProceduraGeneratoreFlussi>
    {
        public ProceduraGeneratoreFlussi(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }

        public override void RunProcedure(ArgsProceduraGeneratoreFlussi args)
        {
            string selectedFilePath = args.FilePath;
            string selectedFolderPath = args.FolderPath;

            DataTable DatiFlusso = Utilities.ReadExcelToDataTable(selectedFilePath);
            
            List<string> CFList = new List<string>();
            foreach (DataRow row in DatiFlusso.Rows)
            {
                string? codFiscaleNullabile = row[0].ToString();
                
                if (codFiscaleNullabile == null) 
                { 
                    continue;
                }
                string codFiscale = codFiscaleNullabile;

                 CFList.Add(codFiscale);
                //decimal importoLordo = decimal.Parse(row[1].ToString());
                //decimal reversale = decimal.Parse(row[2].ToString());
                //decimal importoNetto = decimal.Parse(row[3].ToString());

               
            }

            string cfstring = string.Join(", ", CFList.Select(cf => $"'{cf}'"));

            string sql = $@"
                SELECT cod_fiscale, num_domanda FROM Domanda WHERE cod_fiscale in ({cfstring}) and anno_accademico = '20242025' and tipo_bando = 'lz'
            ";

            using (SqlCommand readData = new SqlCommand(sql, CONNECTION))
            {
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscaleEstratto = Utilities.SafeGetString(reader, "cod_fiscale");
                        if (codFiscaleEstratto == string.Empty)
                        {
                            continue;

                        }



                    }
                
                }

            }
        }
        

    }
}
