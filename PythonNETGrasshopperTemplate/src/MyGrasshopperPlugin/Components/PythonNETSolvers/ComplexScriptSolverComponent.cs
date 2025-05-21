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
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using MyGrasshopperPlugin.Converters;
using MyGrasshopperPluginCore.CS_Model;
using MyGrasshopperPluginCore.Application.PythonNETInit;
using MyGrasshopperPluginCore.Application.PythonNETSolvers;

namespace MyGrasshopperPlugin.Components
{
    public class ComplexScriptSolverComponent : GH_Component
    {

        public ComplexScriptSolverComponent()
          : base("ComplexScriptSolverComponent", "Solve complex_script.py",
                "This is a component that shows how to transfer complex data between the main Grasshopper/C# component and the python script.\n" +
                "For instance:\n" +
                "this script takes as input in Grasshopper/C#: a list, a number of columns and a number of rows \n" +
                "These input are sent to python\n" +
                "Python turns the list into a Numpy array of shape (rowNumber,colNumber)\n" +
                "then the Numpy array is returned in C#/Grasshopper as a Tree",
              AccessToAll.GHAssemblyName, AccessToAll.GHComponentsFolder1)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("A List of Numbers", "list", "A list of numbers to be converted in python into a Numpy array with rowNumber rows and colNumber columns", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Row Number", "rowNumber", "The Number of Rows of the returned Numpy array", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Column Number", "colNumber", "The Number of Columns of the returned Numpy array", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Grasshopper tree", "tree", "A python numpy array converted back into a Grasshopper tree", GH_ParamAccess.tree);
        }


        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!PythonNETManager.IsInitialized)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Python has not been started. Please start the 'StartPython.NET' component first.");
                return;
            }

            var branch = new List<GH_Number>();
            int row = 0;
            int col = 0;

            if (!DA.GetDataList(0, branch)) { return; }
            if (!DA.GetData(1, ref row)) { return; }
            if (!DA.GetData(2, ref col)) { return; }

            var csResult = new CS_Result();
            try
            {
                var csData = new CS_Data(GHNumberConvert.ToArray(branch), row, col);
                csResult = ComplexScriptSolver.Solve(csData);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
            finally
            {
                DA.SetDataTree(0, GHNumberConvert.ToTree(csResult.Matrix));
            }
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
            get { return new Guid("e35a0cc7-b1c3-452c-8193-87a648485379"); }
        }

    }
}