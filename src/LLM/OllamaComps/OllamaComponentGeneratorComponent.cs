using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Grasshopper.Kernel;
using System.Text.Json;
// removed CodeDOM-based compilation
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using System.Collections.Generic;
using Rhino;
using LLM.Templates;
using Grasshopper.GUI.Canvas;
using System.Drawing;

namespace LLM.OllamaComps
{
    public class OllamaComponentMakerComponent : GH_Component_HTTPAsync
    {
        public OllamaComponentMakerComponent()
          : base("Create GH Component", "GHCreate",
              "Generates and compiles a Grasshopper component via Ollama",
              "crft", "LLM") { }
        /// <summary>
        /// Automatically add and wire an OllamaModelParam dropdown on placement.
        /// </summary>
        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            // If already wired, skip
            if (Params.Input.Count > 1 && Params.Input[1].SourceCount > 0) return;
            // Create and wire model dropdown list
            var list = new OllamaModelParam();
            document.AddObject(list, false);
            document.NewSolution(false);
            // Connect dropdown to Model input
            if (Params.Input.Count > 1)
                Params.Input[1].AddSource(list);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Generate", "G", "Toggle to generate and compile component", GH_ParamAccess.item, false);
            pManager.AddParameter(new OllamaModelParam(), "Model", "M", "Ollama model to use", GH_ParamAccess.item);
            pManager.AddTextParameter("Description", "D", "Natural-language description of the component", GH_ParamAccess.item);
            pManager.AddTextParameter("Component Name", "N", "Optional component class/name", GH_ParamAccess.item, string.Empty);
            pManager[3].Optional = true;
            pManager.AddTextParameter("Category", "C", "Grasshopper ribbon category", GH_ParamAccess.item, "crft");
            pManager.AddTextParameter("Subcategory", "S", "Grasshopper ribbon subcategory", GH_ParamAccess.item, "LLM");
            pManager.AddNumberParameter("Temperature", "T", "Generation temperature (0-1)", GH_ParamAccess.item, 0.7);
            pManager.AddIntegerParameter("Max Tokens", "MT", "Maximum number of tokens to generate", GH_ParamAccess.item, 2048);
            pManager.AddTextParameter("URL", "U", "Ollama API endpoint", GH_ParamAccess.item, "http://localhost:11434/api/generate");
            pManager.AddIntegerParameter("Timeout", "TO", "Request timeout (ms)", GH_ParamAccess.item, 60000);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Generated Code", "C", "Generated C# code", GH_ParamAccess.item);
            pManager.AddTextParameter("Component Class", "CC", "Generated component class name", GH_ParamAccess.item);
            pManager.AddTextParameter("Source Path", "CS", "File path of saved .cs source", GH_ParamAccess.item);
            pManager.AddTextParameter("Assembly Path", "DLL", "File path of compiled .gha assembly", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Success", "S", "Compilation success", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (_shouldExpire)
            {
                switch (_currentState)
                {
                    case RequestState.Off:
                        this.Message = "Inactive";
                        _currentState = RequestState.Idle;
                        break;
                    case RequestState.Error:
                        this.Message = "ERROR";
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _response);
                        _currentState = RequestState.Idle;
                        break;
                    case RequestState.Done:
                        this.Message = "Complete!";
                        _currentState = RequestState.Idle;
                        ProcessAndCompileCode(DA);
                        break;
                }
                _shouldExpire = false;
                return;
            }

            bool active = false;
            string model = string.Empty;
            string description = string.Empty;
            string componentName = string.Empty;
            double temperature = 0.7;
            string url = string.Empty;
            int timeout = 60000;

            if (!DA.GetData("Generate", ref active)) return;
            if (!active)
            {
                _currentState = RequestState.Off;
                _shouldExpire = true;
                _response = string.Empty;
                ExpireSolution(true);
                return;
            }
            if (!DA.GetData("Model", ref model)) return;
            if (!DA.GetData("Description", ref description)) return;
            DA.GetData("Component Name", ref componentName);
            DA.GetData("Temperature", ref temperature);
            if (!DA.GetData("URL", ref url)) return;
            DA.GetData("Timeout", ref timeout);
            int maxTokens = 2048;
            DA.GetData("Max Tokens", ref maxTokens);

            if (string.IsNullOrEmpty(url))
            {
                _response = "Empty URL";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }
            if (string.IsNullOrEmpty(description))
            {
                _response = "Empty component description";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }

            // Combine system instructions with user description into a single prompt
            string systemPrompt = "You are an expert C# programmer specializing in Grasshopper plugin development. " +
                                   "Create a complete, well-structured Grasshopper component based on the description provided. " +
                                   "Respond with only the full C# code for the component (no explanations or markdown).";
            // Build user prompt
            string userPrompt = "Component description: " + description;
            if (!string.IsNullOrEmpty(componentName))
                userPrompt += $"\nComponent name: {componentName}";
            // Merge into one prompt for Ollama
            string fullPrompt = systemPrompt + "\n" + userPrompt;
            // Prepare request body for Ollama /api/generate
            // Build JSON payload using serializer to handle escaping
            var requestPayload = new
            {
                model = model,
                prompt = fullPrompt,
                max_tokens = maxTokens,
                temperature = temperature,
                stream = false
            };
            string body = JsonSerializer.Serialize(requestPayload);
            _currentState = RequestState.Requesting;
            this.Message = "Generating Component...";
            POSTAsync(url, body, "application/json", string.Empty, timeout);
        }

        private void ProcessAndCompileCode(IGH_DataAccess DA)
        {
            try
            {
                using var jsonDocument = JsonDocument.Parse(_response);
                var generatedText = jsonDocument.RootElement.GetProperty("response").GetString() ?? string.Empty;
                string cleanCode = generatedText.Trim();
                if (cleanCode.StartsWith("```"))
                    cleanCode = cleanCode[(cleanCode.IndexOf('\n') + 1)..];
                if (cleanCode.EndsWith("```"))
                    cleanCode = cleanCode[..cleanCode.LastIndexOf("```")];
                cleanCode = cleanCode.Trim();
                // Remove any leading/trailing HTML/XML tags or metadata lines (e.g., <think> wrappers)
                var lines = cleanCode.Split(new[] {'\r','\n'}, StringSplitOptions.RemoveEmptyEntries).ToList();
                // Strip leading tags
                while (lines.Count > 0 && lines[0].TrimStart().StartsWith("<") && lines[0].Contains(">"))
                    lines.RemoveAt(0);
                // Strip trailing tags
                while (lines.Count > 0 && lines[^1].TrimEnd().StartsWith("<") && lines[^1].Contains(">"))
                    lines.RemoveAt(lines.Count - 1);
                cleanCode = string.Join("\n", lines).Trim();
                string className = ExtractClassName(cleanCode);
                string csPath = SaveComponentCode(cleanCode, className);
                string assemblyPath = Path.ChangeExtension(csPath, ".gha");
                bool success = true;
                try
                {
                    CompileCode(cleanCode, assemblyPath);
                }
                catch (Exception exCompile)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Compilation failed: " + exCompile.Message);
                    assemblyPath = string.Empty;
                    success = false;
                }
                DA.SetData(0, cleanCode);
                DA.SetData(1, className);
                DA.SetData(2, csPath);
                DA.SetData(3, assemblyPath);
                DA.SetData(4, success);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error processing response: " + ex.Message);
                DA.SetData(0, _response);
                DA.SetData(4, false);
            }
        }

        private string ExtractClassName(string code)
        {
            var match = Regex.Match(code, @"public\s+class\s+(\w+)\s*:");
            return match.Success ? match.Groups[1].Value : "UnknownComponent";
        }

        private string SaveComponentCode(string code, string className)
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                string dir = Path.Combine(pluginDir, "GeneratedComponents");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, className + ".cs");
                File.WriteAllText(path, code, Encoding.UTF8);
                return path;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to save component: " + ex.Message);
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Compiles C# source code into a Grasshopper plugin assembly (.gha).
        /// </summary>
        private void CompileCode(string code, string outputPath)
        {
            // Use Roslyn for cross-platform compilation
            // Parse the source
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            // Gather references from loaded assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location) && File.Exists(a.Location))
                .Select(a => a.Location)
                .Distinct();
            var refs = assemblies.Select(path => MetadataReference.CreateFromFile(path)).ToList();
            // Explicitly ensure Grasshopper, GH_IO, and plugin assembly are referenced
            try
            {
                var ghLoc = typeof(Grasshopper.Kernel.GH_Component).Assembly.Location;
                if (File.Exists(ghLoc)) refs.Add(MetadataReference.CreateFromFile(ghLoc));
                var ghIoAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name.Equals("GH_IO", StringComparison.OrdinalIgnoreCase));
                if (ghIoAsm != null && File.Exists(ghIoAsm.Location))
                    refs.Add(MetadataReference.CreateFromFile(ghIoAsm.Location));
                var pluginAsm = Assembly.GetExecutingAssembly().Location;
                if (File.Exists(pluginAsm)) refs.Add(MetadataReference.CreateFromFile(pluginAsm));
            }
            catch { /* best-effort references */ }
            // Create compilation
            var compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(outputPath),
                new[] { syntaxTree },
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var result = compilation.Emit(outputPath);
            if (!result.Success)
            {
                var failures = result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
                var sb = new StringBuilder("Compilation failed:");
                foreach (var diag in failures)
                    sb.AppendLine(diag.ToString());
                throw new Exception(sb.ToString());
            }
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("BF4C9D32-A84B-4BA2-BD5F-E75DEF32CC71");
    }
}