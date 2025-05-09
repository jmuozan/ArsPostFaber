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
//------------------------------------------------------------------------------------------------------------

using System;
using System.IO;
using Python.Runtime;
using MyGrasshopperPluginCore.Converters;

namespace MyGrasshopperPluginCore.Application.PythonNETInit
{
    public static class PythonNETManager
    {
        public static bool IsInitialized { get; set; } = false;

        /// <summary>
        /// Initializes the Python engine. This method must be called before using any methods from the Python.Runtime namespace.
        /// </summary>
        /// <param name="condaEnvPath">The path to the conda environment where Python is installed.</param>
        /// <param name="pythonDllName">The Name of the python3xx.dll file contained in the conda environment.</param>
        /// <param name="pythonProjectDirectory">The path to the directory containing the python scripts.</param>
        /// <remarks>
        /// This method sets the following environment variables:
        /// PATH: adds condaEnvPath to PATH
        /// PYTHONHOME: sets to condaEnvPath
        /// PYTHONPATH: sets to the concat of site_packages, Lib, DLLs and pythonProjectDirectory
        /// PYTHONNET_PYDLL: sets to pythonDllPath
        /// </remarks>
        public static void Initialize(string condaEnvPath, string pythonDllName, string pythonProjectDirectory)
        {
            if (IsInitialized)
            {
                return; //nothing to do
            }
            try
            {
                string Lib = Path.Combine(condaEnvPath, "Lib");
                string site_packages = Path.Combine(Lib, "site-packages");
                string DLLs = Path.Combine(condaEnvPath, "DLLs");
                string pythonDllPath = Path.Combine(condaEnvPath, pythonDllName);

                var path = Environment.GetEnvironmentVariable("PATH").TrimEnd(';');
                if (!path.Contains(condaEnvPath))
                {
                    path = string.IsNullOrEmpty(path) ? condaEnvPath : path + ";" + condaEnvPath; //add condaEnvPath to PATH (only once)
                }

                Environment.SetEnvironmentVariable("PATH", path, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("PYTHONHOME", condaEnvPath, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("PYTHONPATH", $"{site_packages};{Lib};{DLLs};{pythonProjectDirectory}", EnvironmentVariableTarget.Process);

                //Runtime.PythonDLL = pythonDllPath;
                Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", pythonDllName);

                PythonEngine.PythonHome = condaEnvPath;
                PythonEngine.PythonPath = Environment.GetEnvironmentVariable("PYTHONPATH", EnvironmentVariableTarget.Process);

                PythonEngine.Initialize();
                Main.RegisterConverters();
                IsInitialized = true;
                return;
            }
            catch (Exception e)
            {
                throw;
            }
        }

        /// <summary>
        /// Shuts down the Python engine if it is initialized.
        /// </summary>
        public static void ShutDown()
        {
            if (!IsInitialized)
            {
                return; //nothing to do
            }
            //else Python is initialized and must be closed
            try
            {
                PythonEngine.Shutdown();
                IsInitialized = false;
            }
            catch (Exception e)
            {
                throw;
            }
        }
    }
}