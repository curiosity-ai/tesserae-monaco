using System;
using System.Collections.Generic;
using static Transpose.Core.dom;

namespace Tesserae.Monaco
{
    /// <summary>
    /// The Monaco disposables a component has accumulated - event subscriptions, provider registrations,
    /// actions - released together when the component is torn down.
    ///
    /// Monaco hands one back from nearly every <c>on...</c> and <c>register...</c> call, and disposing the
    /// editor does not release the ones that live on a global registry, so something has to hold them.
    ///
    /// What is held is a release closure rather than the handle itself: <see cref="IJsDisposable"/> is an
    /// <c>[External]</c> declaration, so nothing is emitted for it and it has no runtime type metadata -
    /// which a <c>List&lt;IJsDisposable&gt;</c> needs to construct itself, and fails on with
    /// "Cannot read properties of undefined (reading '$$name')". A list of <see cref="Action"/> has no such
    /// problem.
    ///
    /// Disposal is defensive: one handle that throws must not strand the rest.
    /// </summary>
    public sealed class DisposableBag
    {
        private readonly List<Action> _releases = new List<Action>();

        /// <summary>Takes ownership of a Monaco disposable. Null handles are ignored.</summary>
        public void Add(IJsDisposable disposable)
        {
            if (disposable is null) return;

            _releases.Add(() => disposable.dispose());
        }

        /// <summary>How many handles are held.</summary>
        public int Count => _releases.Count;

        /// <summary>Disposes everything held, then empties the bag.</summary>
        public void DisposeAll()
        {
            foreach (var release in _releases)
            {
                try
                {
                    release();
                }
                catch (Exception exception)
                {
                    console.error("Tesserae.Monaco: a Monaco disposable threw on release", exception);
                }
            }

            _releases.Clear();
        }
    }
}
