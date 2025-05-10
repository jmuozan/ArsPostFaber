using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Grasshopper.Kernel;
using Rhino;
using System.Text;
// removed CodeDOM-based compilation
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using System.Collections.Generic;

namespace LLM.OllamaComps
{
    public class ComponentCompilerComponent : GH_Component
    {
        public ComponentCompilerComponent()
          : base("Compile Component", "Compile",
              "Compiles a generated component code file into a DLL",
              "crft", "LLM") { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Compile", "C", "Trigger compilation", GH_ParamAccess.item, false);
            pManager.AddTextParameter("Code", "Code", "C# code to compile", GH_ParamAccess.item);
            pManager.AddTextParameter("Output Path", "P", "Path to save the compiled DLL (optional)", GH_ParamAccess.item, string.Empty);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Result", "R", "Compilation result", GH_ParamAccess.item);
            pManager.AddTextParameter("DLL Path", "D", "Path to the compiled DLL", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Success", "S", "Whether compilation was successful", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool compile = false;
            string code = string.Empty;
            string outputPath = string.Empty;
            DA.GetData("Compile", ref compile);
            if (!compile)
            {
                DA.SetData("Result", "Compilation not triggered");
                DA.SetData("Success", false);
                return;
            }
            if (!DA.GetData("Code", ref code)) return;
            DA.GetData("Output Path", ref outputPath);
            if (string.IsNullOrEmpty(code))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No code provided");
                DA.SetData("Result", "No code provided");
                DA.SetData("Success", false);
                return;
            }
            if (string.IsNullOrEmpty(outputPath))
            {
                string className = ExtractClassName(code);
                string temp = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "CompiledComponents");
                Directory.CreateDirectory(temp);
                outputPath = Path.Combine(temp, className + ".dll");
            }
            try
            {
                CompileCode(code, outputPath);
                DA.SetData("Result", "Compilation successful");
                DA.SetData("DLL Path", outputPath);
                DA.SetData("Success", true);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData("Result", "Compilation failed: " + ex.Message);
                DA.SetData("Success", false);
            }
        }

        private string ExtractClassName(string code)
        {
            var match = Regex.Match(code, @"public\s+class\s+(\w+)\s*[:{]");
            return match.Success ? match.Groups[1].Value : "GeneratedComponent";
        }

        private void CompileCode(string code, string outputPath)
        {
            // Use Roslyn to compile on all platforms
            // Parse the source
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            // Use all currently loaded assemblies as references (cross-platform)
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
        public override Guid ComponentGuid => new Guid("E2FA5D3B-4A7C-4C11-9B31-8EA0E4E3D293");
    }
}