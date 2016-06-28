// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Runtime.Versioning;
using Cake.Core.Polyfill;

namespace Cake.Core
{
    /// <summary>
    /// Represents the runtime that Cake is running in.
    /// </summary>
    public sealed class CakeRuntime : ICakeRuntime
    {
        private readonly FrameworkName _framework;
        private readonly bool _isCoreClr;
        private readonly Version _version;

        /// <summary>
        /// Gets the target .NET framework version that the current AppDomain is targeting.
        /// </summary>
        public FrameworkName TargetFramework
        {
            get { return _framework; }
        }

        /// <summary>
        /// Gets the version of Cake executing the script.
        /// </summary>
        public Version CakeVersion
        {
            get { return _version; }
        }

        /// <summary>
        /// Gets a value indicating whether we're running on CoreClr.
        /// </summary>
        /// <value>
        /// <c>true</c> if we're runnning on CoreClr; otherwise, <c>false</c>.
        /// </value>
        public bool IsCoreClr
        {
            get { return _isCoreClr; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CakeRuntime"/> class.
        /// </summary>
        public CakeRuntime()
        {
            _framework = EnvironmentHelper.GetFramework();
            _version = AssemblyHelper.GetExecutingAssembly().GetName().Version;
            _isCoreClr = EnvironmentHelper.IsCoreClr();
        }
    }
}
