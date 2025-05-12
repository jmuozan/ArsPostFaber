using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Grasshopper.Kernel;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Rhino;
using LLM.Templates;
using System.Drawing;
// PDF extraction libraries
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using LLM.Utils;

namespace LLM.OllamaComps
{
    /// <summary>
    /// Improved Ollama-based component generator with PDF context integration and enhanced error handling
    /// </summary>
    public class OllamaComponentGeneratorComponent : GH_Component_HTTPAsync
    {
        // Compilation tracking
        private int _compileAttempts = 0;
        private const int _maxCompileAttempts = 5;
        
        // Last request details
        private string _lastUrl = string.Empty;
        private string _model = string.Empty;
        private double _temperature = 0.7;
        private int _maxTokens = 4096; // Increased token limit for context
        private int _timeoutMs = 60000;
        
        // Prompt parts
        private string _systemPrompt = string.Empty;
        private string _userPrompt = string.Empty;
        private string _contextPrompt = string.Empty;
        
        // Context management
        private PdfContextExtractor _contextExtractor = new PdfContextExtractor();
        private bool _contextLoaded = false;
        private DateTime _lastContextCheck = DateTime.MinValue;
        private const int _contextRefreshMinutes = 10;

        // Constructor
        public OllamaComponentGeneratorComponent()
          : base("Create GH Component", "GHCreate",
              "Generates and compiles a Grasshopper component via Ollama with coding book context",
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
            pManager.AddBooleanParameter("Use Context", "UC", "Use PDF context from coding books", GH_ParamAccess.item, true);
            pManager.AddTextParameter("Context Folder", "CF", "Path to context PDFs folder", GH_ParamAccess.item, "context_for_llm");
            pManager.AddNumberParameter("Temperature", "T", "Generation temperature (0-1)", GH_ParamAccess.item, 0.7);
            pManager.AddIntegerParameter("Max Tokens", "MT", "Maximum number of tokens to generate", GH_ParamAccess.item, 4096);
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
            pManager.AddTextParameter("Context Info", "CI", "Information about loaded context", GH_ParamAccess.item);
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
            string category = "crft";
            string subcategory = "LLM";
            bool useContext = true;
            string contextFolder = "context_for_llm";
            double temperature = 0.7;
            string url = string.Empty;
            int timeout = 60000;
            int maxTokens = 4096;

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
            DA.GetData("Category", ref category);
            DA.GetData("Subcategory", ref subcategory);
            DA.GetData("Use Context", ref useContext);
            DA.GetData("Context Folder", ref contextFolder);
            DA.GetData("Temperature", ref temperature);
            DA.GetData("Max Tokens", ref maxTokens);
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

            // Load context if enabled and not already loaded or outdated
            string contextInfo = "No context used";
            if (useContext)
            {
                // Check if context needs refresh (only refresh every 10 minutes to avoid file system overhead)
                TimeSpan timeSinceLastCheck = DateTime.Now - _lastContextCheck;
                if (!_contextLoaded || timeSinceLastCheck.TotalMinutes > _contextRefreshMinutes)
                {
                    LoadContext(contextFolder);
                    _lastContextCheck = DateTime.Now;
                }
                
                // Extract relevant context for this specific request
                _contextPrompt = ExtractRelevantContext(description, componentName);
                contextInfo = $"Using {_pdfContext.Count} context files with {_contextPrompt.Length} chars of relevant content";
            }
            else
            {
                _contextPrompt = string.Empty;
            }

            // Output context info
            DA.SetData("Context Info", contextInfo);

            // Build system prompt with component generation instructions
            _systemPrompt = "You are an expert C# programmer specializing in Grasshopper plugin development for Rhino. " +
                           "Create a complete, well-structured Grasshopper component based on the description provided. " +
                           "Your generated code must:";
            
            if (!string.IsNullOrEmpty(_contextPrompt))
            {
                _systemPrompt += "\n1. Follow the Grasshopper component architecture patterns shown in the context";
                _systemPrompt += "\n2. Use the specific implementation details from the context when relevant";
                _systemPrompt += "\n3. Match the code style and organization of the provided examples";
            }
            else 
            {
                _systemPrompt += "\n1. Follow standard Grasshopper component patterns";
                _systemPrompt += "\n2. Include all necessary namespaces and error handling";
            }
            
            _systemPrompt += "\n4. Include RegisterInputParams, RegisterOutputParams, and SolveInstance methods";
            _systemPrompt += "\n5. Generate a unique ComponentGuid using a new Guid";
            _systemPrompt += "\n6. Be production-ready with proper error handling";
            _systemPrompt += "\n7. Provide only complete, compilable C# code without explanations or markdown";

            // Build user prompt with all the component details
            _userPrompt = $"Create a Grasshopper component that: {description}";
            
            if (!string.IsNullOrEmpty(componentName))
                _userPrompt += $"\nComponent name: {componentName}";
                
            _userPrompt += $"\nCategory: {category}\nSubcategory: {subcategory}";
            
            // Add context if available
            if (!string.IsNullOrEmpty(_contextPrompt))
            {
                _userPrompt += "\n\nCoding reference context:\n" + _contextPrompt;
            }

            // Store parameters for potential retries
            _model = model;
            _temperature = temperature;
            _maxTokens = maxTokens;
            _timeoutMs = timeout;

            // Build the full prompt for Ollama
            var fullPrompt = _systemPrompt + "\n\n" + _userPrompt;

            // Prepare request body
            var requestPayload = new
            {
                model = model,
                prompt = fullPrompt,
                max_tokens = maxTokens,
                temperature = temperature,
                stream = false
            };
            
            string body = JsonSerializer.Serialize(requestPayload);
            _lastUrl = url;
            
            // Reset compilation attempts for new generation
            _compileAttempts = 0;
            
            // Start generation
            _currentState = RequestState.Requesting;
            this.Message = "Generating Component...";
            POSTAsync(url, body, "application/json", string.Empty, timeout);
        }

dContext(string contextFolder)
        {
            _pdfContext.Clear();
            _contextLoaded = false;
            
            try
            {
                // Determine correct path - try several possibilities
                string[] possiblePaths = new[]
                {
                    contextFolder,
                    Path.Combine(Directory.GetCurrentDirectory(), contextFolder),
                    Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), contextFolder),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), contextFolder),
                };
                
                string validPath = null;
                foreach (var path in possiblePaths)
                {
                    if (Directory.Exists(path))
                    {
                        validPath = path;
                        break;
                    }
                }
                
                if (validPath == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Context folder not found: tried {string.Join(", ", possiblePaths)}");
                    return;
                }
                
                // Look for text files or PDFs
                string[] files = Directory.GetFiles(validPath, "*.*")
                    .Where(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || 
                               f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                
                if (files.Length == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"No text/PDF/CS files found in {validPath}");
                    return;
                }
                
                // Process each file
                foreach (var file in files)
                {
                    string content = string.Empty;
                    string fileName = Path.GetFileName(file);
                    
                    if (file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Extract text from PDF using iText7
                            content = ExtractTextFromPdf(file);
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                                $"Successfully extracted text from {fileName} ({content.Length} chars)");
                        }
                        catch (Exception pdfEx)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                                $"Error extracting text from PDF {fileName}: {pdfEx.Message}");
                            content = $"Failed to extract PDF content: {pdfEx.Message}";
                        }
                    }
                    else
                    {
                        // Read text files directly
                        content = File.ReadAllText(file);
                    }
                    
                    // Store content keyed by filename
                    _pdfContext[fileName] = content;
                }
                
                _contextLoaded = true;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    $"Loaded {_pdfContext.Count} context files from {validPath}");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error loading context: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract relevant context for the current component generation request
        /// </summary>
        private string ExtractRelevantContext(string description, string componentName)
        {
            if (_pdfContext.Count == 0)
                return string.Empty;
                
            StringBuilder relevantContext = new StringBuilder();
            
            // Create a list of keywords from the description and component name
            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Add component name words if provided
            if (!string.IsNullOrEmpty(componentName))
            {
                foreach (var word in SplitCamelCase(componentName))
                    keywords.Add(word);
            }
            
            // Add words from description
            foreach (var word in description.Split(new[] { ' ', ',', '.', ':', ';', '-', '_', '/', '\\', '(', ')', '[', ']' }, 
                                                 StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length > 3) // Only consider words longer than 3 chars
                    keywords.Add(word);
            }
            
            // Look for context containing these keywords
            foreach (var entry in _pdfContext)
            {
                string fileName = entry.Key;
                string content = entry.Value;
                
                // Split content into chunks
                string[] chunks = SplitIntoChunks(content, 1500, 500);
                
                foreach (var chunk in chunks)
                {
                    // Count how many keywords match this chunk
                    int matches = keywords.Count(keyword => 
                        chunk.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                        
                    if (matches >= 2) // If at least 2 keywords match, include this chunk
                    {
                        relevantContext.AppendLine($"--- From {fileName} ---");
                        relevantContext.AppendLine(chunk);
                        relevantContext.AppendLine();
                    }
                }
            }
            
            // Truncate if too long (maintain token budget for the actual generation)
            if (relevantContext.Length > 10000)
            {
                relevantContext.Length = 10000;
                relevantContext.AppendLine("... [truncated for length] ...");
            }
            
            return relevantContext.ToString();
        }

        /// <summary>
        /// Split camelCase or PascalCase words
        /// </summary>
        private string[] SplitCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return Array.Empty<string>();
                
            return Regex.Split(input, @"(?<!^)(?=[A-Z])");
        }

        /// <summary>
        /// Split long text into overlapping chunks of specified size
        /// </summary>
        private string[] SplitIntoChunks(string text, int chunkSize, int overlap)
        {
            if (string.IsNullOrEmpty(text) || chunkSize <= 0)
                return Array.Empty<string>();
                
            List<string> chunks = new List<string>();
            int pos = 0;
            
            while (pos < text.Length)
            {
                int size = Math.Min(chunkSize, text.Length - pos);
                chunks.Add(text.Substring(pos, size));
                pos += size - overlap;
                
                // Ensure we make progress
                if (pos <= 0) pos = chunkSize;
            }
            
            return chunks.ToArray();
        }

        private void ProcessAndCompileCode(IGH_DataAccess DA)
        {
            try
            {
                using var jsonDocument = JsonDocument.Parse(_response);
                var generatedText = jsonDocument.RootElement.GetProperty("response").GetString() ?? string.Empty;
                
                // Clean code - remove Markdown code fences if present
                string cleanCode = generatedText.Trim();
                if (cleanCode.StartsWith("```csharp"))
                {
                    cleanCode = cleanCode.Substring(cleanCode.IndexOf('\n') + 1);
                }
                if (cleanCode.EndsWith("```"))
                {
                    cleanCode = cleanCode.Substring(0, cleanCode.LastIndexOf("```"));
                }
                cleanCode = cleanCode.Trim();
                
                // Extract component class name
                string className = ExtractClassName(cleanCode);
                
                // Save code to file
                string csPath = SaveComponentCode(cleanCode, className);
                string assemblyPath = Path.ChangeExtension(csPath, ".gha");
                
                // Attempt compilation
                bool success = true;
                Exception compileEx = null;
                try
                {
                    CompileCode(cleanCode, assemblyPath);
                }
                catch (Exception ex)
                {
                    compileEx = ex;
                    success = false;
                }
                
                // Handle compile failures with retry via LLM
                if (!success && _compileAttempts < _maxCompileAttempts)
                {
                    _compileAttempts++;
                    
                    // Extract error message
                    string errMsg = compileEx?.Message ?? "Unknown compilation error";
                    
                    // Build a better retry prompt with more specific error details
                    string retrySystem = "You are an expert C# programmer fixing a Grasshopper component that failed to compile. " +
                                       "Carefully analyze the compile errors and provide a corrected version that fixes all issues. " + 
                                       "Respond with ONLY the complete fixed code.";
                    
                    string retryUser = $"Fix this C# Grasshopper component code that failed to compile with these errors:\n\n" +
                                     $"ERROR: {errMsg}\n\n" +
                                     $"Here's the code that needs to be fixed:\n\n{cleanCode}";
                    
                    // Prepare retry request
                    var retryPayload = new
                    {
                        model = _model,
                        prompt = $"{retrySystem}\n\n{retryUser}",
                        max_tokens = _maxTokens,
                        temperature = Math.Max(0.1, _temperature - 0.1), // Slightly lower temperature for fixes
                        stream = false
                    };
                    
                    string retryBody = JsonSerializer.Serialize(retryPayload);
                    
                    // Update UI
                    this.Message = $"Retry {_compileAttempts}/{_maxCompileAttempts}: fixing compile errors...";
                    _currentState = RequestState.Requesting;
                    
                    // Send retry request
                    POSTAsync(_lastUrl, retryBody, "application/json", string.Empty, _timeoutMs);
                    
                    // Prevent setting outputs now, wait for next response
                    _shouldExpire = false;
                    return;
                }
                
                // If reached max attempts but still failing, show detailed error
                if (!success)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Compilation failed after {_compileAttempts} attempts: " + (compileEx?.Message ?? ""));
                    assemblyPath = string.Empty;
                }
                
                // Output results
                DA.SetData(0, cleanCode);
                DA.SetData(1, className);
                DA.SetData(2, csPath);
                DA.SetData(3, assemblyPath);
                DA.SetData(4, success);
                
                // Set context info output
                if (!string.IsNullOrEmpty(_contextPrompt))
                {
                    DA.SetData(5, $"Used {_pdfContext.Count} context files with {_contextPrompt.Length} chars of relevant content");
                }
                else
                {
                    DA.SetData(5, "No context used");
                }
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

        /// <summary>
        /// Returns the component icon.
        /// </summary>
        protected override Bitmap Icon => null;
        


        /// <summary>
        /// Gets the unique ID for this component.
        /// </summary>
        public override Guid ComponentGuid => new Guid("BF4C9D32-A84B-4BA2-BD5F-E75DEF32CC71");
    }
}