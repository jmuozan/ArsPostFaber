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

using Python.Runtime;

namespace MyGrasshopperPluginCore.Application.PythonNETSolvers
{
    public static class TestScriptSolver
    {
        public static string? Solve(string str0, string str1)
        {
            string pythonScript = "test_script";

            string result = "";

            // following code is inspired from https://github.com/pythonnet/pythonnet/wiki/Threading
            var m_threadState = PythonEngine.BeginAllowThreads();
            using (Py.GIL())
            {
                try
                {
                    dynamic test_script = PyModule.Import(pythonScript);
                    dynamic mainFunction = test_script.main;
                    dynamic pyResult = mainFunction(str0, str1);
                    result = (string)pyResult;
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
