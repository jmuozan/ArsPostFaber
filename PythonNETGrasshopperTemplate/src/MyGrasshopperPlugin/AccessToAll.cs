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

//Copyright < 2021 - 2025 > < Universit� catholique de Louvain (UCLouvain)>

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
using Grasshopper.Kernel;

namespace MyGrasshopperPlugin
{



    /// <summary>
    /// the class AccessToAll contains the properties and methods accessible from all Grasshopper components.
    /// </summary>
    public static class AccessToAll
    {

        private static MyGrasshopperPluginInfo info = new MyGrasshopperPluginInfo();

        public static string GHAssemblyName
        {
            get
            {
                return info.Name;
            }
        }
        public static string GHComponentsFolder0 { get { return "0. Initialize Python"; } }
        public static string GHComponentsFolder1 { get { return "1. Main Components"; } } 


        /// <summary>
        /// Gets or sets a value indicating whether the plugin is in user mode.
        /// True for user mode, false for developer mode.
        /// </summary>
        public static bool user_mode = true;

        /// <summary>
        /// Gets the Special Folder with path : "C:\\Users\\Me\\AppData\\Roaming\\Grasshopper\\Libraries\\"
        /// </summary>
        public static string specialFolder
        {
            get 
            { 
                string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); // AppData = "C:\Users\Me\AppData\Roaming"
                return Path.Combine(AppData,"Grasshopper", "Libraries");
            }
        }

        /// <summary>
        /// Gets the root directory (containing the solution, the python and C# projects, the temporary folder,...).
        /// </summary>
        public static string rootDirectory
        {
            get
            {
                if (user_mode)
                {
                    return Path.Combine(specialFolder, GHAssemblyName);
                }
                else
                {
                    var currentDirectory = Directory.GetCurrentDirectory(); //rootDirectory/MyGrasshopperPlugin/bin/Debug/net48/
                    for (int i = 0; i < 4; i++) //rootDirectory is 4 levels above the current directory
                    {
                        currentDirectory = Directory.GetParent(currentDirectory).FullName;
                    }
                    return currentDirectory;
                }
            }
        }

        /// <summary>
        /// Gets the python project directory (containing the python scripts,...).
        /// </summary>
        public static string pythonProjectDirectory
        {
            get { return Path.Combine(rootDirectory, "MyPythonScripts"); }
        }

        public static string tempDirectory
        {
            get { return Path.Combine(rootDirectory, ".temp"); }
        }






    }
}

