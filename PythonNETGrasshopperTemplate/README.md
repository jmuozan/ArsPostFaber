# PythonNETGrasshopperTemplate  
A structured template for developing **Grasshopper plugins** using **C# and Python.NET**.  

## Overview  
**PythonNETGrasshopperTemplate** provides an efficient framework for integrating **Python.NET** into Grasshopper plugin development. By leveraging **Python.NET** and the **official Grasshopper development template** from McNeel, this template enables developers to create powerful **Grasshopper components** that seamlessly execute Python scripts within C#.  

### Key Features:  
- **Python.NET Integration** – Embed and call **Python 3 classes and methods** directly within C# Grasshopper components.  
- **Seamless Grasshopper Development** – Built on the official **Grasshopper template** for structured plugin development.  
- **Direct Python Library Access** – Use **NumPy, Pandas, SciPy**, and other Python libraries inside Grasshopper without extra wrappers.  
- **Multi-Version Compatibility** – Supports **Rhino 7 and Rhino 8** for maximum flexibility.  
- **Enhanced Workflow** – Develop and manage **multiple Grasshopper components** efficiently in **Visual Studio**.  

## Why Use PythonNETGrasshopperTemplate?  
✅ **Full Python and .NET Compatibility** – Leverage both Python’s flexibility and C#’s performance.  
✅ **Structured and Scalable Development** – Use Visual Studio for **better organization, debugging, and maintainability**.  
✅ **No Need for Interprocess Communication** – Unlike subprocess-based solutions, Python.NET allows **direct** interaction with Python objects from C#.  
✅ **Optimized Data Sharing** – **Avoid unnecessary data transfers** when exchanging information between Python and C#.  
✅ **Supports Advanced Python Libraries** – Work with **scientific, AI, and CAD-related** Python libraries seamlessly.  

## How It Works  
This template **combines [Python.NET](https://github.com/pythonnet/pythonnet) with the [official Grasshopper development template](https://github.com/mcneel/RhinoVisualStudioExtensions/releases)**, allowing C# components to:  
1. **Load and execute Python scripts dynamically**, without spawning subprocesses.  
2. **Directly use Python libraries** within C# with **efficient memory and data sharing**.  
3. **Integrate Python-powered logic** into Grasshopper components with **minimal overhead**.  

## Installation  

### Prerequisites  
1. **Install Rhino** – Rhino 7 or Rhino 8 recommended.  
2. **Install Anaconda3** – Latest version from [Anaconda.com](https://www.anaconda.com/download). Anaconda is recommended over a basic [Python Installation](https://www.python.org/) to manage virtual environments and installed libraries easily. 
3. **Clone the Repository** – [GitHub Repo](https://github.com/JonasFeron/PythonNETGrasshopperTemplate).  
4. **Set Up in Visual Studio** – Open `PythonNETGrasshopperTemplate.sln` and launch Rhino 7 (or 8) in Debug mode.
5. **Develop Custom Grasshopper Components** – Follow the [Official Grasshopper Developer Guide](https://developer.rhino3d.com/guides/grasshopper/your-first-component-windows/) 

## Use Cases  
PythonNETGrasshopperTemplate is **ideal for**:  
- **Advanced Grasshopper Plugin Development** – Use **Python and C# together** for custom components.  
- **Scientific and Computational Applications** – Integrate Python libraries for **numerical computing, AI, and automation**.  
- **Rhino and CAD Automation** – Enhance Rhino workflows with **Python.NET and C# capabilities**.  

## License  
PythonNETGrasshopperTemplate is licensed under the **Apache 2.0 License**.  

📌 **GitHub Repository**: [PythonNETGrasshopperTemplate on GitHub](https://github.com/JonasFeron/PythonNETGrasshopperTemplate)  
