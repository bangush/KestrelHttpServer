using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Windows.Rio.Internal.Schedulers
{
    public class RioAwaitable<TRequest> : ICriticalNotifyCompletion //where TRequest// : UvRequest
    {
        private readonly static Action _callbackCompleted = () => { };

        private Action _callback;

        private Exception _exception;

        private int _status;

        public static readonly Action<TRequest, int, Exception, object> Callback = (req, status, error, state) =>
        {
            var awaitable = (RioAwaitable<TRequest>)state;

            awaitable._exception = error;
            awaitable._status = status;

            var continuation = Interlocked.Exchange(ref awaitable._callback, _callbackCompleted);

            continuation?.Invoke();
        };

        public RioAwaitable<TRequest> GetAwaiter() => this;
        public bool IsCompleted => _callback == _callbackCompleted;

        public RioWriteResult GetResult()
        {
            var exception = _exception;
            var status = _status;

            // Reset the awaitable state
            _exception = null;
            _status = 0;
            _callback = null;

            return new RioWriteResult(status, exception);
        }

        public void OnCompleted(Action continuation)
        {
            if (_callback == _callbackCompleted ||
                Interlocked.CompareExchange(ref _callback, continuation, null) == _callbackCompleted)
            {
                Task.Run(continuation);
            }
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }
    }

    public struct RioWriteResult
    {
        public int Status { get; }
        public Exception Error { get; }

        public RioWriteResult(int status, Exception error)
        {
            Status = status;
            Error = error;
        }
    }
}
