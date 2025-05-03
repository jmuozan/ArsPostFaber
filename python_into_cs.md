I have some Python code in my c# plugin. To do that I embed the Python script into the c# project and set the “Build Action” to “Embedded resource”. I create a c# file with the same name as the Python script and in the c# file I create a command class and inherit from the following class:

```cs
using System;
using System.Collections.Generic;
using System.Text;
using Rhino.Commands;
using System.Reflection;
using System.IO;
using Rhino.DocObjects;
using Rhino;


namespace Foo
{

    public enum PythonEngine
    {
        IronPython=0,
        CPython=1
    }

    public abstract class PythonCommand : Command
    {

        public PythonEngine Engine { get; set; } = PythonEngine.IronPython;

        public string scriptName
        {
            get
            {
                return $"{this.GetType().Name}.py";
            }
        }

        // get a path to extract the python script to
        public string scriptPath
        {
            get
            {

                var assembly = Assembly.GetExecutingAssembly();
                string asm_path = System.IO.Path.GetDirectoryName(assembly.Location);
                return Path.Combine(asm_path, "python", this.scriptName);
            }
        }

        // URI of the script within the compiled assembly
        public string scriptResourceName
        {
            get
            {
                return $"{this.GetType().Namespace}.{scriptName}";
            }
        }

        private string scriptContent;

        protected static PythonCommand _instance;

        public PythonCommand()
        {
            _instance = this;
        }
        public static PythonCommand Instance
        {
            get { return _instance; }
        }

        public override string EnglishName
        {
            get { return $"{this.GetType().Name}"; }
        }

        // Extract the script to disk
        public bool ExtractScript()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string result;

                using (Stream stream = assembly.GetManifestResourceStream(this.scriptResourceName))
                using (StreamReader reader = new StreamReader(stream))
                {
                    result = reader.ReadToEnd();
                }

                string scriptDir = Path.GetDirectoryName(this.scriptPath);
                if (!Directory.Exists(scriptDir))
                {
                    Directory.CreateDirectory(scriptDir);
                }

                File.WriteAllText(this.scriptPath, result);

                return true;
            }
            catch (System.Exception ex)
            {
                return false;
            }
        }

        // Read the script and store it's content
        private bool GetScript()
        {
            if (!File.Exists(this.scriptPath))
            {
                this.ExtractScript();
            }
            if (File.Exists(this.scriptPath))
            {
                this.scriptContent = File.ReadAllText(this.scriptPath);
                return true;
            }
            else
            {
                return false;
            }
        }

        // Run the command using either the older IronPython engine or the newer CPython engine
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            int scriptResult = 1;
            if (this.GetScript())
            {
                if (this.Engine == PythonEngine.IronPython)
                {
                    Rhino.Runtime.PythonScript script = Rhino.Runtime.PythonScript.Create();
                    script.ExecuteScript(this.scriptContent);
                    dynamic RunCommand = script.GetVariable("RunCommand");
                    scriptResult = RunCommand(mode == RunMode.Interactive);
                }
                else if (this.Engine == PythonEngine.CPython) 
                {
                    string command = $"_-ScriptEditor _Run \"{this.scriptPath}\"";
                    bool success = Rhino.RhinoApp.RunScript(command, false);
                    scriptResult = success ? 1 : 0;
                }

                if (scriptResult == 0)
                {
                    return Result.Success;
                }
                else if (scriptResult == 1)
                {
                    return Result.Cancel;
                }
                else
                {
                    return Result.Failure;
                }
            }
            else
            {
                Rhino.RhinoApp.WriteLine($"File {this.scriptPath} was not found");
                return Result.Failure;
            }
        }
    }
}
```
The c# file then is simply:

```cs
namespace Foo
{
    public class MyCommandName: PythonCommand
    {
    }
}
```