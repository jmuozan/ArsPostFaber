# PDF-Enabled Ollama Component Generator - Usage Guide

This guide explains how to use the enhanced Ollama Component Generator that can read PDFs and use them as context for generating Grasshopper components.

## Setup Instructions

### 1. Install Required Libraries

First, ensure that your project has the required libraries by adding iText7 to your project:

```
dotnet add package itext7 --version 7.2.5
```

Or add it to your project file manually as shown in the updated project.

### 2. Create the Utility Class

Add the `PdfContextExtractor.cs` file to a new folder called `LLM/Utils` in your project. This class handles the PDF text extraction and context management.

### 3. Update the Component Generator

Replace your existing `OllamaComponentGeneratorComponent.cs` with the enhanced version that uses the PDF extractor.

### 4. Prepare Your Context Folder

Create a folder called `context_for_llm` in one of these locations:
- The root of your project
- Your user's home directory
- The same directory as your plugin assembly

Place PDFs, code samples (.cs), and text files (.txt) that contain reference materials, coding examples, and documentation in this folder.

Good materials to include:
- Grasshopper SDK documentation
- Code samples of components similar to what you want to generate
- Documentation for libraries you commonly use
- Best practices for Grasshopper component development

### 5. Build and Run

Build your project to ensure everything compiles correctly.

## Using the Component

1. **Add the Component to Your Canvas**
   - The "Create GH Component" component will automatically add an Ollama model selector
   - Connect these two components

2. **Configure the Component**
   - Enter a detailed description of the component you want to create
   - Optionally specify a component name
   - Choose a category and subcategory
   - Make sure "Use Context" is enabled
   - Verify the Context Folder path is correct

3. **Generate the Component**
   - Make sure Ollama is running on your machine
   - Toggle the "Generate" input to True
   - The component will:
     - Load context from your PDFs and text files
     - Find relevant information based on your description
     - Generate code using the Ollama model
     - Compile the component
     - Handle any compilation errors automatically

4. **Review Output**
   - Check the generated code in the "Generated Code" output
   - The "Source Path" shows where the source file was saved
   - The "Assembly Path" shows where the compiled .gha is located
   - "Success" indicates if compilation was successful
   - "Context Info" shows details about the context that was used

## Example

Here's an example prompt to try:

**Description:**
"Create a component that loads an image and allows adjusting brightness, contrast, and saturation. It should output the modified image and preview it on the canvas."

## Troubleshooting

### PDF Extraction Issues

If you encounter issues with PDF extraction:

1. **Check File Permissions**
   - Ensure your application has read permissions for the PDF files

2. **PDF Format Issues**
   - Some PDFs might be scanned or have security settings that prevent text extraction
   - Try converting problematic PDFs to text files manually

3. **Extract Text Manually**
   - For troublesome PDFs, you can manually extract the text and save it as a .txt file with the same name

### Component Generation Issues

If your generated components have compilation errors that aren't being fixed automatically:

1. **Increase Max Compile Attempts**
   - You can modify the `_maxCompileAttempts` constant in the code to allow more retry attempts

2. **Check Console Output**
   - Look for detailed error messages in the Rhino console

3. **Add More Examples**
   - Add more example code to your context folder that's similar to what you're trying to generate

## Advanced Usage

### Creating a Component Cookbook

For best results, create a "cookbook" of component examples in text or code files:

```csharp
// Example: Image Processing Component
using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace MyPlugins
{
    public class ImageBrightnessComponent : GH_Component
    {
        public ImageBrightnessComponent()
          : base("Image Brightness", "Brightness",
              "Adjusts the brightness of an image",
              "Display", "Image")
        {
        }
        
        // Implementation details...
    }
}
```

### Using Multiple Models

You can experiment with different Ollama models for different types of components:
- Smaller models (like `wizard-coder:1b`) for simple components
- Medium models for most components (like `deepseek-coder:6.7b`)
- Larger models for complex components (like `llama3:70b`)

Simply select the appropriate model from the dropdown before generating.
