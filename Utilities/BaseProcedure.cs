using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal abstract class BaseProcedure<TArgs> : IDisposable
    {
        protected SqlConnection? CONNECTION;
        protected MasterForm? _masterForm;

        protected BaseProcedure(MasterForm? _masterForm, SqlConnection? connection_string)
        {
            this._masterForm = _masterForm;
            CONNECTION = connection_string;
        }

        //protected MainUI _mainForm; protected string CONNECTION_STRING; protected BaseProcedure(MainUI _masterForm, string connection_string) { }


        public virtual void EscapePressed()
        {
            MessageBox.Show(ToString() + ": Escape not implemented");
        }

        public abstract void RunProcedure(TArgs args);

        private bool disposed = false;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    CONNECTION = null;
                    _masterForm = null;
                }
                disposed = true;
            }
        }

        // Destructor
        ~BaseProcedure()
        {
            Dispose(false);
        }

    }
}
