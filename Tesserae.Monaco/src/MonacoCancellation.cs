using System;
using System.Threading;

namespace Tesserae.Monaco
{
    /// <summary>
    /// Monaco's cancellation token as a .NET <see cref="CancellationToken"/>, for the length of one provider
    /// call. Monaco cancels a request the moment its answer stops mattering - the caret moved on, the text
    /// changed again - and a host that hands <see cref="Token"/> to its fetch is what turns that into an
    /// aborted request rather than a server that keeps working on a question nobody is waiting for.
    ///
    /// The source is deliberately not disposed: it owns no timer, and a token the host has already linked
    /// to something of its own must not start throwing <see cref="ObjectDisposedException"/>. Disposing this
    /// only detaches it from Monaco's token.
    /// </summary>
    internal sealed class MonacoCancellation : IDisposable
    {
        private readonly CancellationTokenSource _source = new CancellationTokenSource();
        private readonly IJsDisposable           _registration;

        public MonacoCancellation(ICancellationToken token)
        {
            if (token is null) return;

            if (token.isCancellationRequested)
            {
                _source.Cancel();
                return;
            }

            _registration = token.onCancellationRequested(() => _source.Cancel());
        }

        public CancellationToken Token => _source.Token;

        public bool IsCancellationRequested => _source.IsCancellationRequested;

        public void Dispose() => _registration?.dispose();
    }
}
