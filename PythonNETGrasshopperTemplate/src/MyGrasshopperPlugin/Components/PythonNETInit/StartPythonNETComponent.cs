//PythonNETGrasshopperTemplate

//Copyright <2025> <Jonas Feron>

//Licensed under the Apache License, Version 2.0 (the "License");
//you may not use this file except in compliance with the License.
//You may obtain a copy of the License at

//    http://www.apache.org/licenses/LICENSE-2.0

//Unless required by applicable law or agreed to in writing, software
//distributed under the License is distributed on an "AS IS" BASIS,
//WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//See the License for the specific language governing permissions and
//limitations under the License.

//List of the contributors to the development of PythonNETGrasshopperTemplate: see NOTICE file.
//Description and complete License: see NOTICE file.

//this file was imported from https://github.com/JonasFeron/PythonConnectedGrasshopperTemplate and is used WITH modifications.
//------------------------------------------------------------------------------------------------------------

//Copyright < 2021 - 2025 > < Université catholique de Louvain (UCLouvain)>

//Licensed under the Apache License, Version 2.0 (the "License");
//you may not use this file except in compliance with the License.
//You may obtain a copy of the License at

//    http://www.apache.org/licenses/LICENSE-2.0

//Unless required by applicable law or agreed to in writing, software
//distributed under the License is distributed on an "AS IS" BASIS,
//WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//See the License for the specific language governing permissions and
//limitations under the License.

//List of the contributors to the development of PythonConnectedGrasshopperTemplate: see NOTICE file.
//Description and complete License: see NOTICE file.
//------------------------------------------------------------------------------------------------------------

using System;
using Grasshopper.Kernel;
using System.IO;
using Python.Runtime;
using MyGrasshopperPluginCore.Application.PythonNETInit;

namespace MyGrasshopperPlugin.Components
{
    public class StartPythonNETComponent : GH_Component
    {
        private static string default_anacondaPath
        {
            get
            {
                if (PythonNETConfig.anacondaPath != null)
                {
                    return PythonNETConfig.anacondaPath;
                }
                else
                {
                    return @"C:\Users\Me\Anaconda3";
                }
            }
        }
        private static readonly string default_condaEnvName = "base";
        private static string default_pythonDllName
        {
            get
            {
                if (PythonNETConfig.pythonDllName != null)
                {
                    return PythonNETConfig.pythonDllName;
                }
                else
                {
                    return @"python3xx.dll";
                }
            }
        }


        public StartPythonNETComponent()
          : base("StartPython.NET", "StartPy",
              "Initialize Python.NET before running any calculation", AccessToAll.GHAssemblyName, AccessToAll.GHComponentsFolder0)
        {
            Grasshopper.Instances.DocumentServer.DocumentRemoved += DocumentClose;
        }


        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Start Python", "Start", "Connect here a toggle. If true, Python/Anaconda starts and can calculate.", GH_ParamAccess.item);

            pManager.AddBooleanParameter("User mode", "user", "true for user mode, false for developer mode.", GH_ParamAccess.item, true);
            pManager[1].Optional = true;
            pManager.AddTextParameter("Anaconda Directory", "condaPath", "Path to the directory where Anaconda3 is installed.", GH_ParamAccess.item, default_anacondaPath);
            pManager[2].Optional = true;
            pManager.AddTextParameter("conda Environment Name", "condaEnv", "Name of the conda environment to activate.", GH_ParamAccess.item, default_condaEnvName);
            pManager[3].Optional = true;
            pManager.AddTextParameter("python3xx.dll file", ".dll", "Name of the \"python3xx.dll\" file contained in the specified conda environment", GH_ParamAccess.item, default_pythonDllName);
            pManager[4].Optional = true;

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //1) Initialize and Collect Data
            bool start = false;
            bool _user_mode = true;
            string _anacondaPath = default_anacondaPath;
            string _condaEnvName = default_condaEnvName;
            string _pythonDllName = default_pythonDllName;

            if (!DA.GetData(0, ref start)) return;
            if (!DA.GetData(1, ref _user_mode)) return;
            if (!DA.GetData(2, ref _anacondaPath)) return;
            if (!DA.GetData(3, ref _condaEnvName)) return;
            if (!DA.GetData(4, ref _pythonDllName)) return;

            //2) Check validity of user input ?
            ConfigurePythonNET(_user_mode, _anacondaPath, _condaEnvName, _pythonDllName);

            //3) Initialize Python.NET, following https://github.com/pythonnet/pythonnet/wiki/Using-Python.NET-with-Virtual-Environments

            if (start && PythonNETManager.IsInitialized)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Python.NET is already started.");
                return;
            }
            if (start && !PythonNETManager.IsInitialized) 
            {
                PythonNETManager.Initialize(PythonNETConfig.condaEnvPath, PythonNETConfig.pythonDllName, AccessToAll.pythonProjectDirectory);

                
                var m_threadState = PythonEngine.BeginAllowThreads();
                using (Py.GIL())
                {
                    dynamic np = Py.Import("numpy");
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Python.NET setup completed.");
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"According to Numpy, cos(2*pi)= {np.cos(np.pi * 2)}");
                }
                PythonEngine.EndAllowThreads(m_threadState);
            }

            if (!start)
            {
                PythonNETManager.ShutDown();
            }
            if (!PythonNETManager.IsInitialized)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Python.NET is closed. Please restart Python.NET.");
            }

        }

        /// <summary>
        /// When Grasshopper is closed, stop the PythonNETInit engine.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="doc"></param>
        private void DocumentClose(GH_DocumentServer sender, GH_Document doc)
        {
            PythonNETManager.ShutDown();
        }


        /// <summary>
        /// Checks the user input for validity and sets the configuration accordingly.
        /// </summary>
        /// <param name="userMode">Indicates whether the user mode is enabled.</param>
        /// <param name="anacondaPath">The path to the Anaconda installation directory.</param>
        /// <param name="condaEnvName">The name of the conda environment to activate.</param>
        /// <param name="pythonDllName">The name of the python DLL file.</param>
        private void ConfigurePythonNET(bool userMode, string anacondaPath, string condaEnvName, string pythonDllName)
        {
            #region rootDirectory and pythonProjectDirectory
            AccessToAll.user_mode = userMode;

            if (AccessToAll.user_mode && !Directory.Exists(AccessToAll.rootDirectory)) //User mode
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Check that {AccessToAll.GHAssemblyName} has been correctly installed in: {AccessToAll.specialFolder}");
                return;
            }
            if (!AccessToAll.user_mode && !Directory.Exists(AccessToAll.rootDirectory)) //Developer mode
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Check the path to {AccessToAll.rootDirectory}");
                return;
            }
            if (!Directory.Exists(AccessToAll.pythonProjectDirectory))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Check the path to {AccessToAll.pythonProjectDirectory}");
                return;
            }
            #endregion rootDirectory and pythonProjectDirectory

            #region PythonNET configuration (anacondaPath, condaEnvName, pythonDllName)
            //anacondaPath
            try
            {
                PythonNETConfig.anacondaPath = anacondaPath;
            }
            catch (ArgumentException e)
            {
                string default_msg = $"Please provide a valid path, similar to: {default_anacondaPath}";
                if (anacondaPath == default_anacondaPath)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Impossible to find a valid Anaconda3 Installation. " + default_msg);
                    return;
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message + default_msg);
                    return;
                }
            }
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"A valid Anaconda3 installation was found here: {PythonNETConfig.anacondaPath}");

            //condaEnvName
            try
            {
                PythonNETConfig.condaEnvName = condaEnvName;
            }
            catch (ArgumentException e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                return;
            }
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"\"{PythonNETConfig.condaEnvName}\" is a valid anaconda environment");

            //pythonDllName
            try
            {
                PythonNETConfig.pythonDllName = pythonDllName;
            }
            catch (ArgumentException e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                return;
            }
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{PythonNETConfig.pythonDllName} is a valid .dll file");
            #endregion PythonNET configuration (anacondaPath, condaEnvName, pythonDllName)
        }


        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("05f48156-22ca-4125-a32f-15a1cd8b27e6"); }
        }









    }

}
