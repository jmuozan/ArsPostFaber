# Agent-Based LLM Component Generator with PDF Context Integration

This document explains how to enhance the OllamaComponentGeneratorComponent to act as an agent that reads local PDF files for context and iteratively improves component generation until successful compilation.

## Overview

The component can be transformed from a simple LLM-based code generator into a persistent agent that:

1. Loads PDF documents from a designated folder for context
2. Generates Grasshopper component code based on descriptions
3. Automatically attempts compilation
4. If compilation fails, it analyzes errors and tries again with improved prompts
5. Continues until either successful compilation or maximum retries reached

## Implementation Details

### 1. PDF Context Integration

First, we need to add PDF loading and processing capabilities:

```csharp
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

public class PdfContextManager
{
    private static readonly string _contextFolderPath = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), 
        "context_for_llm");
        
    public static string LoadPdfContext(string[] specificFiles = null)
    {
        StringBuilder contextBuilder = new StringBuilder();
        
        if (!Directory.Exists(_contextFolderPath))
        {
            Directory.CreateDirectory(_contextFolderPath);
            return "No context files found. Created context_for_llm folder.";
        }
        
        string[] pdfFiles = specificFiles ?? 
            Directory.GetFiles(_contextFolderPath, "*.pdf", SearchOption.TopDirectoryOnly);
            
        foreach (string pdfPath in pdfFiles)
        {
            try
            {
                using (PdfDocument document = PdfDocument.Open(pdfPath))
                {
                    contextBuilder.AppendLine($"=== CONTEXT FROM: {Path.GetFileName(pdfPath)} ===");
                    
                    for (int i = 1; i <= document.NumberOfPages; i++)
                    {
                        var page = document.GetPage(i);
                        string text = ContentOrderTextExtractor.GetText(page);
                        contextBuilder.AppendLine(text);
                    }
                    
                    contextBuilder.AppendLine("=== END CONTEXT ===\n");
                }
            }
            catch (Exception ex)
            {
                contextBuilder.AppendLine($"Error loading PDF {Path.GetFileName(pdfPath)}: {ex.Message}");
            }
        }
        
        return contextBuilder.ToString();
    }
}
```

### 2. Enhancing OllamaComponentGeneratorComponent with Agent Capabilities

Now, we need to modify the OllamaComponentGeneratorComponent to incorporate agent-like behavior:

```csharp
// New class fields
private bool _agentMode = true;  // Always enable agent mode
private int _maxRetryAttempts = 5;
private string _contextCache = null;
private bool _isAgentRunning = false;
private string _currentDescription = string.Empty;
private string _currentComponentName = string.Empty;
private string _currentCategory = string.Empty;
private string _currentSubcategory = string.Empty;
private CompilationErrorTracker _errorTracker = new CompilationErrorTracker();

// This internal class will track compilation errors to improve prompts
private class CompilationErrorTracker 
{
    public List<string> PreviousErrors { get; } = new List<string>();
    public HashSet<string> CommonErrors { get; } = new HashSet<string>();
    
    public void AddError(string error)
    {
        PreviousErrors.Add(error);
        // Extract common patterns
        if (error.Contains("namespace") && error.Contains("not exist"))
        {
            CommonErrors.Add("namespace_not_found");
        }
        if (error.Contains("type or namespace") && error.Contains("could not be found"))
        {
            CommonErrors.Add("missing_reference");
        }
        // Add more patterns as needed
    }
    
    public string GenerateErrorPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Previous compilation errors:");
        
        // Add the last 3 errors to keep context manageable
        foreach (var error in PreviousErrors.Skip(Math.Max(0, PreviousErrors.Count - 3)))
        {
            sb.AppendLine($"- {error}");
        }
        
        if (CommonErrors.Contains("namespace_not_found"))
        {
            sb.AppendLine("Ensure all namespaces are properly defined and accessible.");
        }
        if (CommonErrors.Contains("missing_reference"))
        {
            sb.AppendLine("Make sure to use only types from referenced assemblies: System, System.Core, Grasshopper, Rhino, GH_IO.");
        }
        
        return sb.ToString();
    }
}
```

### 3. Modifying SolveInstance Method

Next, we need to modify the SolveInstance method to implement the agent behavior:

```csharp
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
                
                // In agent mode, process the response but don't immediately set outputs
                if (_agentMode && _isAgentRunning)
                {
                    AgentProcessAndCompileCode(DA);
                }
                else
                {
                    // Regular processing for non-agent mode
                    ProcessAndCompileCode(DA);
                }
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
    double temperature = 0.7;
    string url = string.Empty;
    int timeout = 60000;

    if (!DA.GetData("Generate", ref active)) return;
    if (!active)
    {
        _currentState = RequestState.Off;
        _shouldExpire = true;
        _response = string.Empty;
        _isAgentRunning = false;
        ExpireSolution(true);
        return;
    }
    
    if (!DA.GetData("Model", ref model)) return;
    if (!DA.GetData("Description", ref description)) return;
    DA.GetData("Component Name", ref componentName);
    DA.GetData("Category", ref category);
    DA.GetData("Subcategory", ref subcategory);
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

    // Store current params for agent retries
    _currentDescription = description;
    _currentComponentName = componentName;
    _currentCategory = category;
    _currentSubcategory = subcategory;
    _compileAttempts = 0;
    _errorTracker = new CompilationErrorTracker();

    // Load PDF context if not already loaded
    if (_contextCache == null)
    {
        this.Message = "Loading context...";
        _contextCache = PdfContextManager.LoadPdfContext();
        if (string.IsNullOrEmpty(_contextCache))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                "No context files found in 'context_for_llm' folder. Agent will function with limited context.");
        }
        else
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                $"Loaded context data from PDFs ({_contextCache.Length / 1024} KB).");
        }
    }

    // Build the system prompt with context info and coding guidelines
    string systemPrompt = BuildSystemPrompt(description, componentName, category, subcategory);
    
    // Agent mode started
    _isAgentRunning = _agentMode;
    
    // Prepare request for Ollama API
    var requestPayload = new
    {
        model = model,
        prompt = systemPrompt,
        max_tokens = maxTokens,
        temperature = temperature,
        stream = false
    };
    
    string body = JsonSerializer.Serialize(requestPayload);
    _currentState = RequestState.Requesting;
    this.Message = "Generating Component...";
    
    // Store request details for potential retries
    _lastUrl = url;
    _lastBody = body;
    _model = model;
    _temperature = temperature;
    _maxTokens = maxTokens;
    _timeoutMs = timeout;
    _systemPrompt = systemPrompt;
    
    // Send initial generation request
    POSTAsync(url, body, "application/json", string.Empty, timeout);
}
```

### 4. Adding Agent Processing Method

We need to add a method to handle the agent's processing and compilation retry logic:

```csharp
private void AgentProcessAndCompileCode(IGH_DataAccess DA)
{
    try
    {
        using var jsonDocument = JsonDocument.Parse(_response);
        var generatedText = jsonDocument.RootElement.GetProperty("response").GetString() ?? string.Empty;
        string cleanCode = CleanGeneratedCode(generatedText);
        
        string className = ExtractClassName(cleanCode);
        string csPath = SaveComponentCode(cleanCode, className);
        string assemblyPath = Path.ChangeExtension(csPath, ".gha");
        
        // Attempt compilation
        bool success = true;
        Exception compileEx = null;
        
        try
        {
            CompileCode(cleanCode, assemblyPath);
            this.Message = $"Compilation succeeded after {_compileAttempts + 1} attempts";
        }
        catch (Exception ex)
        {
            compileEx = ex;
            success = false;
            
            // Add the error to our tracker
            _errorTracker.AddError(ex.Message);
        }
        
        // Handle agent retry logic
        if (!success && _compileAttempts < _maxRetryAttempts)
        {
            _compileAttempts++;
            this.Message = $"Retrying ({_compileAttempts}/{_maxRetryAttempts})...";
            
            // Create an improved prompt with error feedback
            string retryPrompt = BuildRetryPrompt(cleanCode, compileEx?.Message ?? "Unknown error");
            
            // Send the retry request
            var retryPayload = new
            {
                model = _model,
                prompt = retryPrompt,
                max_tokens = _maxTokens,
                temperature = _temperature,
                stream = false
            };
            
            string retryBody = JsonSerializer.Serialize(retryPayload);
            _currentState = RequestState.Requesting;
            
            // Send retry request
            POSTAsync(_lastUrl, retryBody, "application/json", string.Empty, _timeoutMs);
            
            // Don't set outputs yet - wait for the retry
            return;
        }
        
        // All retries exhausted or successful compilation
        _isAgentRunning = false;
        
        // Set outputs
        DA.SetData(0, cleanCode);
        DA.SetData(1, className);
        DA.SetData(2, csPath);
        DA.SetData(3, success ? assemblyPath : string.Empty);
        DA.SetData(4, success);
        
        if (success)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                $"Successfully generated and compiled {className} after {_compileAttempts + 1} attempts.");
        }
        else
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                "Failed to compile component after maximum retry attempts.");
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, compileEx?.Message ?? "Unknown error");
        }
    }
    catch (Exception ex)
    {
        _isAgentRunning = false;
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error processing response: " + ex.Message);
        DA.SetData(0, _response);
        DA.SetData(4, false);
    }
}
```

### 5. Helper Methods for Code Processing

We need to add several helper methods for cleaning code and building prompts:

```csharp
private string CleanGeneratedCode(string generatedText)
{
    string cleanCode = generatedText.Trim();
    
    // Remove markdown code blocks
    if (cleanCode.StartsWith("```"))
    {
        int firstNewLine = cleanCode.IndexOf('\n');
        if (firstNewLine > 0)
        {
            cleanCode = cleanCode.Substring(firstNewLine + 1);
        }
    }
    
    if (cleanCode.EndsWith("```"))
    {
        int lastBackticks = cleanCode.LastIndexOf("```");
        if (lastBackticks > 0)
        {
            cleanCode = cleanCode.Substring(0, lastBackticks);
        }
    }
    
    // Remove any leading/trailing tags or metadata
    var lines = cleanCode.Split(new[] {'\r','\n'}, StringSplitOptions.RemoveEmptyEntries).ToList();
    
    // Strip leading XML tags
    while (lines.Count > 0 && (lines[0].TrimStart().StartsWith("<") && lines[0].Contains(">")))
    {
        lines.RemoveAt(0);
    }
    
    // Strip trailing XML tags
    while (lines.Count > 0 && (lines[^1].TrimEnd().EndsWith(">") && lines[^1].Contains("<")))
    {
        lines.RemoveAt(lines.Count - 1);
    }
    
    return string.Join("\n", lines).Trim();
}

private string BuildSystemPrompt(string description, string componentName, string category, string subcategory)
{
    var sb = new StringBuilder();
    
    // Core system context
    sb.AppendLine("You are an expert C# programmer specializing in Grasshopper plugin development.");
    sb.AppendLine("Create a complete, well-structured Grasshopper component based on the description provided.");
    
    // Add PDF context if available
    if (!string.IsNullOrEmpty(_contextCache))
    {
        sb.AppendLine("\n--- REFERENCE CONTEXT ---");
        sb.AppendLine(_contextCache);
        sb.AppendLine("--- END REFERENCE CONTEXT ---\n");
    }
    
    // Coding guidelines
    sb.AppendLine("CODING REQUIREMENTS:");
    sb.AppendLine("1. Use standard Grasshopper component patterns with RegisterInputParams, RegisterOutputParams, and SolveInstance methods");
    sb.AppendLine("2. Include all necessary namespaces, especially: Rhino.Geometry, Grasshopper.Kernel");
    sb.AppendLine($"3. Use \"{category}\" as Category and \"{subcategory}\" as Subcategory");
    sb.AppendLine("4. Generate a unique GUID for the component using: new Guid(\"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx\")");
    sb.AppendLine("5. Error-handle appropriately. Use try/catch blocks for risky operations");
    sb.AppendLine("6. ONLY output complete, compilable C# code - no explanations or markdown");
    sb.AppendLine("7. NEVER use \"throw new NotImplementedException()\", always implement all methods");
    sb.AppendLine("8. Only reference Grasshopper, Rhino, System, System.Core, and GH_IO assemblies");
    sb.AppendLine("9. Use simple namespaces without deeply nested structures");
    
    // Component details
    sb.AppendLine($"\nComponent Description: {description}");
    if (!string.IsNullOrEmpty(componentName))
    {
        sb.AppendLine($"Component Class Name: {componentName}");
    }
    
    return sb.ToString();
}

private string BuildRetryPrompt(string previousCode, string error)
{
    var sb = new StringBuilder();
    
    // Start with the base prompt
    sb.AppendLine("You are an expert C# programmer specializing in Grasshopper plugin development.");
    sb.AppendLine("Fix the compilation errors in the previously generated component.");
    
    // Add the error information
    sb.AppendLine("\n--- COMPILATION ERRORS ---");
    sb.AppendLine(error);
    sb.AppendLine(_errorTracker.GenerateErrorPrompt());
    sb.AppendLine("--- END COMPILATION ERRORS ---\n");
    
    // Add the previous code
    sb.AppendLine("--- PREVIOUS CODE ---");
    sb.AppendLine(previousCode);
    sb.AppendLine("--- END PREVIOUS CODE ---\n");
    
    // Add specific fixing instructions
    sb.AppendLine("INSTRUCTIONS:");
    sb.AppendLine("1. Carefully analyze the compilation errors");
    sb.AppendLine("2. Fix ALL errors in the code");
    sb.AppendLine("3. Return ONLY the complete, fixed code without any explanations or comments about the changes");
    sb.AppendLine("4. Ensure all namespaces are correct and accessible");
    sb.AppendLine("5. Use only types from Rhino, Grasshopper, System, System.Core, and GH_IO assemblies");
    sb.AppendLine("6. Do not use any third-party libraries");
    
    return sb.ToString();
}
```

### 6. Package Management for PDF Support

You'll need to add the PdfPig NuGet package to your project for PDF handling. Add this to your csproj file:

```xml
<ItemGroup>
  <PackageReference Include="PdfPig" Version="0.1.9" />
</ItemGroup>
```

## How to Set Up and Use

### Setting Up PDF Context

1. Create a folder named `context_for_llm` in the same directory as your compiled plugin
2. Place any relevant PDF documents in this folder:
   - Grasshopper development guides
   - Rhino SDK documentation
   - Domain-specific information for your components
   - Code examples or tutorials

The component will automatically find and load these documents when it runs.

### Using the Agent Component

1. Install the plugin as usual
2. Create the `context_for_llm` folder and add relevant PDFs
3. In Grasshopper, add the OllamaComponentGeneratorComponent to your canvas
4. Configure your component requirements:
   - Description: Natural language description of what the component should do
   - Component Name (optional): Specific class name for the component
   - Category & Subcategory: Where it should appear in the Grasshopper ribbon

5. Click "Generate" to start the agent process
   - The component will show status messages during generation and compilation
   - You'll see retry attempts if compilation fails
   - When successful, outputs will include the source code and path to compiled assembly

## How the Agent Works

The LLM component behaves as an agent through several mechanisms:

1. **Persistent State**: Maintains information about compilation attempts, errors, and progress
2. **Error Analysis**: Tracks and categorizes compilation errors to improve subsequent attempts
3. **Adaptive Prompting**: Modifies prompts based on previous errors and compilation failures
4. **Iterative Improvement**: Makes multiple attempts, each informed by previous failures

The agent process follows this workflow:

1. When you trigger component generation:
   - The agent loads context from PDFs
   - Sends an initial prompt to the LLM
   - Attempts to compile the generated code

2. If compilation fails:
   - The agent analyzes the specific errors
   - Creates an improved prompt that includes:
     - The original description
     - The previous code
     - The specific compilation errors
     - Guidelines to fix those errors
   - Sends this to the LLM for a revised solution

3. This cycle continues up to 5 times until:
   - Successful compilation occurs, or
   - Maximum retries are reached

## Troubleshooting

- **PDF Loading Issues**: Check that PDFs are not encrypted or password-protected
- **Compilation Always Fails**: Try setting a lower temperature value (0.3-0.5) for more consistent output
- **Agent Gets Stuck**: Ensure your Ollama server is responsive and the model has sufficient context length
- **Memory Issues**: If you have many or large PDFs, consider implementing chunking or filtering mechanisms

## Customization

You can modify the agent's behavior by adjusting:

- `_maxRetryAttempts`: Maximum number of compilation retry attempts
- Content of `BuildSystemPrompt()` and `BuildRetryPrompt()`: Change how the LLM is instructed
- Error patterns in `CompilationErrorTracker`: Add recognizable error types

## Implementation Notes

1. **NuGet Dependencies**: You'll need to add the PdfPig package for PDF parsing
2. **Project Structure**: Create the `context_for_llm` folder alongside your compiled assembly
3. **Error Tracking**: The CompilationErrorTracker can be expanded to recognize more error patterns
4. **PDF Context Optimization**: For large PDFs, you might want to add chunking and summarization

The implementation transforms the component into a true agent by giving it:
1. **Memory**: Tracking previous errors and attempts
2. **Context**: Loading and using PDF documents as reference material
3. **Adaptation**: Improving prompts based on previous failures
4. **Persistence**: Maintaining state across multiple attempts

This approach significantly improves the quality and reliability of generated components by leveraging the power of iterative refinement through agent-like behavior.
