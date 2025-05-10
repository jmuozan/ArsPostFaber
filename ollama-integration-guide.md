# Ollama Integration Guide for Brain Plugin

This guide outlines how to integrate [Ollama](https://ollama.ai/) with the Brain plugin for Grasshopper, allowing you to interact with local LLM models and even generate Grasshopper components through natural language prompts.

## Table of Contents

- [Overview](#overview)
- [Implementation](#implementation)
  - [Basic Ollama Component](#basic-ollama-component)
  - [Response Parser Component](#response-parser-component)
  - [Component Generator](#component-generator)
  - [Component Compiler](#component-compiler)
- [Usage Workflow](#usage-workflow)
- [Limitations and Considerations](#limitations-and-considerations)

## Overview

The Ollama integration extends the Brain plugin with the ability to:

1. **Send prompts to locally running Ollama models** (like deepseek-r1:1.5b)
2. **Parse responses** from the Ollama API
3. **Generate Grasshopper component code** based on natural language descriptions
4. **Compile generated components** for use in Grasshopper

These capabilities build on the existing HTTP request infrastructure in the Brain plugin, utilizing the `GH_Component_HTTPAsync` template for asynchronous API calls.

## Implementation

### Basic Ollama Component

The first component (`OllamaGenerateComponent`) provides a simple interface to the Ollama API:

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using Grasshopper.Kernel;
using Brain.Templates;
using Rhino.Geometry;

namespace Brain.OllamaComps
{
    public class OllamaGenerateComponent : GH_Component_HTTPAsync
    {
        /// <summary>
        /// Initializes a new instance of the OllamaGenerateComponent class.
        /// </summary>
        public OllamaGenerateComponent()
          : base("Ollama Generate", "Ollama",
              "Generates text using a locally running Ollama model",
              "Brain", "LLM")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // active
            pManager.AddBooleanParameter("Send", "S", "Perform the request?", GH_ParamAccess.item, false);
            // model name
            pManager.AddTextParameter("Model", "M", "Ollama model name (e.g., deepseek-r1:1.5b)", GH_ParamAccess.item, "deepseek-r1:1.5b");
            // prompt
            pManager.AddTextParameter("Prompt", "P", "Prompt to send to the model", GH_ParamAccess.item);
            // temperature
            pManager.AddNumberParameter("Temperature", "T", "Temperature for generation (0-1)", GH_ParamAccess.item, 0.7);
            // max tokens
            pManager.AddIntegerParameter("Max Tokens", "MT", "Maximum number of tokens to generate", GH_ParamAccess.item, 2048);
            // url
            pManager.AddTextParameter("URL", "U", "Ollama API URL (default: http://localhost:11434/api/generate)", GH_ParamAccess.item, "http://localhost:11434/api/generate");
            // timeout
            pManager.AddIntegerParameter("Timeout", "TO", "Timeout for the request in ms", GH_ParamAccess.item, 30000);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Response", "R", "Generated text response", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
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
                        break;
                }
                // Output
                DA.SetData(0, _response);
                _shouldExpire = false;
                return;
            }

            bool active = false;
            string model = "";
            string prompt = "";
            double temperature = 0.7;
            int maxTokens = 2048;
            string url = "";
            int timeout = 30000;

            DA.GetData("Send", ref active);
            if (!active)
            {
                _currentState = RequestState.Off;
                _shouldExpire = true;
                _response = "";
                ExpireSolution(true);
                return;
            }

            if (!DA.GetData("Model", ref model)) return;
            if (!DA.GetData("Prompt", ref prompt)) return;
            DA.GetData("Temperature", ref temperature);
            DA.GetData("Max Tokens", ref maxTokens);
            if (!DA.GetData("URL", ref url)) return;
            if (!DA.GetData("Timeout", ref timeout)) return;

            // Validity checks
            if (url == null || url.Length == 0)
            {
                _response = "Empty URL";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }

            if (prompt == null || prompt.Length == 0)
            {
                _response = "Empty prompt";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }

            // Prepare request body
            string body = $"{{\"model\": \"{model}\", \"prompt\": \"{prompt.Replace("\"", "\\\"")}\", \"temperature\": {temperature}, \"max_tokens\": {maxTokens}, \"stream\": false}}";

            _currentState = RequestState.Requesting;
            this.Message = "Requesting...";

            // Send POST request
            POSTAsync(url, body, "application/json", "", timeout);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return null; // You can add a custom icon here
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("8B267D39-DC46-4F4F-8D91-6F0C3F1B8A2C"); }
        }
    }
}
```

### Response Parser Component

To extract useful information from Ollama's JSON response:

```csharp
using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using System.Text.Json;

namespace Brain.OllamaComps
{
    public class OllamaResponseParserComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the OllamaResponseParserComponent class.
        /// </summary>
        public OllamaResponseParserComponent()
          : base("Parse Ollama Response", "OllamaParser",
              "Parses the JSON response from Ollama",
              "Brain", "LLM")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Response", "R", "JSON response from Ollama", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Generated Text", "T", "The generated text", GH_ParamAccess.item);
            pManager.AddTextParameter("Model", "M", "Model used for generation", GH_ParamAccess.item);
            pManager.AddNumberParameter("Total Duration", "D", "Total time taken in seconds", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Prompt Tokens", "PT", "Number of tokens in the prompt", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Total Tokens", "TT", "Total number of tokens used", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string response = "";
            if (!DA.GetData(0, ref response)) return;
            
            if (string.IsNullOrEmpty(response))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Empty response");
                return;
            }

            try
            {
                var jsonDocument = JsonDocument.Parse(response);
                var root = jsonDocument.RootElement;

                // Extract main values
                string generatedText = "";
                string model = "";
                double totalDuration = 0;
                int promptTokens = 0;
                int totalTokens = 0;

                if (root.TryGetProperty("response", out var responseElement))
                {
                    generatedText = responseElement.GetString();
                }

                if (root.TryGetProperty("model", out var modelElement))
                {
                    model = modelElement.GetString();
                }

                if (root.TryGetProperty("total_duration", out var durationElement))
                {
                    totalDuration = durationElement.GetInt64() / 1000000000.0; // Convert nanoseconds to seconds
                }

                if (root.TryGetProperty("prompt_eval_count", out var promptEvalElement))
                {
                    promptTokens = promptEvalElement.GetInt32();
                }

                if (root.TryGetProperty("eval_count", out var evalElement))
                {
                    int generatedTokens = evalElement.GetInt32();
                    totalTokens = promptTokens + generatedTokens;
                }

                // Output values
                DA.SetData(0, generatedText);
                DA.SetData(1, model);
                DA.SetData(2, totalDuration);
                DA.SetData(3, promptTokens);
                DA.SetData(4, totalTokens);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error parsing response: " + ex.Message);
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("D9CA5E85-7A23-4B5F-A018-C9D48FA5B3F1"); }
        }
    }
}
```

### Component Generator

This component uses Ollama to generate C# code for new Grasshopper components based on natural language descriptions:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Grasshopper.Kernel;
using Brain.Templates;
using Rhino.Geometry;

namespace Brain.OllamaComps
{
    public class OllamaComponentGeneratorComponent : GH_Component_HTTPAsync
    {
        /// <summary>
        /// Initializes a new instance of the OllamaComponentGeneratorComponent class.
        /// </summary>
        public OllamaComponentGeneratorComponent()
          : base("Generate GH Component", "GHGen",
              "Uses Ollama to generate a Grasshopper component based on a prompt",
              "Brain", "LLM")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // active
            pManager.AddBooleanParameter("Generate", "G", "Generate the component?", GH_ParamAccess.item, false);
            // model name
            pManager.AddTextParameter("Model", "M", "Ollama model name (e.g., deepseek-r1:1.5b)", GH_ParamAccess.item, "deepseek-r1:1.5b");
            // description
            pManager.AddTextParameter("Description", "D", "Description of the component you want to generate", GH_ParamAccess.item);
            // component name
            pManager.AddTextParameter("Component Name", "N", "Name for the component (optional)", GH_ParamAccess.item, "");
            pManager[3].Optional = true;
            // temperature
            pManager.AddNumberParameter("Temperature", "T", "Temperature for generation (0-1)", GH_ParamAccess.item, 0.7);
            // url
            pManager.AddTextParameter("URL", "U", "Ollama API URL (default: http://localhost:11434/api/generate)", GH_ParamAccess.item, "http://localhost:11434/api/generate");
            // timeout
            pManager.AddIntegerParameter("Timeout", "TO", "Timeout for the request in ms", GH_ParamAccess.item, 60000);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Generated Code", "C", "C# code for the generated component", GH_ParamAccess.item);
            pManager.AddTextParameter("Component Class", "CC", "Component class name", GH_ParamAccess.item);
            pManager.AddTextParameter("Save Path", "P", "Path where the component was saved (if enabled)", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
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
                        
                        // Process the response to extract the code and save it
                        ProcessAndExtractCode(DA);
                        break;
                }
                _shouldExpire = false;
                return;
            }

            bool active = false;
            string model = "";
            string description = "";
            string componentName = "";
            double temperature = 0.7;
            string url = "";
            int timeout = 60000;

            DA.GetData("Generate", ref active);
            if (!active)
            {
                _currentState = RequestState.Off;
                _shouldExpire = true;
                _response = "";
                ExpireSolution(true);
                return;
            }

            if (!DA.GetData("Model", ref model)) return;
            if (!DA.GetData("Description", ref description)) return;
            DA.GetData("Component Name", ref componentName);
            DA.GetData("Temperature", ref temperature);
            if (!DA.GetData("URL", ref url)) return;
            if (!DA.GetData("Timeout", ref timeout)) return;

            // Validity checks
            if (url == null || url.Length == 0)
            {
                _response = "Empty URL";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }

            if (description == null || description.Length == 0)
            {
                _response = "Empty component description";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }

            // Prepare a system prompt that explains how to create a GH component
            string systemPrompt = @"You are an expert C# programmer specializing in Grasshopper plugin development. 
I need you to create a complete, well-structured Grasshopper component based on the description I provide. 
Your response should ONLY include the complete C# code with no explanations.
Follow these guidelines:
1. Use standard Grasshopper component patterns with RegisterInputParams, RegisterOutputParams, and SolveInstance methods
2. Include appropriate namespaces and error handling
3. Generate a unique GUID for the component using the format: new Guid(""00000000-0000-0000-0000-000000000000"")
4. Make sure the code is complete and can be directly compiled
5. DO NOT include any explanations or markdown - ONLY output the C# code file";

            // Build a prompt that includes the component name if provided
            string prompt = "Create a Grasshopper component that: " + description;
            if (!string.IsNullOrEmpty(componentName))
            {
                prompt += $"\nThe component should be named: {componentName}";
            }
            
            prompt += "\nUse the Brain.Templates namespace if appropriate for HTTP requests.";

            // Prepare request body with system and user prompts
            string body = $"{{\"model\": \"{model}\", \"system\": \"{systemPrompt.Replace("\"", "\\\"")}\", \"prompt\": \"{prompt.Replace("\"", "\\\"")}\", \"temperature\": {temperature}, \"stream\": false}}";

            _currentState = RequestState.Requesting;
            this.Message = "Generating Component...";

            // Send POST request
            POSTAsync(url, body, "application/json", "", timeout);
        }

        private void ProcessAndExtractCode(IGH_DataAccess DA)
        {
            try
            {
                // Parse the JSON response
                var jsonDocument = System.Text.Json.JsonDocument.Parse(_response);
                var generatedText = jsonDocument.RootElement.GetProperty("response").GetString();
                
                // Clean up the code - remove any markdown formatting if present
                string cleanCode = generatedText;
                if (cleanCode.StartsWith("```csharp"))
                {
                    cleanCode = cleanCode.Substring(cleanCode.IndexOf('\n') + 1);
                }
                if (cleanCode.EndsWith("```"))
                {
                    cleanCode = cleanCode.Substring(0, cleanCode.LastIndexOf("```"));
                }
                
                cleanCode = cleanCode.Trim();
                
                // Try to extract the class name
                string className = ExtractClassName(cleanCode);
                
                // Output the code
                DA.SetData(0, cleanCode);
                DA.SetData(1, className);
                
                // Optional: Save the component to a file
                string path = SaveComponentCode(cleanCode, className);
                DA.SetData(2, path);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error processing generated code: " + ex.Message);
                DA.SetData(0, _response);
            }
        }
        
        private string ExtractClassName(string code)
        {
            // Simple regex to extract class name
            var match = System.Text.RegularExpressions.Regex.Match(code, @"public\s+class\s+(\w+)\s*:");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return "UnknownComponent";
        }
        
        private string SaveComponentCode(string code, string className)
        {
            try
            {
                // Create Components directory if it doesn't exist
                string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string componentsDir = Path.Combine(pluginDirectory, "GeneratedComponents");
                Directory.CreateDirectory(componentsDir);
                
                // Save the code to a file
                string filePath = Path.Combine(componentsDir, $"{className}.cs");
                File.WriteAllText(filePath, code);
                return filePath;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to save component: " + ex.Message);
                return "Failed to save";
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("BF4C9D32-A84B-4BA2-BD5F-E75DEF32CC71"); }
        }
    }
}
```

### Component Compiler

This component attempts to compile generated component code into a DLL:

```csharp
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Grasshopper.Kernel;
using Microsoft.CSharp;

namespace Brain.OllamaComps
{
    public class ComponentCompilerComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ComponentCompilerComponent class.
        /// </summary>
        public ComponentCompilerComponent()
          : base("Compile Component", "Compile",
              "Compiles a generated component code file into a DLL",
              "Brain", "LLM")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Compile", "C", "Trigger compilation", GH_ParamAccess.item, false);
            pManager.AddTextParameter("Code", "Code", "C# code to compile", GH_ParamAccess.item);
            pManager.AddTextParameter("Output Path", "P", "Path to save the compiled DLL (optional)", GH_ParamAccess.item, "");
            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Result", "R", "Compilation result", GH_ParamAccess.item);
            pManager.AddTextParameter("DLL Path", "D", "Path to the compiled DLL", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Success", "S", "Whether compilation was successful", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool compile = false;
            string code = "";
            string outputPath = "";

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

            // If no output path provided, create one in a temp directory
            if (string.IsNullOrEmpty(outputPath))
            {
                string className = ExtractClassName(code);
                string tempPath = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "CompiledComponents");
                Directory.CreateDirectory(tempPath);
                outputPath = Path.Combine(tempPath, $"{className}.dll");
            }

            // Compile the code
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
            var match = System.Text.RegularExpressions.Regex.Match(code, @"public\s+class\s+(\w+)\s*:");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return "GeneratedComponent";
        }

        private void CompileCode(string code, string outputPath)
        {
            // This is a simplified version - in practice you would need to reference
            // all the required assemblies and handle more compilation options
            var provider = new CSharpCodeProvider();
            var parameters = new CompilerParameters
            {
                GenerateExecutable = false,
                OutputAssembly = outputPath,
                GenerateInMemory = false
            };

            // Add references to required assemblies
            parameters.ReferencedAssemblies.Add("System.dll");
            parameters.ReferencedAssemblies.Add("System.Core.dll");
            parameters.ReferencedAssemblies.Add("System.Drawing.dll");
            
            // Add references to Rhino and Grasshopper assemblies
            string rhinoDir = Path.GetDirectoryName(typeof(Rhino.RhinoApp).Assembly.Location);
            parameters.ReferencedAssemblies.Add(Path.Combine(rhinoDir, "Rhino.dll"));
            parameters.ReferencedAssemblies.Add(Path.Combine(rhinoDir, "Grasshopper.dll"));
            parameters.ReferencedAssemblies.Add(Path.Combine(rhinoDir, "GH_IO.dll"));
            
            // Add reference to the Brain plugin
            parameters.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);

            CompilerResults results = provider.CompileAssemblyFromSource(parameters, code);

            if (results.Errors.HasErrors)
            {
                string errorMessage = "Compilation errors:";
                foreach (CompilerError error in results.Errors)
                {
                    errorMessage += $"\nLine {error.Line}: {error.ErrorText}";
                }
                throw new Exception(errorMessage);
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("E2FA5D3B-4A7C-4C11-9B31-8EA0E4E3D293"); }
        }
    }
}
```

## Usage Workflow

Here's how to use these components to generate and implement new Grasshopper components with Ollama:

1. **Install and Run Ollama**
   - Download and install Ollama from [ollama.ai](https://ollama.ai/)
   - Run Ollama and pull the deepseek-r1:1.5b model (or another model of your choice)
   - Example command: `ollama pull deepseek-r1:1.5b`

2. **Text Generation**
   - Use the `OllamaGenerateComponent` to interact with your local Ollama model
   - Provide a prompt and set the appropriate parameters
   - Click the "Send" toggle to execute the request

3. **Component Generation**
   - Use the `OllamaComponentGeneratorComponent` to create a Grasshopper component
   - Provide a natural language description of the component you want
   - Example: "Create a component that takes a circle and returns its area and circumference"
   - Toggle "Generate" to create the component code

4. **Save and Compile**
   - The component code will be automatically saved to a file in the "GeneratedComponents" directory
   - Compile the component using the `ComponentCompilerComponent` or add it to your Visual Studio project
   - Rebuild the Brain plugin with the new component

5. **Using the Generated Component**
   - After rebuilding, restart Grasshopper
   - The new component should appear in the "Brain" tab under a category defined in the generated code

## Limitations and Considerations

### Model Capabilities
- Smaller models like deepseek-r1:1.5b may struggle with complex code generation
- Consider using larger models like CodeLlama, Llama-2-70b, or Mistral for better code quality
- Experiment with different temperature settings (lower for more focused code, higher for creativity)

### Dynamic Loading
The current implementation doesn't support truly dynamic loading of components at runtime. Some limitations:
- Generated components need to be compiled and the plugin rebuilt
- Grasshopper needs to be restarted to load new components
- For a fully dynamic approach, more advanced assembly handling would be needed

### Code Quality
- Generated code may require validation and cleanup
- Check for potential issues like:
  - Incomplete error handling
  - Missing namespaces
  - Incorrect GUID generation
  - Logic errors in component implementation

### Performance Considerations
- Local LLM models require significant system resources
- Generation time depends on your hardware and model size
- Consider using smaller models for quick tests and larger ones for final code generation

### Integration Ideas
- Create a library of component templates for the LLM to reference
- Build a component that can generate entire plugin frameworks
- Implement a two-way conversation with the LLM for iterative component design
