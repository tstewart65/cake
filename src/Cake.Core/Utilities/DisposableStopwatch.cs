// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;

using Cake.Core.Diagnostics;

namespace Cake.Core.Utilities
{
    /// <summary>
    /// Disposable Stopwatch used for timing how long a section of code takes and writing the result to the log.
    /// </summary>
    public class DisposableStopwatch : IDisposable
    {
        private readonly Stopwatch _sw;
        private readonly ICakeLog _log;
        private readonly string _message;

        /// <summary>
        /// Initializes a new instance of the <see cref="DisposableStopwatch"/> class.
        /// </summary>
        /// <param name="log">logger.</param>
        /// <param name="message">message.</param>
        public DisposableStopwatch(ICakeLog log, string message)
        {
            _log = log;
            _message = message;
            _sw = Stopwatch.StartNew();
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            if (_sw != null)
            {
                _sw.Stop();
                _log.Verbose("{0}: {1}ms", _message, _sw.ElapsedMilliseconds);
            }
        }
    }
}
