using System;
using System.Collections.Concurrent;
using System.Threading;

namespace AITools.ScreenCapture.Helper
{
    // One MTA thread owns all recorder calls, including disposal and device checks.
    // Enqueue/Dispose never wait for a driver or encoder on the window thread.
    internal sealed class CaptureWorker : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private readonly Action<Exception> _failed;
        private readonly object _gate = new object();
        private bool _closed;

        internal CaptureWorker(Action<Exception> failed)
        {
            _failed = failed;
            var thread = new Thread(Run) { IsBackground = true, Name = "Capture encoder" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }
        internal bool Enqueue(Action action)
        {
            lock (_gate)
            {
                if (_closed) return false;
                _queue.Add(action);
                return true;
            }
        }
        private void Run()
        {
            try
            {
                foreach (Action action in _queue.GetConsumingEnumerable())
                {
                    try { action(); }
                    catch (Exception ex) { _failed(ex); }
                }
            }
            finally { _queue.Dispose(); }
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_closed) return;
                _closed = true;
                _queue.CompleteAdding();
            }
        }
    }
}
