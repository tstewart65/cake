// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if NETCORE
using System;
using System.Reflection;
using Cake.Core.IO;
using Cake.Core.Scripting;
using System.Collections.Generic;
using Cake.Core.Diagnostics;

namespace Cake.Scripting.XPlat
{
    internal abstract class XPlatScriptSession : IScriptSession
    {
        private readonly IScriptHost _host;
        private readonly ICakeLog _log;
        private readonly HashSet<FilePath> _referencePaths;
        private readonly HashSet<Assembly> _references;
        private readonly HashSet<string> _namespaces;

        public HashSet<FilePath> ReferencePaths
        {
            get { return _referencePaths; }
        }

        public HashSet<Assembly> References
        {
            get { return _references; }
        }

        public HashSet<string> Namespaces
        {
            get { return _namespaces; }
        }

        protected XPlatScriptSession(IScriptHost host, ICakeLog log)
        {
            _host = host;
            _log = log;
            _referencePaths = new HashSet<FilePath>(PathComparer.Default);
            _references = new HashSet<Assembly>();
            _namespaces = new HashSet<string>(StringComparer.Ordinal);
        }

        public void AddReference(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException("assembly");
            }
            _log.Debug("Adding reference to {0}...", new FilePath(assembly.Location).GetFilename().FullPath);
            _references.Add(assembly);
        }

        public void AddReference(FilePath path)
        {
            if (path == null)
            {
                throw new ArgumentNullException("path");
            }
            _log.Debug("Adding reference to {0}...", path.GetFilename().FullPath);
            _referencePaths.Add(path);
        }

        public void ImportNamespace(string @namespace)
        {
            if (!_namespaces.Contains(@namespace))
            {
                _log.Debug("Importing namespace {0}...", @namespace);
                _namespaces.Add(@namespace);
            }
        }

        public abstract void Execute(Script script);
    }
}
#endif