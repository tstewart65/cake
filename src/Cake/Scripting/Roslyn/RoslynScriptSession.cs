// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Cake.Core;
using Cake.Core.Configuration;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Core.Reflection;
using Cake.Core.Scripting;
using Cake.Core.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using IO = System.IO;

namespace Cake.Scripting.Roslyn
{
    internal sealed class RoslynScriptSession : IScriptSession
    {
        private readonly IScriptHost _host;
        private readonly IAssemblyLoader _loader;
        private readonly ICakeLog _log;
        private readonly CakeOptions _options;

        private readonly bool _scriptCacheEnabled;
        private readonly bool _scriptForceRecompile;
        private readonly DirectoryPath _scriptCachePath;

        public HashSet<FilePath> ReferencePaths { get; }

        public HashSet<Assembly> References { get; }

        public HashSet<string> Namespaces { get; }

        public RoslynScriptSession(IScriptHost host, IAssemblyLoader loader, ICakeLog log, CakeOptions options, ICakeConfiguration configuration)
        {
            _host = host;
            _loader = loader;
            _log = log;
            _options = options;

            ReferencePaths = new HashSet<FilePath>(PathComparer.Default);
            References = new HashSet<Assembly>();
            Namespaces = new HashSet<string>(StringComparer.Ordinal);

            var cacheEnabled = configuration.GetValue(Constants.Cache.Enabled) ?? "false";
            _scriptCacheEnabled = cacheEnabled.Equals("true", StringComparison.OrdinalIgnoreCase);
            _scriptCachePath = configuration.GetScriptCachePath(options.Script.GetDirectory(), host.Context.Environment);
            _scriptForceRecompile = options.ForceCacheRecompile;
        }

        public void AddReference(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }
            _log.Debug("Adding assembly reference to {0}...", new FilePath(assembly.Location).GetFilename().FullPath);
            References.Add(assembly);
        }

        public void AddReference(FilePath path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            _log.Debug("Adding reference to {0}...", path.GetFilename().FullPath);
#if NETCORE
            References.Add(_loader.Load(path, true));
#else
            ReferencePaths.Add(path);
#endif
        }

        public void ImportNamespace(string @namespace)
        {
            if (!string.IsNullOrWhiteSpace(@namespace) && !Namespaces.Contains(@namespace))
            {
                _log.Debug("Importing namespace {0}...", @namespace);
                Namespaces.Add(@namespace);
            }
        }

        public void Execute(Script script)
        {
            var scriptName = _options.Script.GetFilename();
            using (var sw = new DisposableStopwatch(_log, string.Format("{0} execution time", scriptName)))
            {
                var cacheDLLFileName = $"{scriptName}.dll";
                var cacheHashFileName = $"{scriptName}.hash";
                var cachedAssembly = _scriptCachePath.CombineWithFilePath(cacheDLLFileName).MakeAbsolute(_host.Context.Environment);
                var hashFile = _scriptCachePath.CombineWithFilePath(cacheHashFileName);
                string scriptHash = default;
                if (_scriptCacheEnabled && IO.File.Exists(cachedAssembly.FullPath) && !_scriptForceRecompile)
                {
                    _log.Verbose(cacheDLLFileName);
                    scriptHash = FastHash.GenerateHash(Encoding.UTF8.GetBytes(string.Concat(script.Lines)));
                    var cachedHash = IO.File.Exists(hashFile.FullPath) ? IO.File.ReadAllText(hashFile.FullPath) : string.Empty;
                    if (scriptHash.Equals(cachedHash, StringComparison.InvariantCultureIgnoreCase))
                    {
                        _log.Verbose("Running cached build script...");
                        RunScriptAssembly(cachedAssembly.FullPath);
                        return;
                    }
                    else
                    {
                        _log.Verbose("Cache check failed.");
                    }
                }
                Compilation compilation;
                Microsoft.CodeAnalysis.Scripting.Script<object> roslynScript;
                using (var sw1 = new DisposableStopwatch(_log, "Script compile time"))
                {
                    // Generate the script code.
                    var generator = new RoslynCodeGenerator();
                    var code = generator.Generate(script);

                    // Warn about any code generation excluded namespaces
                    foreach (var @namespace in script.ExcludedNamespaces)
                    {
                        _log.Warning("Namespace {0} excluded by code generation, affected methods:\r\n\t{1}",
                            @namespace.Key, string.Join("\r\n\t", @namespace.Value));
                    }

                    // Create the script options dynamically.
                    var options = Microsoft.CodeAnalysis.Scripting.ScriptOptions.Default
                        .AddImports(Namespaces.Except(script.ExcludedNamespaces.Keys))
                        .AddReferences(References)
                        .AddReferences(ReferencePaths.Select(r => r.FullPath))
                        .WithEmitDebugInformation(_options.PerformDebug)
                        .WithMetadataResolver(Microsoft.CodeAnalysis.Scripting.ScriptMetadataResolver.Default);

                    roslynScript = CSharpScript.Create(code, options, _host.GetType());

                    _log.Verbose("Compiling build script...");
                    compilation = roslynScript.GetCompilation();
                    var diagnostics = compilation.GetDiagnostics();

                    var errors = new List<Diagnostic>();

                    foreach (var diagnostic in diagnostics)
                    {
                        switch (diagnostic.Severity)
                        {
                            case DiagnosticSeverity.Info:
                                _log.Information(diagnostic.ToString());
                                break;
                            case DiagnosticSeverity.Warning:
                                _log.Warning(diagnostic.ToString());
                                break;
                            case DiagnosticSeverity.Error:
                                _log.Error(diagnostic.ToString());
                                errors.Add(diagnostic);
                                break;
                            default:
                                break;
                        }
                    }

                    if (errors.Any())
                    {
                        var errorMessages = string.Join(Environment.NewLine, errors.Select(x => x.ToString()));
                        var message = string.Format(CultureInfo.InvariantCulture, "Error(s) occurred when compiling build script:{0}{1}", Environment.NewLine, errorMessages);
                        throw new CakeException(message);
                    }
                }
                if (_scriptCacheEnabled)
                {
                    // Verify cache directory exists
                    if (!IO.Directory.Exists(_scriptCachePath.FullPath))
                    {
                        IO.Directory.CreateDirectory(_scriptCachePath.FullPath);
                    }
                    if (string.IsNullOrEmpty(scriptHash))
                    {
                        scriptHash = FastHash.GenerateHash(Encoding.UTF8.GetBytes(string.Concat(script.Lines)));
                    }
                    var emitResult = compilation.Emit(cachedAssembly.FullPath);

                    if (emitResult.Success)
                    {
                        IO.File.WriteAllText(hashFile.FullPath, scriptHash);
                        RunScriptAssembly(cachedAssembly.FullPath);
                    }
                }
                else
                {
                    using (new ScriptAssemblyResolver(_log))
                    {
                        roslynScript.RunAsync(_host).Wait();
                    }
                }
            }
        }

        private void RunScriptAssembly(string assemblyPath)
        {
            var assembly = Assembly.LoadFile(assemblyPath);
            var type = assembly.GetType("Submission#0");
            var factoryMethod = type.GetMethod("<Factory>", new[] { typeof(object[]) });
            using (new ScriptAssemblyResolver(_log))
            {
                try
                {
                    var task = (System.Threading.Tasks.Task<object>)factoryMethod.Invoke(null, new object[] { new object[] { _host, null } });
                    task.Wait();
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException;
                }
            }
        }
    }
}