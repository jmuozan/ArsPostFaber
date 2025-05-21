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
using MyGrasshopperPluginCore.Application.PythonNETInit;
using MyGrasshopperPluginCore.Application.PythonNETSolvers;

namespace MyGrasshopperPlugin.Components
{
    public class TestPythonNETComponent : GH_Component
    {

        public TestPythonNETComponent()
          : base("TestPythonNET", "test_script.py",
              "Test to check if Python.NET works well.",
              AccessToAll.GHAssemblyName, AccessToAll.GHComponentsFolder0)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        /// <param name="pManager">The input parameter manager.</param>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("TextToLower", "ToLower", "a string argument to be lowercase", GH_ParamAccess.item);
            pManager.AddTextParameter("TextToUpper", "ToUpper", "a string argument to be uppercase", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        /// <param name="pManager">The output parameter manager.</param>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Result text", "result", "string0.ToLower + string1.ToUpper", GH_ParamAccess.item);
        }

        /// <summary>
        /// This Grasshopper component executes the Python script "test_script.py" with the arguments "str0" and "str1".
        /// </summary>
        /// <param name="DA">The data access object for retrieving input and setting output.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!PythonNETManager.IsInitialized)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Python has not been started. Please start the 'StartPython.NET' component first.");
                return;
            }

            string str0 = "";
            string str1 = "";

            if (!DA.GetData(0, ref str0)) { return; }
            if (!DA.GetData(1, ref str1)) { return; }

            string result = "";
            try
            {
                result = TestScriptSolver.Solve(str0, str1);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
            finally
            {
                DA.SetData(0, result);
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("f817f8e2-0688-4057-9622-736f04be82d8"); }
        }
    }
}
