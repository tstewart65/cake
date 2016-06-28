// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// NOTE: Portions of this code was taken from the ScriptCS project 
// which is licensed under the MIT license.

#if NETCORE
using Cake.Core.Diagnostics;
using Cake.Core.Scripting;
using Cake.Scripting.Roslyn;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using System.Linq;
using System;
using Cake.Core;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Globalization;

namespace Cake.Scripting.XPlat
{
    internal sealed class DebugXPlatScriptSession : XPlatScriptSession
    {
        private readonly IScriptHost _host;
        private readonly ICakeLog _log;

        private const string CompiledType = "Submission#0";
        private const string CompiledMethod = "<Factory>";

        public DebugXPlatScriptSession(IScriptHost host, ICakeLog log) 
            : base(host, log)
        {
            _host = host;
            _log = log;
        }

        public override void Execute(Script script)
        {
            // Generate the script code.
            var generator = new RoslynCodeGenerator();
            var code = generator.Generate(script);

            // Create the script options dynamically.
            var options = Microsoft.CodeAnalysis.Scripting.ScriptOptions.Default
                .AddImports(Namespaces)
                .AddReferences(References)
                .AddReferences(ReferencePaths.Select(r => r.FullPath));

            var roslynScript = CSharpScript.Create(code, options, _host.GetType());
            var compilation = roslynScript.GetCompilation();
            compilation = compilation.WithOptions(compilation.Options
                .WithOptimizationLevel(Microsoft.CodeAnalysis.OptimizationLevel.Debug)
                .WithOutputKind(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

            using (var assemblyStream = new MemoryStream())
            using (var symbolStream = new MemoryStream())
            {
                _log.Verbose("Compiling build script for debugging...");

                var result = compilation.Emit(assemblyStream, pdbStream: symbolStream);
                if (result.Success)
                {
                    var assembly = AssemblyLoadContext.Default.LoadFromStream(assemblyStream, symbolStream);
                    var type = assembly.GetType(CompiledType);
                    var method = type.GetMethod(CompiledMethod, BindingFlags.Static | BindingFlags.Public);

                    var submissionStates = new object[2];
                    submissionStates[0] = _host;

                    method.Invoke(null, new[] { submissionStates });
                }
                else
                {
                    var errors = string.Join(Environment.NewLine, result.Diagnostics.Select(x => x.ToString()));
                    var message = string.Format(CultureInfo.InvariantCulture, "Error occurred when compiling: {0}", errors);
                    throw new CakeException(message);
                }
            };
        }
    }
}
#endif