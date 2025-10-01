using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Data;

namespace ProcedureNet7
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

#if PAGAMENTI || VARIE || VERIFICHE

            MessageBox.Show("L'applicazione non è più supportata, usare la versione Debug.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
            return;

#endif
                MasterForm masterForm = new MasterForm();
                Application.Run(masterForm);
        }
    }
}
