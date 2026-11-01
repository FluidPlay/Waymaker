# Waymaker
Path creation tool for the S&box game engine. It's much simpler than 'RoadTool' to set up and use, and tailored for paths with feathered edges at its ends.

<img width="512" height="512" alt="waymaker-logo" src="https://github.com/user-attachments/assets/d7b695b5-e2b4-482d-acfd-07c0f5b1db6b" />

<img width="1289" height="793" alt="waymaker_sshot" src="https://github.com/user-attachments/assets/a54bdbfe-06e2-41b8-9ac8-0abcab503a8b" />

## Quick Start Guide

https://youtu.be/FTthPa83BJw

## Installation

Copy these folders and files into the target repository's root, preserving their paths:

```text
<repo root>/
├── Code/Waymaker/
│   ├── README.md
│   ├── Waymaker.cs
│   ├── WaymakerMeshBuilder.cs
│   ├── WaymakerMeshUtility.cs
│   ├── WaymakerAssemblyInfo.cs
│   └── waymaker-implementation-plan.md
├── Editor/Waymaker/
│   ├── WaymakerSplineEditorTool.cs
│   └── WaymakerSplineEditorWindow.cs
├── Assets/Waymaker/
│   ├── Materials/
│   │   ├── desertpath_1024_alpha.vmat
│   │   ├── m_desertpath_botcap.vmat
│   │   └── m_desertpath_topcap.vmat
│   └── Shaders/
│       └── waymaker_complex.shader
├── Assets/2D/Textures/Scenery/
│   ├── DesertPath_1024.png
│   ├── DesertPath_BotCap.png
│   ├── DesertPath_TopCap.png
│   └── DesertPath_mask.png                         # Optional translucency mask
└── UnitTests/
    └── WaymakerMeshBuilderTests.cs                 # Optional MSTest coverage
```

The game and editor assemblies must include their respective Code and Editor folders. The optional tests require an MSTest assembly configured to include the Waymaker code; `WaymakerAssemblyInfo.cs` grants that assembly access to internals.

The three materials use the path and cap textures shown above. Keep their asset paths or re-point the materials' texture slots. The mask is optional and is not referenced by the current materials. Do not copy compiled assets (`*.vmat_c`, `*.shader_c`, `*.meta`, or `*.generated.vtex_c`); the s&box editor generates them on import.

See `Code/Waymaker/README.md` for setup and usage details.

## FAQ

Q. I can't find how to adjust the tangent handles for the first control point

A. That's a side effect of S&box native spline control, which Waymaker uses. Just select the second control point (or last, if you only got two) and use the back arrow in the spline panel to select the first control point, instead of the Waymaker root. Then you'll be able to select and tweak the first control point out handle.
