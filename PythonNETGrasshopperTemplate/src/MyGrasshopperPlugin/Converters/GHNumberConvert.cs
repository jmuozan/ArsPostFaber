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
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;

namespace MyGrasshopperPlugin.Converters
{
    public static class GHNumberConvert
    {
        public static GH_Structure<GH_Number> ToTree(double[,] matrix)
        {
            GH_Structure<GH_Number> tree = new GH_Structure<GH_Number>();
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);

            for (int i = 0; i < rows; i++)
            {
                GH_Path path = new GH_Path(i);
                List<GH_Number> branch = new List<GH_Number>(cols);
                for (int j = 0; j < cols; j++)
                {
                    branch.Add(new GH_Number(matrix[i, j]));
                }
                tree.AppendRange(branch, path);
            }
            return tree;
        }

        public static List<GH_Number> ToBranch(double[] array)
        {
            return array.Select(n => new GH_Number(n)).ToList();
        }

        public static double[] ToArray(List<GH_Number> branch)
        {
            return branch.Select(n => n.Value).ToArray();
        }
    }
}
