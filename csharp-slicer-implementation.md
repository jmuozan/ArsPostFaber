# C# Implementation: 3D Slicer for Clay Printing

This guide provides a practical C# implementation of a 3D slicer for clay printing, focusing on generating exterior surfaces and a bottom closure layer. We'll use geometry libraries and efficient algorithms to handle the 3D mesh processing.

## Required Libraries

For a robust C# implementation, we'll use these libraries:

```csharp
// NuGet packages to install:
// - Geometry3Sharp: For 3D mesh operations
// - ClipperLib: For polygon offsetting and boolean operations
// - netDxf: Optional, for DXF file import/export
```

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Numerics;
using g3;               // Geometry3Sharp
using ClipperLib;       // ClipperLib for polygon operations
```

## Project Structure

```
ClayPrintSlicer/
├── Models/
│   ├── Mesh.cs
│   ├── Layer.cs
│   ├── Contour.cs
│   ├── Toolpath.cs
│   └── PrintSettings.cs
├── Services/
│   ├── MeshImporter.cs
│   ├── SlicerService.cs
│   ├── ContourProcessor.cs
│   ├── ToolpathGenerator.cs
│   └── GCodeExporter.cs
└── Program.cs
```

## Core Data Models

### PrintSettings.cs

```csharp
public class PrintSettings
{
    // Layer settings
    public double LayerHeight { get; set; } = 0.5;  // mm
    
    // Extrusion settings
    public double NozzleDiameter { get; set; } = 1.2;  // mm
    public double ExtrusionWidth { get; set; } = 1.5;  // mm
    public double ExtrusionMultiplier { get; set; } = 1.0;
    
    // Shell settings
    public int NumberOfShells { get; set; } = 1;
    public bool CreateBottomLayer { get; set; } = true;
    public BottomFillType BottomFillPattern { get; set; } = BottomFillType.Concentric;
    public bool SpiralVaseMode { get; set; } = false;
    
    // Print parameters
    public double PrintSpeed { get; set; } = 25;  // mm/s
    public double RetractDistance { get; set; } = 1.0;  // mm
    public double RetractSpeed { get; set; } = 10;  // mm/s
    
    // Material settings for clay
    public double MaterialDensity { get; set; } = 1.9;  // g/cm³
    
    // Output settings
    public string OutputFilePath { get; set; } = "output.gcode";
}

public enum BottomFillType
{
    Concentric,
    Rectilinear,
    None
}
```

### Layer.cs and Contour.cs

```csharp
public class Layer
{
    public double Height { get; set; }
    public List<Contour> Contours { get; set; } = new List<Contour>();
    public bool IsBottomLayer => Height <= PrintSettings.LayerHeight;
}

public class Contour
{
    public List<IntPoint> Points { get; set; } = new List<IntPoint>();
    public bool IsOuter { get; set; } = true;
    public int ShellIndex { get; set; } = 0;  // 0 = outermost
    
    // Convert to/from ClipperLib format
    public static List<IntPoint> ToClipperPath(List<Vector2> points, double scale = 1000)
    {
        return points.Select(p => new IntPoint(
            (long)(p.X * scale), 
            (long)(p.Y * scale))).ToList();
    }
    
    public static List<Vector2> FromClipperPath(List<IntPoint> points, double scale = 1000)
    {
        return points.Select(p => new Vector2(
            (float)(p.X / scale), 
            (float)(p.Y / scale))).ToList();
    }
}
```

### Toolpath.cs

```csharp
public class Toolpath
{
    public List<ToolpathSegment> Segments { get; set; } = new List<ToolpathSegment>();
    public double LayerHeight { get; set; }
}

public class ToolpathSegment
{
    public Vector3 StartPoint { get; set; }
    public Vector3 EndPoint { get; set; }
    public double Extrusion { get; set; }
    public bool IsTravel { get; set; }
    public double FeedRate { get; set; }
    
    public double Length => Vector3.Distance(StartPoint, EndPoint);
}
```

## Mesh Processing

### MeshImporter.cs

```csharp
public class MeshImporter
{
    public DMesh3 ImportMesh(string filePath)
    {
        try
        {
            // Use Geometry3Sharp to import STL file
            var mesh = StandardMeshReader.ReadMesh(filePath);
            
            // Ensure the mesh is valid
            if (!mesh.IsClosed())
            {
                Console.WriteLine("Warning: Mesh is not closed. Results may be unexpected.");
            }
            
            // Orient and position the mesh on build plate
            MeshTransforms.RepositionMesh(mesh);
            
            return mesh;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error importing mesh: {ex.Message}");
            throw;
        }
    }
}

// Extension methods for mesh positioning
public static class MeshTransforms
{
    public static void RepositionMesh(DMesh3 mesh)
    {
        // Get bounding box
        AxisAlignedBox3d bounds = mesh.GetBounds();
        
        // Calculate translation to position mesh at (0,0,0)
        Vector3d translation = new Vector3d(
            -bounds.Min.x,
            -bounds.Min.y,
            -bounds.Min.z);
            
        // Apply the translation
        MeshTransforms.Translate(mesh, translation);
    }
}
```

## Core Slicing Algorithm

### SlicerService.cs

```csharp
public class SlicerService
{
    private readonly PrintSettings _settings;
    
    public SlicerService(PrintSettings settings)
    {
        _settings = settings;
    }
    
    public List<Layer> SliceMesh(DMesh3 mesh)
    {
        var layers = new List<Layer>();
        
        // Get mesh bounds
        AxisAlignedBox3d bounds = mesh.GetBounds();
        double minZ = bounds.Min.z;
        double maxZ = bounds.Max.z;
        
        // Calculate number of layers
        int numLayers = (int)Math.Ceiling((maxZ - minZ) / _settings.LayerHeight);
        
        // Create a mesh slicer instance
        MeshPlaneCutter slicer = new MeshPlaneCutter(mesh);
        
        // Slice each layer
        for (int i = 0; i < numLayers; i++)
        {
            double height = minZ + (i + 1) * _settings.LayerHeight;
            
            // Create slicing plane at current height
            Vector3d normal = Vector3d.AxisZ;
            Vector3d origin = new Vector3d(0, 0, height);
            Frame3f slicePlane = new Frame3f(origin, normal);
            
            // Execute the cut
            PlanarSlice slice = new PlanarSlice(mesh);
            bool success = slice.Compute(slicePlane.Origin, slicePlane.Z);
            
            if (success)
            {
                Layer layer = new Layer { Height = height };
                
                // Convert slice polylines to contours
                foreach (var polyLine in slice.Loops)
                {
                    // Convert to 2D vectors (dropping Z coordinate)
                    List<Vector2> points2D = polyLine.Select(
                        v => new Vector2((float)v.x, (float)v.y)).ToList();
                    
                    // Create contour (convert to Clipper format)
                    Contour contour = new Contour
                    {
                        Points = Contour.ToClipperPath(points2D),
                        IsOuter = true, // We'll determine this later
                        ShellIndex = 0  // Outermost shell
                    };
                    
                    layer.Contours.Add(contour);
                }
                
                layers.Add(layer);
            }
        }
        
        return layers;
    }
}
```

## Contour Processing

### ContourProcessor.cs

```csharp
public class ContourProcessor
{
    private readonly PrintSettings _settings;
    private readonly ClipperOffset _clipper;
    private const double CLIPPER_SCALE = 1000.0; // Scale factor for integer conversion
    
    public ContourProcessor(PrintSettings settings)
    {
        _settings = settings;
        _clipper = new ClipperOffset();
    }
    
    public List<Layer> ProcessLayers(List<Layer> layers)
    {
        List<Layer> processedLayers = new List<Layer>();
        
        foreach (var layer in layers)
        {
            Layer processedLayer = new Layer { Height = layer.Height };
            
            // For each contour in the layer
            foreach (var contour in layer.Contours)
            {
                List<List<IntPoint>> offsetContours = new List<List<IntPoint>>();
                
                // Process outer shell
                // Offset inward by half extrusion width to align extrusion edge with model boundary
                double initialOffset = -(_settings.ExtrusionWidth / 2.0) * CLIPPER_SCALE;
                
                _clipper.Clear();
                _clipper.AddPath(contour.Points, JoinType.jtRound, EndType.etClosedPolygon);
                List<List<IntPoint>> solution = new List<List<IntPoint>>();
                _clipper.Execute(ref solution, initialOffset);
                
                // Add the first offset to our collection
                foreach (var path in solution)
                {
                    if (path.Count > 0)
                    {
                        Contour offsetContour = new Contour
                        {
                            Points = path,
                            IsOuter = true,
                            ShellIndex = 0
                        };
                        processedLayer.Contours.Add(offsetContour);
                        offsetContours.Add(path);
                    }
                }
                
                // Generate additional shells if required
                double shellOffset = -_settings.ExtrusionWidth * CLIPPER_SCALE;
                List<List<IntPoint>> currentShellPaths = new List<List<IntPoint>>(solution);
                
                for (int shellIndex = 1; shellIndex < _settings.NumberOfShells; shellIndex++)
                {
                    _clipper.Clear();
                    foreach (var path in currentShellPaths)
                    {
                        if (path.Count > 0)
                        {
                            _clipper.AddPath(path, JoinType.jtRound, EndType.etClosedPolygon);
                        }
                    }
                    
                    List<List<IntPoint>> nextShellPaths = new List<List<IntPoint>>();
                    _clipper.Execute(ref nextShellPaths, shellOffset);
                    
                    // Add these new shells
                    foreach (var path in nextShellPaths)
                    {
                        if (path.Count > 0)
                        {
                            Contour innerShell = new Contour
                            {
                                Points = path,
                                IsOuter = false,
                                ShellIndex = shellIndex
                            };
                            processedLayer.Contours.Add(innerShell);
                        }
                    }
                    
                    // Update for next iteration
                    currentShellPaths = nextShellPaths;
                    
                    // If no more valid shells, break
                    if (currentShellPaths.Count == 0)
                        break;
                }
            }
            
            // Special processing for bottom layer
            if (layer.IsBottomLayer && _settings.CreateBottomLayer)
            {
                processedLayer = CreateBottomFill(processedLayer);
            }
            
            processedLayers.Add(processedLayer);
        }
        
        return processedLayers;
    }
    
    private Layer CreateBottomFill(Layer bottomLayer)
    {
        // Find outermost contours (those with ShellIndex = 0)
        var outerContours = bottomLayer.Contours
            .Where(c => c.ShellIndex == 0)
            .ToList();
            
        switch (_settings.BottomFillPattern)
        {
            case BottomFillType.Concentric:
                return CreateConcentricFill(bottomLayer, outerContours);
                
            case BottomFillType.Rectilinear:
                return CreateRectilinearFill(bottomLayer, outerContours);
                
            default:
                return bottomLayer;
        }
    }
    
    private Layer CreateConcentricFill(Layer layer, List<Contour> outerContours)
    {
        // For each outer contour
        foreach (var outerContour in outerContours)
        {
            List<List<IntPoint>> currentPaths = new List<List<IntPoint>> { outerContour.Points };
            double fillOffset = -_settings.ExtrusionWidth * CLIPPER_SCALE;
            int fillIndex = _settings.NumberOfShells; // Start index after shells
            
            while (currentPaths.Count > 0)
            {
                _clipper.Clear();
                foreach (var path in currentPaths)
                {
                    _clipper.AddPath(path, JoinType.jtRound, EndType.etClosedPolygon);
                }
                
                List<List<IntPoint>> nextPaths = new List<List<IntPoint>>();
                _clipper.Execute(ref nextPaths, fillOffset);
                
                // Add valid fill paths
                foreach (var path in nextPaths)
                {
                    if (path.Count > 0)
                    {
                        // Calculate approximate area to check if contour is too small
                        double area = Clipper.Area(path) / (CLIPPER_SCALE * CLIPPER_SCALE);
                        if (Math.Abs(area) < _settings.ExtrusionWidth * _settings.ExtrusionWidth)
                            continue;
                            
                        Contour fillContour = new Contour
                        {
                            Points = path,
                            IsOuter = false,
                            ShellIndex = fillIndex
                        };
                        layer.Contours.Add(fillContour);
                    }
                }
                
                currentPaths = nextPaths;
                fillIndex++;
                
                // Break if we've gone too far (safety)
                if (fillIndex > 100) break;
            }
        }
        
        return layer;
    }
    
    private Layer CreateRectilinearFill(Layer layer, List<Contour> outerContours)
    {
        // For rectilinear fill, we need to:
        // 1. Find bounding box of all outer contours
        // 2. Generate grid lines
        // 3. Clip lines to the outer contours using Clipper
        
        // Find bounds
        long minX = long.MaxValue, minY = long.MaxValue;
        long maxX = long.MinValue, maxY = long.MinValue;
        
        foreach (var contour in outerContours)
        {
            foreach (var point in contour.Points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }
        
        // Create subject paths (outer contours) for clipping
        List<List<IntPoint>> subjectPaths = outerContours.Select(c => c.Points).ToList();
        
        // Create line pattern
        double lineSpacing = _settings.ExtrusionWidth * CLIPPER_SCALE;
        Clipper clipper = new Clipper();
        
        // Generate horizontal lines
        for (long y = minY; y <= maxY; y += (long)lineSpacing)
        {
            var line = new List<IntPoint>
            {
                new IntPoint(minX - lineSpacing, y),
                new IntPoint(maxX + lineSpacing, y)
            };
            
            // Clip line to subject paths
            clipper.Clear();
            clipper.AddPath(line, PolyType.ptSubject, false);
            foreach (var subject in subjectPaths)
            {
                clipper.AddPath(subject, PolyType.ptClip, true);
            }
            
            // Get intersection
            PolyTree solution = new PolyTree();
            clipper.Execute(ClipType.ctIntersection, solution, PolyFillType.pftNonZero);
            
            // Convert to paths and add to layer
            var clippedLines = Clipper.OpenPathsFromPolyTree(solution);
            
            foreach (var clippedLine in clippedLines)
            {
                if (clippedLine.Count > 1)
                {
                    Contour fillLine = new Contour
                    {
                        Points = clippedLine,
                        IsOuter = false,
                        ShellIndex = _settings.NumberOfShells // Fill index
                    };
                    layer.Contours.Add(fillLine);
                }
            }
        }
        
        return layer;
    }
}
```

## Toolpath Generation

### ToolpathGenerator.cs

```csharp
public class ToolpathGenerator
{
    private readonly PrintSettings _settings;
    private const double CLIPPER_SCALE = 1000.0;
    
    public ToolpathGenerator(PrintSettings settings)
    {
        _settings = settings;
    }
    
    public List<Toolpath> GenerateToolpaths(List<Layer> layers)
    {
        List<Toolpath> toolpaths = new List<Toolpath>();
        
        foreach (var layer in layers)
        {
            Toolpath toolpath = new Toolpath { LayerHeight = layer.Height };
            
            // Group contours by shell index
            var contoursByShell = layer.Contours
                .GroupBy(c => c.ShellIndex)
                .OrderBy(g => g.Key) // Process outer shells first
                .ToDictionary(g => g.Key, g => g.ToList());
                
            // Process each shell group
            foreach (var shellGroup in contoursByShell)
            {
                var shellContours = shellGroup.Value;
                
                // Handle closed contours (most shells)
                foreach (var contour in shellContours)
                {
                    if (contour.Points.Count < 3)
                        continue;
                        
                    // Add travel move to first point if needed
                    if (toolpath.Segments.Count == 0)
                    {
                        // First segment ever - add travel
                        AddTravelMove(toolpath, contour.Points[0], layer.Height);
                    }
                    else
                    {
                        // Connect from last point with travel
                        var lastPoint = toolpath.Segments.Last().EndPoint;
                        var nextPoint = new Vector3(
                            (float)(contour.Points[0].X / CLIPPER_SCALE),
                            (float)(contour.Points[0].Y / CLIPPER_SCALE),
                            (float)layer.Height);
                            
                        if (Vector3.Distance(lastPoint, nextPoint) > 0.1)
                        {
                            AddTravelMove(toolpath, contour.Points[0], layer.Height);
                        }
                    }
                    
                    // Add extrusion moves along contour
                    for (int i = 0; i < contour.Points.Count; i++)
                    {
                        int nextIdx = (i + 1) % contour.Points.Count; // Loop back to start
                        AddExtrusionMove(toolpath, contour.Points[i], contour.Points[nextIdx], layer.Height);
                    }
                }
            }
            
            // Special handling for bottom layer fill lines (non-closed paths)
            if (layer.IsBottomLayer && _settings.BottomFillPattern == BottomFillType.Rectilinear)
            {
                // Get fill lines (usually higher shell indices)
                var fillLines = layer.Contours
                    .Where(c => c.ShellIndex >= _settings.NumberOfShells)
                    .ToList();
                    
                foreach (var line in fillLines)
                {
                    if (line.Points.Count < 2)
                        continue;
                        
                    // Travel to start of line
                    AddTravelMove(toolpath, line.Points[0], layer.Height);
                    
                    // Extrude along line
                    for (int i = 0; i < line.Points.Count - 1; i++)
                    {
                        AddExtrusionMove(toolpath, line.Points[i], line.Points[i + 1], layer.Height);
                    }
                }
            }
            
            toolpaths.Add(toolpath);
        }
        
        return toolpaths;
    }
    
    private void AddTravelMove(Toolpath toolpath, IntPoint point, double height)
    {
        Vector3 position = new Vector3(
            (float)(point.X / CLIPPER_SCALE),
            (float)(point.Y / CLIPPER_SCALE),
            (float)height);
            
        // If this is the first move, use current position as start
        Vector3 startPoint = toolpath.Segments.Count > 0 
            ? toolpath.Segments.Last().EndPoint 
            : position;
            
        ToolpathSegment segment = new ToolpathSegment
        {
            StartPoint = startPoint,
            EndPoint = position,
            IsTravel = true,
            Extrusion = 0,
            FeedRate = _settings.PrintSpeed * 60 // mm/min
        };
        
        toolpath.Segments.Add(segment);
    }
    
    private void AddExtrusionMove(Toolpath toolpath, IntPoint start, IntPoint end, double height)
    {
        Vector3 startPos = new Vector3(
            (float)(start.X / CLIPPER_SCALE),
            (float)(start.Y / CLIPPER_SCALE),
            (float)height);
            
        Vector3 endPos = new Vector3(
            (float)(end.X / CLIPPER_SCALE),
            (float)(end.Y / CLIPPER_SCALE),
            (float)height);
            
        // Calculate distance
        double distance = Vector3.Distance(startPos, endPos);
        
        // Calculate extrusion amount
        // Volume = length * width * height
        double volume = distance * _settings.ExtrusionWidth * _settings.LayerHeight;
        double extrusion = volume * _settings.ExtrusionMultiplier;
        
        ToolpathSegment segment = new ToolpathSegment
        {
            StartPoint = startPos,
            EndPoint = endPos,
            IsTravel = false,
            Extrusion = extrusion,
            FeedRate = _settings.PrintSpeed * 60 // mm/min
        };
        
        toolpath.Segments.Add(segment);
    }
}
```

## G-Code Export

### GCodeExporter.cs

```csharp
public class GCodeExporter
{
    private readonly PrintSettings _settings;
    
    public GCodeExporter(PrintSettings settings)
    {
        _settings = settings;
    }
    
    public void ExportGCode(List<Toolpath> toolpaths, string outputPath)
    {
        using (StreamWriter writer = new StreamWriter(outputPath))
        {
            // Write header
            writer.WriteLine("; Clay Printing GCode");
            writer.WriteLine("; Generated: " + DateTime.Now);
            writer.WriteLine("; Settings:");
            writer.WriteLine("; Layer Height: " + _settings.LayerHeight);
            writer.WriteLine("; Extrusion Width: " + _settings.ExtrusionWidth);
            writer.WriteLine("; Print Speed: " + _settings.PrintSpeed);
            writer.WriteLine("");
            
            // Initial setup
            writer.WriteLine("G90 ; Absolute positioning");
            writer.WriteLine("G21 ; Millimeters");
            writer.WriteLine("G92 E0 ; Reset extruder position");
            writer.WriteLine("M83 ; Relative extrusion");
            writer.WriteLine("");
            
            // Process each toolpath
            double currentZ = 0;
            double currentFeedRate = 0;
            
            foreach (var toolpath in toolpaths)
            {
                writer.WriteLine($"; Layer at height {toolpath.LayerHeight}mm");
                
                foreach (var segment in toolpath.Segments)
                {
                    // Handle Z change
                    if (Math.Abs(segment.EndPoint.Z - currentZ) > 0.001)
                    {
                        writer.WriteLine($"G1 Z{segment.EndPoint.Z:F3} F{_settings.PrintSpeed * 60}");
                        currentZ = segment.EndPoint.Z;
                    }
                    
                    // Handle feedrate change
                    if (Math.Abs(segment.FeedRate - currentFeedRate) > 0.001)
                    {
                        currentFeedRate = segment.FeedRate;
                    }
                    
                    // Write move
                    if (segment.IsTravel)
                    {
                        // Travel move - no extrusion
                        writer.WriteLine($"G0 X{segment.EndPoint.X:F3} Y{segment.EndPoint.Y:F3} F{currentFeedRate:F0}");
                    }
                    else
                    {
                        // Extrusion move
                        writer.WriteLine($"G1 X{segment.EndPoint.X:F3} Y{segment.EndPoint.Y:F3} E{segment.Extrusion:F4} F{currentFeedRate:F0}");
                    }
                }
                
                writer.WriteLine("");
            }
            
            // End GCode
            writer.WriteLine("G1 E-" + _settings.RetractDistance + " F" + (_settings.RetractSpeed * 60)); // Final retract
            writer.WriteLine("G0 Z" + (currentZ + 10)); // Raise nozzle
            writer.WriteLine("G0 X0 Y0 F3000"); // Return to origin
            writer.WriteLine("M84"); // Disable motors
            
            writer.Flush();
        }
    }
}
```

## Main Program

### Program.cs

```csharp
class Program
{
    static void Main(string[] args)
    {
        // Parse command line arguments
        bool spiralMode = false;
        
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].ToLower() == "--spiral" || args[i].ToLower() == "-s")
            {
                spiralMode = true;
            }
        }
        try
        {
            Console.WriteLine("Clay Printing Slicer");
            
            // Get input file
            string inputFile = args.Length > 0 ? args[0] : "model.stl";
            
            // Configure settings
            PrintSettings settings = new PrintSettings
            {
                LayerHeight = 0.5,
                NozzleDiameter = 1.0,
                ExtrusionWidth = 1.5,
                NumberOfShells = 1,
                CreateBottomLayer = true,
                BottomFillPattern = BottomFillType.Concentric,
                PrintSpeed = 25,
                OutputFilePath = "output.gcode"
            };
            
            // Create processing pipeline
            MeshImporter importer = new MeshImporter();
            SlicerService slicer = new SlicerService(settings);
            ContourProcessor contourProcessor = new ContourProcessor(settings);
            ToolpathGenerator toolpathGenerator = new ToolpathGenerator(settings);
            GCodeExporter exporter = new GCodeExporter(settings);
            
            // Execute pipeline
            Console.WriteLine("Importing mesh...");
            var mesh = importer.ImportMesh(inputFile);
            
            Console.WriteLine("Slicing mesh...");
            var layers = slicer.SliceMesh(mesh);
            
            Console.WriteLine("Processing contours...");
            var processedLayers = contourProcessor.ProcessLayers(layers);
            
            Console.WriteLine("Generating toolpaths...");
            var toolpaths = toolpathGenerator.GenerateToolpaths(processedLayers);
            
            Console.WriteLine("Exporting G-Code...");
            exporter.ExportGCode(toolpaths, settings.OutputFilePath);
            
            Console.WriteLine($"Done! G-Code written to {settings.OutputFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
```

## Advanced Features

### Spiral Vase Mode Implementation

For continuous extrusion in clay printing, you can implement a spiral vase mode:

```csharp
public List<Toolpath> GenerateSpiralToolpath(List<Layer> layers)
{
    List<Toolpath> toolpaths = new List<Toolpath>();
    Toolpath spiralToolpath = new Toolpath { LayerHeight = layers[0].Height };
    
    // Get outermost contours of each layer
    var outerContours = layers.Select(layer => 
        layer.Contours.FirstOrDefault(c => c.ShellIndex == 0)).ToList();
    
    // Add first layer normally
    var firstLayerToolpath = GenerateToolpaths(new List<Layer> { layers[0] }).First();
    spiralToolpath.Segments.AddRange(firstLayerToolpath.Segments);
    
    // Create spiral for remaining layers
    for (int i = 1; i < layers.Count; i++)
    {
        var currentLayer = layers[i];
        var previousLayer = layers[i - 1];
        var currentContour = outerContours[i];
        var previousContour = outerContours[i - 1];
        
        if (currentContour == null || previousContour == null)
            continue;
            
        // Interpolate between layers
        for (int j = 0; j < currentContour.Points.Count; j++)
        {
            int prevIdx = j % previousContour.Points.Count;
            int nextIdx = (j + 1) % currentContour.Points.Count;
            
            // Current point
            Vector3 startPoint = new Vector3(
                (float)(currentContour.Points[j].X / CLIPPER_SCALE),
                (float)(currentContour.Points[j].Y / CLIPPER_SCALE),
                (float)currentLayer.Height);
                
            // Next point on same layer
            Vector3 endPoint = new Vector3(
                (float)(currentContour.Points[nextIdx].X / CLIPPER_SCALE),
                (float)(currentContour.Points[nextIdx].Y / CLIPPER_SCALE),
                (float)currentLayer.Height);
                
            // Calculate extrusion
            double distance = Vector3.Distance(startPoint, endPoint);
            double volume = distance * _settings.ExtrusionWidth * _settings.LayerHeight;
            double extrusion = volume * _settings.ExtrusionMultiplier;
            
            // Add segment
            ToolpathSegment segment = new ToolpathSegment
            {
                StartPoint = startPoint,
                EndPoint = endPoint,
                IsTravel = false,
                Extrusion = extrusion,
                FeedRate = _settings.PrintSpeed * 60
            };
            
            spiralToolpath.Segments.Add(segment);
        }
    }
    
    toolpaths.Add(spiralToolpath);
    return toolpaths;
}