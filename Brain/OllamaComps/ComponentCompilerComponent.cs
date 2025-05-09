using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Grasshopper.Kernel;
using Rhino;
using System.Text;
using Microsoft.CSharp;

namespace Brain.OllamaComps
{
    public class ComponentCompilerComponent : GH_Component
    {
        public ComponentCompilerComponent()
          : base("Compile Component", "Compile",
              "Compiles a generated component code file into a DLL",
              "Brain", "LLM") { }

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
            var provider = new CSharpCodeProvider();
            var parameters = new CompilerParameters
            {
                GenerateExecutable = false,
                OutputAssembly = outputPath,
                GenerateInMemory = false
            };
            parameters.ReferencedAssemblies.Add("System.dll");
            parameters.ReferencedAssemblies.Add("System.Core.dll");
            parameters.ReferencedAssemblies.Add("System.Drawing.dll");
            string rhinoDir = Path.GetDirectoryName(typeof(Rhino.RhinoApp).Assembly.Location) ?? string.Empty;
            parameters.ReferencedAssemblies.Add(Path.Combine(rhinoDir, "Rhino.dll"));
            parameters.ReferencedAssemblies.Add(Path.Combine(rhinoDir, "Grasshopper.dll"));
            parameters.ReferencedAssemblies.Add(Path.Combine(rhinoDir, "GH_IO.dll"));
            parameters.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);
            CompilerResults results = provider.CompileAssemblyFromSource(parameters, code);
            if (results.Errors.HasErrors)
            {
                var errors = new StringBuilder("Compilation errors:");
                foreach (CompilerError error in results.Errors)
                    errors.AppendLine($"Line {error.Line}: {error.ErrorText}");
                throw new Exception(errors.ToString());
            }
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("E2FA5D3B-4A7C-4C11-9B31-8EA0E4E3D293");
    }
}