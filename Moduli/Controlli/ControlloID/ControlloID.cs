using OpenCvSharp;
using System;
using System.Data.SqlClient;
using System.Diagnostics; // For Process.Start
using System.IO;
using Tesseract;

namespace ProcedureNet7
{
    internal class ControlloID : BaseProcedure<ArgsControlloID>
    {
        // Example constructor
        public ControlloID(MasterForm? _masterForm, SqlConnection? connection_string)
            : base(_masterForm, connection_string) { }

        public override void RunProcedure(ArgsControlloID args)
        {

        }

    }
}
