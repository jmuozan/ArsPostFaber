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

using System.Numerics;
using Grasshopper.Kernel;
using MyGrasshopperPluginCore.CS_Model;
using Python.Runtime;
using Rhino.Runtime;

namespace MyGrasshopperPluginCore.Application.PythonNETSolvers
{
    public static class ComplexScriptSolver
    {
        public static CS_Result? Solve(CS_Data csData)
        {
            string pythonScript = "complex_script";
            var m_threadState = PythonEngine.BeginAllowThreads();
            CS_Result? result = null;

            using (Py.GIL())
            {
                try
                {
                    PyObject pyData = csData.ToPython();
                    dynamic script = PyModule.Import(pythonScript);
                    dynamic mainFunction = script.main;
                    dynamic pyResult = mainFunction(pyData);
                    result = pyResult.As<CS_Result>();
                }
                catch (Exception e)
                {
                    throw;
                }
            }

            PythonEngine.EndAllowThreads(m_threadState);
            return result;
        }
    }
}
