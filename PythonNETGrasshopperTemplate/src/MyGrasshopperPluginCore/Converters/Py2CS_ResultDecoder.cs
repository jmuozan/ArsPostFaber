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
using System.Runtime.InteropServices;

namespace MyGrasshopperPluginCore.Converters
{
    public class Py2CS_ResultDecoder : IPyObjectDecoder
    {
        public bool CanDecode(PyType objectType, Type targetType)
        {
            if (targetType != typeof(CS_Result))
                return false;

            using (Py.GIL())
            {
                try
                {
                    return objectType.Name == "Py_Result";
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool TryDecode<T>(PyObject pyObj, out T? value)
        {
            value = default;
            if (typeof(T) != typeof(CS_Result))
                return false;

            using (Py.GIL())
            {
                try
                {
                    dynamic py = pyObj.As<dynamic>();
                    dynamic npArray = py.matrix;
                    
                    //var matrix = npArray.As<double[,]>(); does not work
                    var matrix = ToCSArray2D(npArray);

                    value = (T)(object)new CS_Result { Matrix = matrix };
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in TryDecode: {ex.Message}");
                    return false;
                }
            }
        }

        private static double[,] ToCSArray2D(dynamic npArray)
        {
            // Get array dimensions
            var shape = ((PyObject)npArray.shape).As<int[]>();
            if (shape.Length != 2)
            {
                throw new ArgumentException("Expected 2D numpy array");
            }

            // Get the numpy array data directly as a contiguous array
            var flatData = ((PyObject)npArray.ravel()).As<double[]>();
            
            // Create the 2D array with the correct dimensions
            var matrix = new double[shape[0], shape[1]];
            
            // Copy the data directly into the 2D array
            Buffer.BlockCopy(flatData, 0, matrix, 0, flatData.Length * sizeof(double));
            
            return matrix;
        }
    }
}