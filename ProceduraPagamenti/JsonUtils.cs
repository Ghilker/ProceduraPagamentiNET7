using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class JsonDeserializer
    {
        string jsonPath = "ProcedureNet7.ProceduraPagamenti.JSON.PagamentiBenefici.json";
        public void Deserialize(IProgress<(int, string)> progress)
        {
            List<Beneficio> benefici = DeserializeJsonResource(jsonPath);
            foreach (Beneficio beneficio in benefici)
            {
                //  progress.Report((1, $"Beneficio: {beneficio.codBeneficio}"));
                foreach (TipologiaPagamento tipologiaPagamento in beneficio.tipologiePagamenti)
                {
                    // progress.Report((1, $"  TipologiaPagamento: {tipologiaPagamento.nomeTipologiaPagamento}"));
                    foreach (CategoriaPagamento categoria in tipologiaPagamento.categoria)
                    {
                        // progress.Report((1, $"    NomeCategoria: {sottocategoria.nomeCategoria}"));
                        foreach (DaPagareItem daPagare in categoria.daPagare)
                        {
                            // progress.Report((1, $"      DaPagare: {daPagare.Key}, {daPagare.Value}, {beneficio.codBeneficio}{daPagare.Key}"));
                            progress.Report((0, $"INSERT INTO Tipologie_pagam_test values ('{beneficio.codBeneficio}{daPagare.Key}', '{beneficio.nomeBeneficio} - {categoria.nomeCategoria} - {daPagare.Value}', '{categoria.cod_categoria}', '{beneficio.codBeneficio}', '{daPagare.Key}', '{beneficio.nomeBeneficio}','{tipologiaPagamento.nomeTipologiaPagamento}', '{categoria.nomeCategoria}', '{daPagare.Value}') "));
                        }
                    }
                }
            }
        }
        private static List<Beneficio> DeserializeJsonResource(string resourcePath)
        {
            // Get the current assembly
            Assembly assembly = Assembly.GetExecutingAssembly();

            // Read the resource stream for the embedded JSON file
            using (Stream stream = assembly.GetManifestResourceStream(resourcePath))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Could not find embedded resource");
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    string jsonString = reader.ReadToEnd();
                    return JsonSerializer.Deserialize<List<Beneficio>>(jsonString);
                }
            }
        }

    }
}
