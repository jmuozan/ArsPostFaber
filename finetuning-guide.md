# Model Fine-tuning Pipeline for Grasshopper Component Generation

This guide walks through setting up a complete fine-tuning pipeline for creating specialized models that can generate high-quality Grasshopper components.

## Overview

Fine-tuning a model on your specific codebase and documentation provides several advantages:

1. **Better code quality** - The model learns your coding style and patterns
2. **Domain-specific knowledge** - It understands Grasshopper-specific concepts and APIs
3. **Faster generation** - Less context is needed for each generation request
4. **More reliable components** - Fewer compilation errors and bugs

## Prerequisites

- macOS (Apple Silicon preferred for performance)
- 16GB+ RAM (32GB recommended)
- 100GB+ free disk space
- Ollama already installed
- Python 3.10+

## Setup

### 1. Install Required Tools

```bash
# Create a virtual environment
python -m venv ft_env
source ft_env/bin/activate

# Install dependencies
pip install -U pip
pip install axolotl transformers datasets accelerate bitsandbytes
pip install sentencepiece einops wandb scipy
pip install itext7-pdfhtml pdfminer.six python-docx
```

### 2. Create Project Structure

```bash
mkdir -p grasshopper_finetuning/{scripts,data,models,output}
cd grasshopper_finetuning
```

### 3. Prepare Data Collection Script

Create a script to extract and format your context data for training:

```python
# scripts/collect_training_data.py
import os
import json
import glob
import re
import random
from pathlib import Path

# PDF processing
from pdfminer.high_level import extract_text as pdf_extract_text
# C# file processing
import re

# Docx file processing
try:
    import docx
    DOCX_AVAILABLE = True
except ImportError:
    DOCX_AVAILABLE = False

class DataCollector:
    def __init__(self, context_dir, output_dir):
        self.context_dir = context_dir
        self.output_dir = output_dir
        self.examples = []
        
    def collect_all(self):
        """Collect all training examples from context directory"""
        # Process PDF files
        for pdf_file in glob.glob(os.path.join(self.context_dir, "**/*.pdf"), recursive=True):
            self.process_pdf(pdf_file)
            
        # Process C# files
        for cs_file in glob.glob(os.path.join(self.context_dir, "**/*.cs"), recursive=True):
            self.process_cs_file(cs_file)
            
        # Process text files
        for txt_file in glob.glob(os.path.join(self.context_dir, "**/*.txt"), recursive=True):
            self.process_text_file(txt_file)
            
        # Process Markdown files
        for md_file in glob.glob(os.path.join(self.context_dir, "**/*.md"), recursive=True):
            self.process_markdown(md_file)
            
        # Process Word files if docx is available
        if DOCX_AVAILABLE:
            for docx_file in glob.glob(os.path.join(self.context_dir, "**/*.docx"), recursive=True):
                self.process_docx(docx_file)
                
        print(f"Collected {len(self.examples)} training examples")
        
        # Save to jsonl file
        self.save_examples()
        
        # Generate train/test split
        self.create_train_test_split()
        
    def process_pdf(self, pdf_path):
        """Extract text from PDF and create training examples"""
        try:
            print(f"Processing PDF: {pdf_path}")
            text = pdf_extract_text(pdf_path)
            
            # Clean up text
            text = re.sub(r'\s+', ' ', text).strip()
            
            # Extract C# code blocks if any
            code_blocks = re.findall(r'```csharp(.*?)```', text, re.DOTALL)
            code_blocks += re.findall(r'public class \w+\s*:(.*?)}\s*}', text, re.DOTALL)
            
            # Create examples from code blocks
            for code in code_blocks:
                if len(code.strip()) < 100:  # Skip very small snippets
                    continue
                    
                # Try to identify what the code does
                class_match = re.search(r'public class (\w+)', code)
                if class_match:
                    class_name = class_match.group(1)
                    # Create a description based on class name
                    description = f"Create a Grasshopper component named {class_name}"
                    
                    # Look for constructor summary or component purpose
                    summary_match = re.search(r'/// <summary>(.*?)</summary>', code, re.DOTALL)
                    if summary_match:
                        summary = summary_match.group(1).strip()
                        description += f" that {summary}"
                    
                    # Create training example
                    self.examples.append({
                        "instruction": description,
                        "input": "",
                        "output": code.strip()
                    })
            
            # Create document-based examples
            sections = self.split_into_sections(text)
            for section in sections:
                # Skip very small sections
                if len(section) < 300:
                    continue
                    
                # Create examples for documentation sections
                if "component" in section.lower() and "grasshopper" in section.lower():
                    # Try to extract what this section is about
                    title_match = re.search(r'^([A-Z][^.!?]{10,100})[.!?]', section)
                    if title_match:
                        title = title_match.group(1).strip()
                        # Create a description prompt
                        description = f"Based on the following documentation: '{title}', create a Grasshopper component that implements this functionality."
                        
                        # Create a meta-example (the model should learn to create code from docs)
                        self.examples.append({
                            "instruction": description,
                            "input": section[:1000],  # Limit to first 1000 chars
                            "output": "// The model should generate appropriate component code here based on the documentation"
                        })
                
        except Exception as e:
            print(f"Error processing PDF {pdf_path}: {e}")
                
    def process_cs_file(self, cs_path):
        """Process C# source files to extract component patterns"""
        try:
            print(f"Processing C# file: {cs_path}")
            with open(cs_path, 'r', encoding='utf-8') as f:
                code = f.read()
            
            # Look for GH_Component classes
            component_classes = re.findall(r'public class (\w+)\s*:\s*GH_Component\s*{(.*?)}', code, re.DOTALL)
            
            for class_name, class_body in component_classes:
                # Extract constructor
                constructor = re.search(fr'public {class_name}\s*\([^)]*\)\s*:(.*?}})', class_body, re.DOTALL)
                
                if constructor:
                    # Extract component description
                    desc_match = re.search(r'base\s*\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\)', constructor.group(1))
                    
                    if desc_match:
                        name, nickname, desc, category, subcategory = desc_match.groups()
                        
                        # Create instruction based on component info
                        instruction = f"Create a Grasshopper component named {name} that {desc}. It should be in category {category}, subcategory {subcategory}."
                        
                        # Get the full component class with proper brace matching
                        full_class = self.extract_full_class(code, class_name)
                        
                        if full_class:
                            self.examples.append({
                                "instruction": instruction,
                                "input": "",
                                "output": full_class
                            })
                            
                            # Also create a variant with some implementation details as input
                            methods = re.findall(r'(protected override void \w+\([^)]*\)\s*{[^}]*})', full_class, re.DOTALL)
                            if methods:
                                # Choose a random method to use as partial implementation
                                method = random.choice(methods)
                                # Create a prompt asking to complete the component
                                self.examples.append({
                                    "instruction": f"Complete this Grasshopper component that {desc}. Fill in all necessary methods and ensure it compiles.",
                                    "input": f"public class {class_name} : GH_Component\n{{\n    // TODO: Implement this component\n    \n    {method}\n}}\n",
                                    "output": full_class
                                })
                
        except Exception as e:
            print(f"Error processing C# file {cs_path}: {e}")
    
    def extract_full_class(self, code, class_name):
        """Extract a full class definition with proper brace matching"""
        class_start = code.find(f"public class {class_name}")
        if class_start == -1:
            return None
            
        # Find the opening brace after class declaration
        brace_start = code.find("{", class_start)
        if brace_start == -1:
            return None
            
        # Match braces to find the end of the class
        brace_count = 1
        pos = brace_start + 1
        
        while brace_count > 0 and pos < len(code):
            if code[pos] == '{':
                brace_count += 1
            elif code[pos] == '}':
                brace_count -= 1
            pos += 1
            
        if brace_count == 0:
            return code[class_start:pos].strip()
        
        return None
                    
    def process_text_file(self, txt_path):
        """Process text files for additional context"""
        try:
            print(f"Processing text file: {txt_path}")
            with open(txt_path, 'r', encoding='utf-8') as f:
                text = f.read()
                
            # Look for code blocks
            code_blocks = re.findall(r'```csharp(.*?)```', text, re.DOTALL)
            for code in code_blocks:
                if "GH_Component" in code and len(code.strip()) > 200:
                    # Try to extract class info
                    class_match = re.search(r'public class (\w+)', code)
                    if class_match:
                        class_name = class_match.group(1)
                        self.examples.append({
                            "instruction": f"Create a Grasshopper component called {class_name}",
                            "input": "",
                            "output": code.strip()
                        })
        except Exception as e:
            print(f"Error processing text file {txt_path}: {e}")
                
    def process_markdown(self, md_path):
        """Process Markdown files for documentation and code examples"""
        try:
            print(f"Processing Markdown file: {md_path}")
            with open(md_path, 'r', encoding='utf-8') as f:
                text = f.read()
                
            # Process similar to text files but with markdown structure awareness
            code_blocks = re.findall(r'```csharp(.*?)```', text, re.DOTALL)
            
            # Find headings and their content
            sections = re.split(r'(#+\s+.*?)\n', text)
            current_heading = ""
            current_content = ""
            
            for i, section in enumerate(sections):
                if i % 2 == 0:  # Even indices are content
                    current_content = section
                    
                    # Check if this content contains code examples
                    code_examples = re.findall(r'```csharp(.*?)```', current_content, re.DOTALL)
                    for code in code_examples:
                        if "GH_Component" in code and len(code.strip()) > 200:
                            # Create an example with the heading as context
                            self.examples.append({
                                "instruction": f"Based on this documentation section '{current_heading}', create the following Grasshopper component.",
                                "input": current_content[:500],  # First 500 chars of the section
                                "output": code.strip()
                            })
                else:  # Odd indices are headings
                    current_heading = section.strip('# ')
        except Exception as e:
            print(f"Error processing Markdown file {md_path}: {e}")
                
    def process_docx(self, docx_path):
        """Process Word documents if docx library is available"""
        try:
            print(f"Processing Word document: {docx_path}")
            doc = docx.Document(docx_path)
            text = "\n".join([para.text for para in doc.paragraphs])
            
            # Process similar to text files
            sections = self.split_into_sections(text)
            for section in sections:
                # Skip very small sections
                if len(section) < 300:
                    continue
                    
                # Create examples for relevant sections
                if "component" in section.lower() and "grasshopper" in section.lower():
                    # Try to extract what this section is about
                    title_match = re.search(r'^([A-Z][^.!?]{10,100})[.!?]', section)
                    if title_match:
                        title = title_match.group(1).strip()
                        # Create a description prompt
                        description = f"Based on the following documentation: '{title}', create a Grasshopper component that implements this functionality."
                        
                        self.examples.append({
                            "instruction": description,
                            "input": section[:1000],  # Limit to first 1000 chars
                            "output": "// The model should generate appropriate component code here based on the documentation"
                        })
        except Exception as e:
            print(f"Error processing Word document {docx_path}: {e}")
                
    def split_into_sections(self, text, min_length=300, max_length=2000):
        """Split text into logical sections based on paragraph breaks and headings"""
        # First try to split by headings (all caps followed by newline or all caps with numbers)
        sections = re.split(r'\n\s*[A-Z][A-Z\s]+[A-Z](?:\s+\d+)?\s*\n', text)
        
        # If no good sections, try splitting by double newlines
        if len(sections) <= 1 or all(len(s.strip()) < min_length for s in sections):
            sections = re.split(r'\n\s*\n', text)
            
        # Ensure sections are within size limits
        result = []
        current = ""
        
        for section in sections:
            section = section.strip()
            if not section:
                continue
                
            if len(current) + len(section) <= max_length:
                current += "\n" + section if current else section
            else:
                if current and len(current) >= min_length:
                    result.append(current)
                current = section
                
        if current and len(current) >= min_length:
            result.append(current)
            
        return result
                
    def save_examples(self):
        """Save collected examples to JSONL file"""
        os.makedirs(self.output_dir, exist_ok=True)
        output_file = os.path.join(self.output_dir, "examples.jsonl")
        
        # Deduplicate examples based on output (code)
        unique_outputs = set()
        unique_examples = []
        
        for example in self.examples:
            output_hash = hash(example["output"])
            if output_hash not in unique_outputs:
                unique_outputs.add(output_hash)
                unique_examples.append(example)
                
        print(f"Saving {len(unique_examples)} unique examples (from {len(self.examples)} total)")
        
        with open(output_file, 'w', encoding='utf-8') as f:
            for example in unique_examples:
                f.write(json.dumps(example) + '\n')
                
        print(f"Saved examples to {output_file}")
        
    def create_train_test_split(self, test_size=0.1):
        """Split examples into training and test sets"""
        os.makedirs(self.output_dir, exist_ok=True)
        examples_file = os.path.join(self.output_dir, "examples.jsonl")
        
        if not os.path.exists(examples_file):
            print(f"Error: {examples_file} not found")
            return
            
        # Load examples
        examples = []
        with open(examples_file, 'r', encoding='utf-8') as f:
            for line in f:
                if line.strip():
                    examples.append(json.loads(line))
                    
        # Shuffle examples
        random.shuffle(examples)
        
        # Calculate split
        split_idx = int(len(examples) * (1 - test_size))
        train_examples = examples[:split_idx]
        test_examples = examples[split_idx:]
        
        # Save splits
        train_file = os.path.join(self.output_dir, "train.jsonl")
        test_file = os.path.join(self.output_dir, "test.jsonl")
        
        with open(train_file, 'w', encoding='utf-8') as f:
            for example in train_examples:
                f.write(json.dumps(example) + '\n')
                
        with open(test_file, 'w', encoding='utf-8') as f:
            for example in test_examples:
                f.write(json.dumps(example) + '\n')
                
        print(f"Created train/test split: {len(train_examples)} training examples, {len(test_examples)} test examples")


if __name__ == "__main__":
    import argparse
    
    parser = argparse.ArgumentParser(description="Collect training data from context files")
    parser.add_argument("--context_dir", type=str, default="../context_for_llm",
                       help="Directory containing context files")
    parser.add_argument("--output_dir", type=str, default="../data",
                       help="Directory to save training data")
    
    args = parser.parse_args()
    
    collector = DataCollector(args.context_dir, args.output_dir)
    collector.collect_all()
```

### 4. Create Axolotl Configuration

Next, create the configuration for the fine-tuning process:

```yaml
# config/finetune_config.yml
base_model: deepseek-coder-6.7b-instruct
model_type: DeepseekCoderConfig
tokenizer_type: DeepseekCoderTokenizer
tokenizer_config:
  model_max_length: 8192
  pad_token: "<pad>"
  bos_token: "<|begin_of_text|>"
  eos_token: "<|end_of_text|>"

datasets:
  - path: ./data/train.jsonl
    type: alpaca
    data_files:
      - train.jsonl
    split: train

dataset_prepared_path: ./data/prepared
val_dataset_size: 0.05
val_split_seed: 42

sequence_len: 4096
sample_packing: true
pad_to_sequence_len: true

# Training parameters
lora_r: 32
lora_alpha: 16
lora_dropout: 0.05
lora_target_modules:
  - q_proj
  - k_proj
  - v_proj
  - o_proj
  - gate_proj
  - up_proj
  - down_proj

learning_rate: 2e-4
lr_scheduler: cosine
lr_warmup_steps: 100
weight_decay: 0.0

train_batch_size: 2  # Adjust based on your GPU memory
micro_batch_size: 2
gradient_accumulation_steps: 1
num_epochs: 3
optimizer: adamw_8bit

gradient_checkpointing: true
early_stopping_patience: 3
save_steps: 20
val_steps: 20
logging_steps: 10

load_in_8bit: true
bf16: auto

output_dir: ./output/grasshopper-component-model
```

### 5. Create Fine-tuning Script

```python
# scripts/run_finetuning.py
import os
import subprocess
import argparse
from pathlib import Path

def run_finetuning(config_path, context_dir, output_dir):
    """Run the fine-tuning process using Axolotl"""
    # First collect training data
    print("Collecting training data...")
    data_dir = os.path.join(output_dir, "data")
    os.makedirs(data_dir, exist_ok=True)
    
    collect_cmd = [
        "python", "scripts/collect_training_data.py",
        "--context_dir", context_dir,
        "--output_dir", data_dir
    ]
    
    subprocess.run(collect_cmd, check=True)
    
    # Run fine-tuning with Axolotl
    print("\nStarting fine-tuning process...")
    finetune_cmd = [
        "accelerate", "launch", "-m", "axolotl.cli.train",
        config_path
    ]
    
    subprocess.run(finetune_cmd, check=True)
    
    print("\nFine-tuning complete! Model saved to:", os.path.join(output_dir, "output/grasshopper-component-model"))
    
    # Create Ollama model
    print("\nCreating Ollama model...")
    model_name = "grasshopper-component-generator"
    
    # Create Modelfile
    modelfile_path = os.path.join(output_dir, "Modelfile")
    with open(modelfile_path, "w") as f:
        f.write(f"""FROM deepseek-coder:6.7b-instruct
PARAMETER temperature 0.6
PARAMETER stop "<|end_of_text|>"
PARAMETER stop "</s>"
PARAMETER stop "```"

ADAPTER {os.path.join(output_dir, "output/grasshopper-component-model/adapter_model.bin")}

SYSTEM """
You are an expert Grasshopper component developer specialized in creating C# components for Rhino/Grasshopper.
When asked to create a component, you will produce complete, compilable C# code that follows best practices.
Only output valid C# code with no additional explanation. The code should include all required methods and properties.
"""
""")
    
    # Create Ollama model
    subprocess.run(["ollama", "create", model_name, "-f", modelfile_path], check=True)
    
    print(f"\nSuccess! Your fine-tuned model is now available as 'ollama run {model_name}'")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Run fine-tuning pipeline for Grasshopper component generation")
    parser.add_argument("--config", type=str, default="config/finetune_config.yml",
                       help="Path to Axolotl configuration")
    parser.add_argument("--context_dir", type=str, default="../context_for_llm",
                       help="Directory containing context files")
    parser.add_argument("--output_dir", type=str, default=".",
                       help="Directory for outputs")
    
    args = parser.parse_args()
    
    run_finetuning(args.config, args.context_dir, args.output_dir)
```

## Running the Pipeline

1. **Create configuration files**:
   ```bash
   mkdir -p config
   # Save the axolotl config to config/finetune_config.yml
   ```

2. **Prepare your data directory**:
   Make sure your `context_for_llm` directory contains:
   - PDFs from programming books
   - C# code samples (especially Grasshopper components)
   - Documentation in text/markdown format

3. **Run data collection only**:
   ```bash
   python scripts/collect_training_data.py
   ```

4. **Run the full pipeline**:
   ```bash
   python scripts/run_finetuning.py
   ```

## Integrating with Your Component Generator

Once the model is fine-tuned and available in Ollama, you can easily integrate it with your component generator:

1. **Add the fine-tuned model to the available models**:
   Modify your model selection dropdown to include the new model.

2. **Update the `OllamaModelParam.cs` file**:
   ```csharp
   // Add any local models to a preferred list
   private string[] _preferredModels = { "grasshopper-component-generator" };
   
   public OllamaModelParam()
   {
       Name = "Ollama Models";
       NickName = "M";
       Description = "Select an Ollama model installed locally";
       ListItems.Clear();
       RefreshModels();
   }

   private void RefreshModels()
   {
       try
       {
           // Show preferred models at the top
           foreach (var model in _preferredModels)
           {
               ListItems.Add(new GH_ValueListItem(model, $"\"{model}\""));
           }
   
           // Then get other local models
           var psi = new ProcessStartInfo("ollama", "list")
           {
               RedirectStandardOutput = true,
               UseShellExecute = false,
               CreateNoWindow = true
           };
           // ...rest of the method
       }
   }
   ```

3. **Optimize prompts for the fine-tuned model**:
   Update the system prompt to be more concise since the model now knows about Grasshopper components:

   ```csharp
   _systemPrompt = "Create a Grasshopper component based on the following description. Output only C# code with no additional explanations.";
   ```

4. **Adjust generation parameters**:
   The fine-tuned model will likely work better with lower temperature:

   ```csharp
   temperature = 0.3; // Lower temperature for more focused results
   ```

## Advanced Techniques

### Selective Merging of Fine-tuning Results

You can merge your fine-tuned adapter with the base model for even better performance:

```bash
python -m axolotl.cli.merge_lora \
  --base_model deepseek-coder-6.7b-instruct \
  --output_dir merged_model \
  --adapter_path output/grasshopper-component-model/adapter_model.bin
```

### Quantized Export

Export a 4-bit quantized version for faster inference:

```bash
python -m axolotl.cli.export_gguf \
  --model_dir merged_model \
  --output_dir quantized \
  --quantization q4_k_m
```

### Evaluate Model Quality

Create an evaluation script to test your model against a benchmark set of component descriptions:

```python
# scripts/evaluate_model.py
import subprocess
import json
import tempfile
import os
from pathlib import Path
import re

def evaluate_model(model_name, test_file, output_dir):
    """Evaluate model performance on component generation tasks"""
    # Load test cases
    test_cases = []
    with open(test_file, 'r') as f:
        for line in f:
            if line.strip():
                test_cases.append(json.loads(line))
    
    results = []
    correct = 0
    
    for i, case in enumerate(test_cases):
        print(f"Evaluating case {i+1}/{len(test_cases)}...")
        
        # Create prompt
        prompt = case["instruction"]
        if case["input"]:
            prompt += "\n\n" + case["input"]
        
        # Call Ollama
        ollama_cmd = ["ollama", "run", model_name, prompt]
        result = subprocess.run(ollama_cmd, capture_output=True, text=True)
        generated_code = result.stdout.strip()
        
        # Basic validation
        is_valid = validate_code(generated_code)
        
        # Save results
        results.append({
            "prompt": prompt,
            "expected": case["output"],
            "generated": generated_code,
            "is_valid": is_valid
        })
        
        if is_valid:
            correct += 1
    
    # Calculate accuracy
    accuracy = correct / len(test_cases) if test_cases else 0
    
    # Save results
    os.makedirs(output_dir, exist_ok=True)
    with open(os.path.join(output_dir, "evaluation_results.json"), 'w') as f:
        json.dump({
            "accuracy": accuracy,
            "correct": correct,
            "total": len(test_cases),
            "results": results
        }, f, indent=2)
    
    print(f"Evaluation complete: {correct}/{len(test_cases)} valid components ({accuracy:.2%} accuracy)")
    print(f"Results saved to {os.path.join(output_dir, 'evaluation_results.json')}")

def validate_code(code):
    """Basic validation of C# code for component patterns"""
    # Check for minimum code structure
    if not re.search(r'public class \w+\s*:\s*GH_Component', code):
        return False
        
    # Check for required methods
    required_methods = [
        r'protected override void RegisterInputParams',
        r'protected override void RegisterOutputParams',
        r'protected override void SolveInstance'
    ]
    
    for method in required_methods:
        if not re.search(method, code):
            return False
    
    # Check for component GUID
    if not re.search(r'public override Guid ComponentGuid', code):
        return False
    
    return True

if __name__ == "__main__":
    import argparse
    
    parser = argparse.ArgumentParser(description="Evaluate fine-tuned model for component generation")
    parser.add_argument("--model", type=str, default="grasshopper-component-generator",
                       help="Ollama model name to evaluate")
    parser.add_argument("--test_file", type=str, default="data/test.jsonl",
                       help="Path to test examples in JSONL format")
    parser.add_argument("--output_dir", type=str, default="evaluation",
                       help="Directory to save evaluation results")
    
    args = parser.parse_args()
    
    evaluate_model(args.model, args.test_file, args.output_dir)
```

## Continuous Improvement

1. **Regular Updates**: Re-run the pipeline with new components and documentation

2. **Feedback Loop**: Log components that had compilation errors and add them to your training data

3. **Active Learning**: Implement a system to automatically save successful generations for retraining

## Integration with Grasshopper

Create a feedback mechanism in your component generator to collect successful generations:

```csharp
private void LogSuccessfulGeneration(string description, string code)
{
    try
    {
        string feedbackDir = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), 
            "GenerationFeedback");
            
        Directory.CreateDirectory(feedbackDir);
        
        // Create a unique filename
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filename = Path.Combine(feedbackDir, $"successful_gen_{timestamp}.json");
        
        // Save as JSON for easy processing in the training pipeline
        var feedback = new
        {
            instruction = description,
            input = string.Empty,
            output = code,
            metadata = new
            {
                timestamp = DateTime.Now.ToString("o"),
                success = true,
                compilation_attempts = _compileAttempts
            }
        };
        
        string json = JsonSerializer.Serialize(feedback, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filename, json);
        
        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
            $"Logged successful generation for future training");
    }
    catch (Exception ex)
    {
        // Don't interrupt the main flow for logging errors
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
            $"Failed to log generation: {ex.Message}");
    }
}
```

Then call this method in your `ProcessAndCompileCode` method when compilation succeeds:

```csharp
// If reached here, compilation succeeded
if (success)
{
    // Log for future training
    LogSuccessfulGeneration(description, cleanCode);
}
```

## Automatic Model Improvement Workflow

Setting up a workflow for continuously improving your model:

### 1. Automate Training Data Collection

Create a script to periodically collect feedback into your training pipeline:

```python
# scripts/collect_feedback.py
import os
import json
import glob
import shutil
import argparse
from pathlib import Path

def collect_feedback(feedback_dir, data_dir):
    """Collect feedback files and add them to training data"""
    # Check for feedback directory
    if not os.path.exists(feedback_dir) or not os.path.isdir(feedback_dir):
        print(f"Feedback directory {feedback_dir} does not exist or is not a directory")
        return
        
    # Look for feedback files
    feedback_files = glob.glob(os.path.join(feedback_dir, "successful_gen_*.json"))
    
    if not feedback_files:
        print("No feedback files found")
        return
        
    print(f"Found {len(feedback_files)} feedback files")
    
    # Load existing training data
    training_file = os.path.join(data_dir, "train.jsonl")
    existing_hashes = set()
    
    if os.path.exists(training_file):
        with open(training_file, 'r', encoding='utf-8') as f:
            for line in f:
                if line.strip():
                    try:
                        example = json.loads(line)
                        # Calculate a simple hash of the output to avoid duplicates
                        output_hash = hash(example.get("output", ""))
                        existing_hashes.add(output_hash)
                    except json.JSONDecodeError:
                        continue
    
    # Create a file for new examples
    new_examples_file = os.path.join(data_dir, "new_examples.jsonl")
    new_count = 0
    
    with open(new_examples_file, 'w', encoding='utf-8') as out:
        for feedback_file in feedback_files:
            try:
                with open(feedback_file, 'r', encoding='utf-8') as f:
                    feedback = json.load(f)
                    
                # Check if this is a duplicate
                output_hash = hash(feedback.get("output", ""))
                if output_hash in existing_hashes:
                    print(f"Skipping duplicate feedback from {os.path.basename(feedback_file)}")
                    continue
                    
                # Remove metadata field before adding to training data
                if "metadata" in feedback:
                    del feedback["metadata"]
                    
                # Write to new examples file
                out.write(json.dumps(feedback) + '\n')
                new_count += 1
                
                # Move processed file to archive
                archive_dir = os.path.join(feedback_dir, "processed")
                os.makedirs(archive_dir, exist_ok=True)
                shutil.move(feedback_file, os.path.join(archive_dir, os.path.basename(feedback_file)))
                
            except Exception as e:
                print(f"Error processing {feedback_file}: {e}")
    
    if new_count > 0:
        # Merge with existing training data
        merged_file = os.path.join(data_dir, "train_with_feedback.jsonl")
        
        with open(merged_file, 'w', encoding='utf-8') as out:
            # Copy existing training data
            if os.path.exists(training_file):
                with open(training_file, 'r', encoding='utf-8') as f:
                    out.write(f.read())
            
            # Add new examples
            with open(new_examples_file, 'r', encoding='utf-8') as f:
                out.write(f.read())
        
        # Backup original training file
        if os.path.exists(training_file):
            backup_file = os.path.join(data_dir, f"train.{int(time.time())}.bak.jsonl")
            shutil.copy(training_file, backup_file)
            
        # Replace training file with merged file
        shutil.move(merged_file, training_file)
        
        print(f"Added {new_count} new examples to training data")
        print(f"Training data updated at {training_file}")
        
        # Clean up
        os.remove(new_examples_file)
    else:
        print("No new examples were added")
        os.remove(new_examples_file)
        
if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Collect feedback for model improvement")
    parser.add_argument("--feedback_dir", type=str, 
                       default="../GenerationFeedback",
                       help="Directory containing feedback files")
    parser.add_argument("--data_dir", type=str, 
                       default="./data",
                       help="Directory containing training data")
    
    args = parser.parse_args()
    
    collect_feedback(args.feedback_dir, args.data_dir)
```

### 2. Set Up a Continuous Training Loop

Create a script that runs periodically to incorporate feedback and retrain the model:

```python
# scripts/continuous_improvement.py
import os
import subprocess
import time
import argparse
import json
from pathlib import Path

def check_for_updates(feedback_dir, min_examples):
    """Check if we have enough new examples to trigger retraining"""
    if not os.path.exists(feedback_dir):
        return False
        
    feedback_files = os.listdir(feedback_dir)
    feedback_files = [f for f in feedback_files if f.startswith("successful_gen_") and f.endswith(".json")]
    
    return len(feedback_files) >= min_examples

def continuous_improvement(config_path, feedback_dir, data_dir, min_examples=20, check_interval=3600):
    """Monitor for new examples and retrain when threshold is reached"""
    print(f"Starting continuous improvement loop...")
    print(f"Will check for new examples every {check_interval} seconds")
    print(f"Will retrain when at least {min_examples} new examples are available")
    
    while True:
        if check_for_updates(feedback_dir, min_examples):
            print(f"Found {min_examples}+ new examples, starting improvement process...")
            
            # Collect feedback into training data
            subprocess.run([
                "python", "scripts/collect_feedback.py",
                "--feedback_dir", feedback_dir,
                "--data_dir", data_dir
            ], check=True)
            
            # Run fine-tuning with updated data
            model_name = f"grasshopper-component-generator-v{int(time.time())}"
            
            subprocess.run([
                "python", "scripts/run_finetuning.py",
                "--config", config_path,
                "--output_dir", f"./output/{model_name}"
            ], check=True)
            
            print(f"Improvement process completed, created new model: {model_name}")
            
            # Create a symlink for the "latest" model
            latest_link = "./output/latest"
            if os.path.exists(latest_link):
                os.remove(latest_link)
            os.symlink(f"./output/{model_name}", latest_link)
            
            # Sleep a bit longer after training
            time.sleep(check_interval * 2)
        else:
            print(f"Not enough new examples yet, checking again in {check_interval} seconds...")
            time.sleep(check_interval)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Continuous model improvement loop")
    parser.add_argument("--config", type=str, default="config/finetune_config.yml",
                       help="Path to Axolotl configuration")
    parser.add_argument("--feedback_dir", type=str, default="../GenerationFeedback",
                       help="Directory containing feedback files")
    parser.add_argument("--data_dir", type=str, default="./data",
                       help="Directory containing training data")
    parser.add_argument("--min_examples", type=int, default=20,
                       help="Minimum number of new examples to trigger retraining")
    parser.add_argument("--check_interval", type=int, default=3600,
                       help="Interval between checks (in seconds)")
    
    args = parser.parse_args()
    
    continuous_improvement(
        args.config, 
        args.feedback_dir, 
        args.data_dir,
        args.min_examples,
        args.check_interval
    )
```

## Setting Up as a Service

To keep the improvement process running in the background, set it up as a service:

### On macOS:

Create a LaunchAgent plist file:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.user.grasshopper-model-improvement</string>
    <key>ProgramArguments</key>
    <array>
        <string>/path/to/python</string>
        <string>/path/to/grasshopper_finetuning/scripts/continuous_improvement.py</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>/path/to/grasshopper_finetuning/logs/improvement.log</string>
    <key>StandardErrorPath</key>
    <string>/path/to/grasshopper_finetuning/logs/improvement_error.log</string>
    <key>WorkingDirectory</key>
    <string>/path/to/grasshopper_finetuning</string>
    <key>EnvironmentVariables</key>
    <dict>
        <key>PATH</key>
        <string>/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin</string>
    </dict>
</dict>
</plist>
```

Save as `~/Library/LaunchAgents/com.user.grasshopper-model-improvement.plist` and load with:

```bash
launchctl load ~/Library/LaunchAgents/com.user.grasshopper-model-improvement.plist
```

## Best Practices for Fine-tuning

1. **Quality over quantity**: Fewer high-quality examples are better than many poor examples

2. **Diverse examples**: Include components with different input/output patterns

3. **Real-world usage**: Include components that show typical Grasshopper SDK usage patterns

4. **Incremental fine-tuning**: Start with a strong base model, then fine-tune incrementally

5. **Target specific skills**: Create focused training sets for specific component types

## Troubleshooting

### Common Issues

1. **Out of memory**: Reduce batch size or use gradient checkpointing
   
2. **Poor generation quality**: Check training data quality, increase epochs, or adjust learning rate
   
3. **Slow inference**: Export to GGUF and use llama.cpp for faster inference

4. **Inconsistent styles**: Pre-process training examples to ensure consistent coding style

### Debugging Fine-tuning

Add verbose logging to your axolotl config:

```yaml
wandb:
  enabled: true
  project: grasshopper-component-generation
```

## Model Distribution

Once you have a fine-tuned model, share it with your team:

1. **Export to Ollama format**:
   ```bash
   ollama export grasshopper-component-generator > grasshopper-component-generator.ollama
   ```

2. **Import on another machine**:
   ```bash
   ollama import grasshopper-component-generator.ollama
   ```

3. **Or create a custom Modelfile for easier sharing**:
   ```
   FROM deepseek-coder:6.7b-instruct

   # Path to adapter weights
   ADAPTER ./adapter_model.bin
   
   PARAMETER temperature 0.7
   PARAMETER stop "<|end_of_text|>"
   PARAMETER stop "</s>"
   
   SYSTEM """
   You are an expert Grasshopper component developer...
   """
   ```

## Future Enhancements

1. **Multi-stage generation**: Use one model for planning and another for implementation

2. **Code analysis integration**: Use static analysis to validate generated components

3. **Component templates**: Initialize generation with skeleton code based on type

4. **Parameter suggestion**: Automatically suggest appropriate parameters based on description

5. **Language-specific LoRA adapters**: Train separate adapters for different component types

## Conclusion

By implementing this fine-tuning pipeline, you'll create a specialized model that excels at generating high-quality Grasshopper components. The continuous improvement cycle ensures the model gets better over time with each successful generation, creating a powerful assistant for your Grasshopper development workflow.

Your PDF context data serves as the initial corpus, but the real magic happens when the model starts learning from its own successful generations, eventually becoming an expert in your specific component style and requirements.
