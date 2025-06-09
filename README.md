<!-- Template from: https://github.com/othneildrew/Best-README-Template -->
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
  <a href="https://github.com/github_username/repo_name">
    <img src="docs/images/parasite.png" alt="Logo" width="100%">
  </a>

<h3 align="center">ArsPostFaber</h3>

  <p align="center">
    Art Post Artisan
    <br />
    <a href="https://github.com/github_username/repo_name"><strong>Explore the docs »</strong></a>
    <br />
    <br />
    <a href="https://github.com/github_username/repo_name">View Demo</a>
    ·
    <a href="https://github.com/github_username/repo_name/issues">Report Bug</a>
    ·
    <a href="https://github.com/github_username/repo_name/issues">Request Feature</a>
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
    <li><a href="#usage">Usage</a></li>
    <li><a href="#roadmap">Roadmap</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#license">License</a></li>
    <li><a href="#contact">Contact</a></li>
    <li><a href="#acknowledgments">Acknowledgments</a></li>
  </ol>
</details>



<!-- ABOUT THE PROJECT -->
## About The Project
crft is a Rhino Grasshopper plugin that provides experimental components for bridging software, hardware, and human interaction in 3D printing workflows.

Key features:
- **Atoms to Bits**: Mesh digitization workflows converting physical objects into digital representations.
- **Mesh Editing with Hand Detection**: Real-time mesh manipulation using hand tracking and AI.
- **Machine Live Control**: Interactive control of devices, including 3D printers.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

### Built With
- .NET 7.0
- Rhino 8 SDK (Grasshopper 8)
- C#, Grasshopper SDK
- SerialPortStream 2.4.2 (RJCP)
- System.IO.Ports

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Getting Started
Follow these steps to set up the plugin locally.

### Prerequisites
- Rhino 8 + Grasshopper
- .NET 7.0 SDK
- USB drivers for serial communication (ensure permissions on macOS/Linux)

### Installation
1. Clone the repository:
   ```sh
   git clone https://github.com/github_username/crft.git
   ```
2. Open `crft.sln` in your IDE.
3. Build the `crft` project in Release mode.
4. Copy `crft.gha` from:
   - Windows: `bin\\Release\\net7.0-windows\\crft.gha`
   - macOS: `bin/Release/net7.0/osx-arm64/crft.gha`
5. Paste `crft.gha` into your Grasshopper components folder and restart Rhino.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Usage
### G-code Generation


### Serial Communication


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

Project Link: [https://github.com/github_username/repo_name](https://github.com/jmuozan/ArsPostFaber)

<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- ACKNOWLEDGMENTS -->
## Acknowledgments

* [t43](https://github.com/LingDong-/t43?tab=readme-ov-file)
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





## Overview
crft is a set of Grasshopper components for Rhino, forming a plugin that explores human-software-machine interaction (HMI). The components are designed as an experimental toolkit for bridging the physical and digital worlds, focusing on:

- **Atoms to Bits**: Mesh digitization workflows, enabling the conversion of physical objects into digital mesh representations.
- **Mesh Editing with Hand Detection**: Real-time mesh manipulation using hand tracking and gesture recognition, leveraging computer vision and AI.
- **Machine Live Control**: Components for live, interactive control of digital and physical systems, exploring new paradigms of HMI.

## Features
- Real-time camera and video capture integration.
- Hand tracking and gesture-based mesh editing.
- Mesh digitization and manipulation tools.
- Experimental interfaces for live machine control.

## Repository Structure


## Build Instructions
To build the plugin, run:

```bash
dotnet build -clp:NoSummary crft.csproj
```

Or use the VS Code task labeled `build`.

## Usage
- Run `download_models.sh` to fetch required models.
- Load the compiled plugin (`crft.gha`) into Grasshopper for Rhino.
- Explore the provided Grasshopper components for mesh digitization, hand-based mesh editing, and live control experiments.

## License
See the `LICENSE` file for details.



## ToDo
- [ ] photogrammetry make it work
- [ ] box break for photogrammetry
- [ ] gcode code from gh file
- [ ] Sculpt module
- [ ] llm vision
- [x] llm chat
- [x] llm create your own components???
- [x] Program simulation