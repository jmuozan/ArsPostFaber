<!-- Template from: https://github.com/othneildrew/Best-README-Template -->
<!--/Users/jorgemuyo/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Grasshopper (b45a29b1-4343-4035-989e-044e8580d9cf)/Libraries-->
<a name="readme-top"></a>

[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![MIT License][license-shield]][license-url]
[![LinkedIn][linkedin-shield]][linkedin-url]

<!-- HEADER -->
<br />
<div align="center">
  <a href="https://github.com/jmuozan/ArsPostFaber">
    <img src="docs/images/parasite.png" alt="Logo" width="100%">
  </a>

<h3 align="center">ArsPostFaber</h3>

  <p align="center">
    Art Post Artisan
    <br />
    <a href="https://jmuozan.github.io/ArsPostFaber-docs/"><strong>Explore the docs »</strong></a>
    <br />
    <br />
    <a href="https://github.com/github_username/ArsPostFaber">View Demo</a>
    ·
    <a href="https://github.com/github_username/ArsPostFaber/issues">Report Bug</a>
    ·
    <a href="https://github.com/github_username/ArsPostFaber/issues">Request Feature</a>
  </p>
</div>



<!-- TABLE OF CONTENTS -->
<details>
  <summary>Table of Contents</summary>
  <ol>
    <li>
      <a href="#about-the-project">About The Project</a>
      <ul>
        <li><a href="#built-with">Built With</a></li>
      </ul>
    </li>
    <li>
      <a href="#getting-started">Getting Started</a>
      <ul>
        <li><a href="#prerequisites">Prerequisites</a></li>
        <li><a href="#installation">Installation</a></li>
      </ul>
    </li>
    <li><a href="#components-overview">Components Overview</a></li>
    <li><a href="#roadmap">Roadmap</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#license">License</a></li>
    <li><a href="#contact">Contact</a></li>
    <li><a href="#acknowledgments">Acknowledgments</a></li>
  </ol>
</details>



<!-- ABOUT THE PROJECT -->
## About The Project

ArsPostFaber is a comprehensive digital fabrication toolkit for Grasshopper that bridges the gap between parametric design and physical production. This plugin provides five major categories of tools for modern digital fabrication workflows:

🤖 **AI-Powered Component Generation** - Create custom Grasshopper components using natural language descriptions
🏭 **Advanced 3D Printing Pipeline** - Complete toolchain from geometry to G-code with intelligent slicing
📡 **Professional Serial Communication** - Enterprise-grade 3D printer control and monitoring
🔧 **Interactive Mesh Processing** - Real-time mesh editing and geometric operations  
📱 **Mobile Photogrammetry** - 3D reconstruction from smartphone captures

<p align="right">(<a href="#readme-top">back to top</a>)</p>

### Built With

* [![C#][CSharp-shield]][CSharp-url]
* [![.NET][DotNet-shield]][DotNet-url]
* [![Grasshopper][GH-shield]][GH-url]
* [![Rhino][Rhino-shield]][Rhino-url]

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Getting Started




<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Components Overview

### 🤖 AI-Powered Component Generation

#### Component that Makes (API)
Generate custom Grasshopper components using OpenAI's GPT models. Simply describe what you want in natural language and get a fully compiled component.
- **Inputs**: Description, API key, model selection, component name
- **Outputs**: Generated code, compiled component, status messages
- **Key Features**: Agent mode with automatic error correction, PDF context support

#### Component that Makes (Local)  
Same functionality as the API version but uses local Ollama models for offline operation.
- **Inputs**: Description, local model selection, generation parameters
- **Outputs**: Generated code, compiled component, status messages
- **Key Features**: Offline operation, infinite retry until success

### 🏭 3D Printing Pipeline

#### Slicer Settings
Configure all parameters for the 3D printing pipeline including layer height, speeds, and printer dimensions.

#### Slice Geometry
Convert 3D Brep geometry into horizontal layer curves ready for processing.

#### Shell Geometry  
Generate perimeter shells and identify inner regions for infill from sliced curves.

#### Infill Geometry
Create infill toolpaths within shell regions using configurable patterns and spacing.

#### G-Code Generator
Convert processed curves into production-ready G-code with advanced motion planning and arc interpolation.

### 📡 Serial Communication

#### Serial Control
Professional-grade serial communication component for streaming G-code to 3D printers with real-time control and monitoring.
- **Features**: Play/pause controls, toolpath visualization, cross-platform compatibility
- **Inputs**: Port settings, G-code commands, printer configuration
- **Outputs**: Status updates, response messages, modified toolpaths

### 🔧 Mesh Processing

#### Mesh Edit
Interactive mesh editing with vertex selection and real-time modification capabilities.

#### Mesh Crop  
Crop meshes to specified bounding boxes with scaling and offset controls.

### 📱 Photogrammetry

#### Photogrammetry
Complete photogrammetry pipeline using mobile device capture and RealityKit reconstruction.
- **Features**: Built-in web server, QR code connection, automatic processing
- **Inputs**: Quality settings, processing parameters
- **Outputs**: Server URL, reconstructed 3D mesh

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Roadmap


<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- CONTRIBUTING -->
## Contributing

Contributions are what make the open source community such an amazing place to learn, inspire, and create. Any contributions you make are **greatly appreciated**.

If you have a suggestion that would make this better, please fork the repo and create a pull request. You can also simply open an issue with the tag "enhancement".
Don't forget to give the project a star! Thanks again!

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- LICENSE -->
## License

Distributed under the MIT License. See `LICENSE` for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- CONTACT -->
## Contact

jmuozan - [@jorgemunyozz](https://twitter.com/jorgemunyozz) - jmuozan@gmail.com

Project Link: [here](https://jmuozan.github.io/ArsPostFaber-docs/)

<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- ACKNOWLEDGMENTS -->
## Acknowledgments

Amazing projects that have inspired and helped the developement of this project

* [t43 slicer](https://github.com/LingDong-/t43?tab=readme-ov-file)
* [Robots](https://github.com/visose/Robots)
* [Vespidae](https://github.com/frikkfossdal/Vespidae)
* [Python gh Template](https://github.com/JonasFeron/PythonNETGrasshopperTemplate)
* [Advanced Developement in grasshopper](https://www.youtube.com/watch?v=Em_teGSpP9w&list=PLx3k0RGeXZ_yZgg-f2k7fO3WxBQ0zLCeU)
* [Rhino developer macos gide](https://developer.rhino3d.com/guides/rhinocommon/your-first-plugin-mac/)
* [Advenced 3D Printing in grasshopper](https://www.amazon.com/Advanced-3D-Printing-Grasshopper%C2%AE-Clay/dp/B086Y7CLLC)
* [Open3D](https://www.open3d.org/html/introduction.html)
* [Mediapipe](https://chuoling.github.io/mediapipe/solutions/holistic.html)
* [Brain plugin grasshopper](https://github.com/ParametricCamp/brain-plugin-grasshopper)


<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- MARKDOWN LINKS & IMAGES -->
<!-- https://www.markdownguide.org/basic-syntax/#reference-style-links -->
[contributors-shield]: https://img.shields.io/github/contributors/jmuozan/ArsPostFaber.svg?style=for-the-badge
[contributors-url]: https://github.com/jmuozan/ArsPostFaber/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/jmuozan/ArsPostFaber.svg?style=for-the-badge
[forks-url]: https://github.com/jmuozan/ArsPostFaber/network/members
[stars-shield]: https://img.shields.io/github/stars/jmuozan/ArsPostFaber.svg?style=for-the-badge
[stars-url]: https://github.com/jmuozan/ArsPostFaber/stargazers
[issues-shield]: https://img.shields.io/github/issues/jmuozan/ArsPostFaber.svg?style=for-the-badge
[issues-url]: https://github.com/jmuozan/ArsPostFaber/issues
[license-shield]: https://img.shields.io/github/license/jmuozan/ArsPostFaber.svg?style=for-the-badge
[license-url]: https://github.com/jmuozan/ArsPostFaber/blob/master/LICENSE
[linkedin-shield]: https://img.shields.io/badge/-LinkedIn-black.svg?style=for-the-badge&logo=linkedin&colorB=555
[linkedin-url]: https://www.linkedin.com/in/jorgemunozzanon/
[product-screenshot]: images/screenshot.png
[CSharp-shield]: https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white
[CSharp-url]: https://docs.microsoft.com/en-us/dotnet/csharp/
[DotNet-shield]: https://img.shields.io/badge/.NET-5C2D91?style=for-the-badge&logo=.net&logoColor=white
[DotNet-url]: https://dotnet.microsoft.com/
[GH-shield]: https://img.shields.io/badge/Grasshopper-8BC34A?style=for-the-badge&logo=grasshopper&logoColor=white
[GH-url]: https://www.grasshopper3d.com/
[Rhino-shield]: https://img.shields.io/badge/Rhino3D-FF6B6B?style=for-the-badge&logo=rhinoceros&logoColor=white  
[Rhino-url]: https://www.rhino3d.com/




## Repository Structure


## Build Instructions


## Debugging / Development




## ToDo
- [ ] gcode
- [ ] gcode plane
- [ ] webapp for drawings rhinocommon