using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Grasshopper.Kernel;
using System.Text.Json;
using Brain.Templates;

namespace Brain.OllamaComps
{
    public class OllamaComponentGeneratorComponent : GH_Component_HTTPAsync
    {
        public OllamaComponentGeneratorComponent()
          : base("Generate GH Component", "GHGen",
              "Uses Ollama to generate a Grasshopper component based on a prompt",
              "Brain", "LLM") { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Generate", "G", "Generate the component?", GH_ParamAccess.item, false);
            pManager.AddTextParameter("Model", "M", "Ollama model name (e.g., deepseek-r1:1.5b)", GH_ParamAccess.item, "deepseek-r1:1.5b");
            pManager.AddTextParameter("Description", "D", "Description of the component you want to generate", GH_ParamAccess.item);
            pManager.AddTextParameter("Component Name", "N", "Name for the component (optional)", GH_ParamAccess.item, string.Empty);
            pManager[3].Optional = true;
            pManager.AddNumberParameter("Temperature", "T", "Temperature for generation (0-1)", GH_ParamAccess.item, 0.7);
            pManager.AddTextParameter("URL", "U", "Ollama API URL (default: http://localhost:11434/api/generate)", GH_ParamAccess.item, "http://localhost:11434/api/generate");
            pManager.AddIntegerParameter("Timeout", "TO", "Timeout for the request in ms", GH_ParamAccess.item, 60000);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Generated Code", "C", "C# code for the generated component", GH_ParamAccess.item);
            pManager.AddTextParameter("Component Class", "CC", "Component class name", GH_ParamAccess.item);
            pManager.AddTextParameter("Save Path", "P", "Path where the component was saved (if enabled)", GH_ParamAccess.item);
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
                        ProcessAndExtractCode(DA);
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

            string systemPrompt = "You are an expert C# programmer specializing in Grasshopper plugin development. I need you to create a complete, well-structured Grasshopper component based on the description I provide. Your response should ONLY include the complete C# code with no explanations. Follow these guidelines: 1. Use standard Grasshopper component patterns with RegisterInputParams, RegisterOutputParams, and SolveInstance methods 2. Include appropriate namespaces and error handling 3. Generate a unique GUID for the component using the format: new Guid(\"00000000-0000-0000-0000-000000000000\"). 4. Make sure the code is complete and can be directly compiled 5. DO NOT include any explanations or markdown - ONLY output the C# code file";

            string prompt = "Create a Grasshopper component that: " + description;
            if (!string.IsNullOrEmpty(componentName))
                prompt += $"\nThe component should be named: {componentName}";
            prompt += "\nUse the Brain.Templates namespace if appropriate for HTTP requests.";

            string body = $"{{\"model\":\"{model}\",\"system\":\"{systemPrompt.Replace("\"", "\\\"")}\",\"prompt\":\"{prompt.Replace("\"", "\\\"")}\",\"temperature\":{temperature},\"stream\":false}}";
            _currentState = RequestState.Requesting;
            this.Message = "Generating Component...";
            POSTAsync(url, body, "application/json", string.Empty, timeout);
        }

        private void ProcessAndExtractCode(IGH_DataAccess DA)
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
                string className = ExtractClassName(cleanCode);
                DA.SetData(0, cleanCode);
                DA.SetData(1, className);
                DA.SetData(2, SaveComponentCode(cleanCode, className));
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error processing generated code: " + ex.Message);
                DA.SetData(0, _response);
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

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("BF4C9D32-A84B-4BA2-BD5F-E75DEF32CC71");
    }
}