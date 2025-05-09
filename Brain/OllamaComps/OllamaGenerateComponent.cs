using System;
using Grasshopper.Kernel;
using Brain.Templates;

namespace Brain.OllamaComps
{
    public class OllamaGenerateComponent : GH_Component_HTTPAsync
    {
        public OllamaGenerateComponent()
          : base("Ollama Generate", "Ollama",
              "Generates text using a locally running Ollama model",
              "Brain", "LLM")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Send", "S", "Perform the request?", GH_ParamAccess.item, false);
            pManager.AddTextParameter("Model", "M", "Ollama model name (e.g., deepseek-r1:1.5b)", GH_ParamAccess.item, "deepseek-r1:1.5b");
            pManager.AddTextParameter("Prompt", "P", "Prompt to send to the model", GH_ParamAccess.item);
            pManager.AddNumberParameter("Temperature", "T", "Temperature for generation (0-1)", GH_ParamAccess.item, 0.7);
            pManager.AddIntegerParameter("Max Tokens", "MT", "Maximum number of tokens to generate", GH_ParamAccess.item, 2048);
            pManager.AddTextParameter("URL", "U", "Ollama API URL (default: http://localhost:11434/api/generate)", GH_ParamAccess.item, "http://localhost:11434/api/generate");
            pManager.AddIntegerParameter("Timeout", "TO", "Timeout for the request in ms", GH_ParamAccess.item, 30000);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Response", "R", "Generated text response", GH_ParamAccess.item);
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
                        break;
                }
                DA.SetData(0, _response);
                _shouldExpire = false;
                return;
            }

            bool active = false;
            string model = string.Empty;
            string prompt = string.Empty;
            double temperature = 0.7;
            int maxTokens = 2048;
            string url = string.Empty;
            int timeout = 30000;

            if (!DA.GetData("Send", ref active)) return;
            if (!active)
            {
                _currentState = RequestState.Off;
                _shouldExpire = true;
                _response = string.Empty;
                ExpireSolution(true);
                return;
            }
            if (!DA.GetData("Model", ref model)) return;
            if (!DA.GetData("Prompt", ref prompt)) return;
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
            if (string.IsNullOrEmpty(prompt))
            {
                _response = "Empty prompt";
                _currentState = RequestState.Error;
                _shouldExpire = true;
                ExpireSolution(true);
                return;
            }

            string body = $"{{\"model\":\"{model}\",\"prompt\":\"{prompt.Replace("\"", "\\\"")}\",\"temperature\":{temperature},\"max_tokens\":{maxTokens},\"stream\":false}}";
            _currentState = RequestState.Requesting;
            this.Message = "Requesting...";
            POSTAsync(url, body, "application/json", string.Empty, timeout);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("8B267D39-DC46-4F4F-8D91-6F0C3F1B8A2C");
    }
}