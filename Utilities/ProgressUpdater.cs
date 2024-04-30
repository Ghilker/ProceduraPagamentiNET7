using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class ProgressUpdater
    {
        private int _updateCount = 0;
        private readonly int maxDashes = 40;
        private bool _inProcedure = false;
        private readonly int _currentProgress;
        private readonly LogLevel _logLevel;

        public ProgressUpdater()
        {

        }

        public ProgressUpdater(int currentProgress, LogLevel logLevel)
        {
            _inProcedure = true;
            _currentProgress = currentProgress;
            _logLevel = logLevel;
        }

        public void StartUpdating()
        {
            _updateCount = 0;
            Task.Run(() => UpdateProgress());
        }

        public void StopUpdating()
        {
            _inProcedure = false;
            Logger.Log(_currentProgress, "UPDATE:" + new String('-', maxDashes), _logLevel);
        }

        private void UpdateProgress()
        {
            while (_inProcedure)
            {
                while (_updateCount < maxDashes)
                {
                    if (!_inProcedure) { break; }
                    Thread.Sleep(250);
                    if (!_inProcedure) { break; }
                    string updateMessage = new('-', _updateCount + 1);
                    Logger.Log(_currentProgress, $"UPDATE:{updateMessage}", _logLevel);
                    _updateCount++;
                }
                _updateCount = 0;
            }
        }
    }
}
