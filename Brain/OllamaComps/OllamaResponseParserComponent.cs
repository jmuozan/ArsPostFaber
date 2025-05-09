using System;
using Grasshopper.Kernel;
using System.Text.Json;

namespace Brain.OllamaComps
{
    public class OllamaResponseParserComponent : GH_Component
    {
        public OllamaResponseParserComponent()
          : base("Parse Ollama Response", "OllamaParser",
              "Parses the JSON response from Ollama",
              "Brain", "LLM") { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Response", "R", "JSON response from Ollama", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Generated Text", "T", "The generated text", GH_ParamAccess.item);
            pManager.AddTextParameter("Model", "M", "Model used for generation", GH_ParamAccess.item);
            pManager.AddNumberParameter("Total Duration", "D", "Total time taken in seconds", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Prompt Tokens", "PT", "Number of tokens in the prompt", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Total Tokens", "TT", "Total number of tokens used", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string response = string.Empty;
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
                string generatedText = string.Empty;
                string model = string.Empty;
                double totalDuration = 0;
                int promptTokens = 0;
                int totalTokens = 0;
                if (root.TryGetProperty("response", out var respEl)) generatedText = respEl.GetString();
                if (root.TryGetProperty("model", out var modelEl)) model = modelEl.GetString();
                if (root.TryGetProperty("total_duration", out var durEl)) totalDuration = durEl.GetInt64() / 1e9;
                if (root.TryGetProperty("prompt_eval_count", out var ptEl)) promptTokens = ptEl.GetInt32();
                if (root.TryGetProperty("eval_count", out var evEl)) totalTokens = promptTokens + evEl.GetInt32();
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

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("D9CA5E85-7A23-4B5F-A018-C9D48FA5B3F1");
    }
}