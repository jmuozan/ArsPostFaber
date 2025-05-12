# Quick Start Guide: Setting Up the AI Component Generator on macOS

This guide will help you quickly set up the PDF-enabled Ollama component generator on your Mac.

## 1. Install Prerequisites

First, install Homebrew if you don't have it already:

```bash
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
```

Then install the required packages:

```bash
# Install Ollama
brew install ollama

# Install .NET SDK if needed for building the plugin
brew install --cask dotnet-sdk

# Install Python for the fine-tuning pipeline
brew install python
```

## 2. Set Up the Project

Clone or download your project repository:

```bash
git clone https://github.com/yourusername/crft.git
cd crft
```

Add the required library for PDF extraction:

```bash
dotnet add package itext7 --version 7.2.5
```

## 3. Create Context Folder

Create a folder for your PDF documentation and code references:

```bash
mkdir -p context_for_llm
```

Add documentation to this folder:
- PDF programming books
- Grasshopper SDK documentation
- Example C# files
- Text files with code snippets

## 4. Download and Run Ollama Model

Download and run a good base model for coding:

```bash
# Download the model
ollama pull deepseek-coder:6.7b-instruct

# Start the Ollama server (if not already running)
ollama serve
```

## 5. Build and Run the Plugin

Build the Grasshopper plugin:

```bash
dotnet build
```

The component will be available in Grasshopper under the "crft" > "LLM" category.

## 6. Using the Component Generator

1. Add the "Create GH Component" component to your canvas
2. It will automatically add an "Ollama Models" dropdown - connect them
3. Fill in:
   - Description of your component
   - Optional component name
   - Category and subcategory
   - Make sure "Use Context" is enabled
4. Toggle "Generate" to True

## 7. Setting Up Fine-tuning (Optional)

If you want to create a specialized model:

1. Install fine-tuning requirements:
   ```bash
   # Create a virtual environment
   python -m venv ft_env
   source ft_env/bin/activate
   
   # Install dependencies
   pip install axolotl transformers datasets accelerate bitsandbytes
   pip install sentencepiece einops wandb scipy
   pip install itext7-pdfhtml pdfminer.six python-docx
   ```

2. Clone the fine-tuning repository:
   ```bash
   git clone https://github.com/yourusername/grasshopper_finetuning.git
   cd grasshopper_finetuning
   ```

3. Set up directory structure:
   ```bash
   mkdir -p {scripts,data,config,output}
   ```

4. Add the necessary scripts from the guide

5. Run the data collection script:
   ```bash
   python scripts/collect_training_data.py --context_dir ../context_for_llm
   ```

6. Run the fine-tuning process:
   ```bash
   python scripts/run_finetuning.py
   ```

## 8. Troubleshooting

### PDF Extraction Issues

If PDFs aren't extracting properly:

1. Verify iText7 is correctly installed:
   ```bash
   dotnet list package | grep itext
   ```

2. Check file permissions:
   ```bash
   chmod -R 644 context_for_llm/*.pdf
   ```

3. Try converting PDFs to text:
   ```bash
   brew install pdftotext
   pdftotext your_file.pdf your_file.txt
   ```

### Ollama Connection Issues

If the component can't connect to Ollama:

1. Verify Ollama is running:
   ```bash
   ollama list
   ```

2. Check the default URL:
   ```bash
   curl http://localhost:11434/api/version
   ```

3. Restart Ollama:
   ```bash
   killall ollama
   ollama serve
   ```

### Model Download Issues

If you're having trouble downloading models:

1. Check disk space:
   ```bash
   df -h
   ```

2. Try with a smaller model first:
   ```bash
   ollama pull deepseek-coder:1.3b
   ```

3. Check Ollama logs:
   ```bash
   cat ~/.ollama/logs/ollama.log
   ```

## 9. Next Steps

- Create a library of component descriptions for batch generation
- Set up the continuous improvement pipeline
- Share your fine-tuned model with teammates
- Consider contributing successful generations back to the project

## 10. Resources

- [Ollama Documentation](https://github.com/ollama/ollama/blob/main/README.md)
- [iText7 Documentation](https://github.com/itext/itext7-dotnet)
- [Axolotl Documentation](https://github.com/OpenAccess-AI-Collective/axolotl)
- [Grasshopper SDK Documentation](https://developer.rhino3d.com/api/grasshopper/html/R_Project_Grasshopper.htm)
