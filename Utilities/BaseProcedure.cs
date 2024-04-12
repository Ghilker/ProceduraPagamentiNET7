using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal abstract class BaseProcedure<TArgs> : IDisposable
    {
        protected string? CONNECTION_STRING;
        protected MainUI _mainForm;

        protected BaseProcedure(MainUI mainUI, string connection_string)
        {
            _mainForm = mainUI;
            CONNECTION_STRING = connection_string;
        }

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
                    CONNECTION_STRING = string.Empty;
                    _mainForm = null;
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
