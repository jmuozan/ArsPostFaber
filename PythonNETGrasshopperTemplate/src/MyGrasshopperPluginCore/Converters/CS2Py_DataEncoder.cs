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

using MyGrasshopperPluginCore.CS_Model;
using Python.Runtime;

namespace MyGrasshopperPluginCore.Converters
{
    public class CS2Py_DataEncoder : IPyObjectEncoder
    {
        public bool CanEncode(Type type)
        {
            return type == typeof(CS_Data);
        }

        public PyObject? TryEncode(object obj)
        {
            if (!CanEncode(obj.GetType()))
                return null;

            var data = (CS_Data)obj;
            using (Py.GIL())
            {
                // Import the Python module containing the py_data class
                dynamic pyModel = Py.Import("py_model.py_data");

                // Create a new instance of py_data with our C# data
                return pyModel.Py_Data(
                    data.Array,
                    data.RowNumber,
                    data.ColNumber
                );
            }
        }
    }
}