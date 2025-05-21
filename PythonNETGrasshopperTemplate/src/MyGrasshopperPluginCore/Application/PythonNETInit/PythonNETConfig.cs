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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyGrasshopperPluginCore.Application.PythonNETInit
{

    public static class PythonNETConfig
    {

        #region Properties


        private static string _anacondaPath = null;

        /// <summary>
        /// Gets the path to the Anaconda installation directory.
        /// </summary>
        /// <remarks>
        /// This property checks for the existence of Anaconda in two possible locations:
        /// 1. The user's profile directory (e.g., "C:\Users\Me\Anaconda3")
        /// 2. The ProgramData directory (e.g., "C:\ProgramData\Anaconda3")
        /// If Anaconda is found in either location, the path is returned. Otherwise, null is returned.
        /// </remarks>
        public static string anacondaPath
        {
            get
            {
                if (_anacondaPath != null)
                {
                    return _anacondaPath;
                }
                else
                {
                    string[] possiblePaths = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Anaconda3"), // "C:\Users\Me\Anaconda3"
                    @"C:\ProgramData\Anaconda3"
                    };
                    foreach (var path in possiblePaths)
                    {
                        if (Directory.Exists(path))
                        {
                            return path;
                        }
                    }
                    return null;
                }
            }
            set
            {
                if (File.Exists(Path.Combine(value, "python.exe")))
                {
                    _anacondaPath = value;
                }
                else
                {
                    throw new ArgumentException("The specified path does not contain a valid Anaconda3 installation. ");
                }
            }
        }


        private static string _condaEnvName = "base";

        /// <summary>
        /// Gets or sets the name of the conda environment.
        /// </summary>
        public static string condaEnvName
        {
            get
            {
                return _condaEnvName;
            }
            set
            {
                //check if value is a valid environment
                if (value == "base")
                {
                    _condaEnvName = value;
                }
                else if (File.Exists(Path.Combine(anacondaPath, "envs", value, "python.exe")))
                {
                    _condaEnvName = value;
                }
                else
                {
                    throw new ArgumentException("The specified anaconda environment does not exist.");
                }
            }
        }

        /// <summary>
        /// Gets the path to the conda environment.
        /// </summary>
        /// <remarks>
        /// If the conda environment is the base environment, the Anaconda installation path is returned.
        /// Otherwise, the path to the specified conda environment is returned.
        /// </remarks>
        public static string condaEnvPath
        {
            get
            {
                if (condaEnvName == "base")
                {
                    return anacondaPath;
                }
                else
                {
                    return Path.Combine(anacondaPath, "envs", condaEnvName);
                }
            }
        }

        private static string _pythonDllName = null;
        public static string pythonDllName
        {
            get
            {
                if (_pythonDllName == null)
                {
                    string[] possibleNames = { "python319.dll", "python318.dll", "python317.dll", "python316.dll", "python315.dll", "python314.dll", "python313.dll", "python312.dll", "python311.dll", "python310.dll", "python39.dll", "python38.dll", "python37.dll", "python36.dll", "python35.dll", "python34.dll", "python33.dll", "python32.dll", "python31.dll" };
                    foreach (var name in possibleNames)
                    {
                        if (File.Exists(Path.Combine(condaEnvPath, name)))
                        {
                            return name;
                        }
                    }
                    return null;
                }
                else
                {
                    return _pythonDllName;
                }
            }
            set
            {
                if (File.Exists(Path.Combine(condaEnvPath, value)))
                {
                    _pythonDllName = value;
                }
                else
                {
                    throw new ArgumentException("The specified \"python3xx.dll\" does not exist in the specified Anaconda environment.\n" +
                        $"Please check that {value} file exists in {condaEnvPath}.\n" +
                        "You may require to \"pip install pythonnet\" in this environment.");
                }
            }
        }
        #endregion Properties
    }
}

