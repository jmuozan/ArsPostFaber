# PythonNETGrasshopperTemplate

# Copyright <2025> <Jonas Feron>

# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at

#     http://www.apache.org/licenses/LICENSE-2.0

# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# List of the contributors to the development of PythonNETGrasshopperTemplate: see NOTICE file.
# Description and complete License: see NOTICE file.

# this file was imported from https://github.com/JonasFeron/PythonConnectedGrasshopperTemplate and is used with modification.
# ------------------------------------------------------------------------------------------------------------

# Copyright <2021-2025> <Université catholique de Louvain (UCLouvain)>

# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at

#     http://www.apache.org/licenses/LICENSE-2.0

# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# List of the contributors to the development of PythonConnectedGrasshopperTemplate: see NOTICE file.
# Description and complete License: see NOTICE file.
# ------------------------------------------------------------------------------------------------------------

import numpy as np


class Py_Data:
    def __init__(self, alist=None, row_number=0, col_number=0):
        """
        Initialize a py_data object that matches the C#_Data class.
        
        Args:
            alist (list): List of double values
            row_number (int): Number of rows
            col_number (int): Number of columns
        """
        self.array = np.array(alist,dtype=float) if alist is not None else np.array([])
        self.row_number = row_number
        self.col_number = col_number
    