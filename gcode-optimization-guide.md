# GCodeGeneratorComponent Path Optimization Implementation Guide

This guide explains how to enhance your existing GCodeGeneratorComponent with advanced path planning features similar to those found in t43 slicer. These improvements will optimize tool paths, minimize non-productive movements, and improve print quality.

## Table of Contents
1. [Current Status Analysis](#current-status-analysis)
2. [New Features Overview](#new-features-overview)
3. [Implementation Details](#implementation-details)
   - [Contour Classification](#1-add-contour-classification)
   - [Contour Segmentation](#2-implement-contour-segmentation)
   - [A* Pathfinding](#3-add-a-pathfinding-between-contours)
   - [Path Generation](#4-implement-optimized-path-generation)
   - [SolveInstance Method](#5-update-your-solveinstance-method)
   - [Additional Options](#6-add-additional-options)
4. [Implementation Suggestions](#implementation-suggestions)
5. [User Documentation](#user-documentation)

## Current Status Analysis

Your component already has basic slicing functionality:
1. It slices geometry (Brep or Mesh) with planes at specified heights
2. It creates contour curves at each layer
3. It generates basic infill for bottom layers using horizontal scan lines
4. It outputs G-code with simple point-to-point movement

However, there are several limitations:
1. No path optimization between contours
2. No consideration of contour direction (internal vs external)
3. No intelligent path planning for minimizing non-productive movements
4. Lacks seamless transitions between different parts of the model

## New Features Overview

We'll add the following features:
- Contour classification (holes vs outer perimeters)
- Region segmentation for complex models
- A* pathfinding for optimal travel between contours
- Path optimization to minimize non-productive movements
- Intelligent contour ordering and orientation
- Advanced infill patterns
- Retraction control for better print quality

## Implementation Details

### 1. Add Contour Classification

```

## Conclusion

This implementation significantly improves the GCodeGeneratorComponent by adding advanced path planning features similar to those found in professional slicers like t43. The key innovations include:

1. **Intelligent contour classification** that distinguishes between outer boundaries and inner holes
2. **Path optimization via A* algorithm** to minimize non-productive travel moves
3. **Multiple infill patterns** to suit different model requirements
4. **Efficient path ordering** to reduce print time and improve quality
5. **Retraction control** for cleaner prints with less stringing

These enhancements will result in faster print times, improved print quality, and more efficient material usage.
csharp
// Add this class to identify and store contour information
private class ContourInfo
{
    public Curve Contour { get; set; }
    public Polyline PolyContour { get; set; }
    public bool IsHole { get; set; }
    public Point3d Centroid { get; set; }
    public int RegionId { get; set; }
    public List<Point3d> Points { get; set; } = new List<Point3d>();
    
    // Returns the direction of the contour (clockwise/counterclockwise)
    public bool IsClockwise()
    {
        if (PolyContour == null || PolyContour.Count < 3)
            return false;
            
        // Calculate area using Green's theorem
        double area = 0;
        for (int i = 0; i < PolyContour.Count - 1; i++)
        {
            area += (PolyContour[i].X * PolyContour[i + 1].Y - 
                     PolyContour[i + 1].X * PolyContour[i].Y);
        }
        // Close the loop
        int last = PolyContour.Count - 1;
        area += (PolyContour[last].X * PolyContour[0].Y - 
                 PolyContour[0].X * PolyContour[last].Y);
                
        return area < 0; // Negative area means clockwise
    }
    
    // Calculate centroid of the contour
    public void CalculateCentroid()
    {
        if (PolyContour == null || PolyContour.Count < 3)
            return;
            
        double cx = 0, cy = 0;
        for (int i = 0; i < PolyContour.Count; i++)
        {
            cx += PolyContour[i].X;
            cy += PolyContour[i].Y;
        }
        Centroid = new Point3d(cx / PolyContour.Count, cy / PolyContour.Count, 
                              PolyContour[0].Z);
    }
}
```

### 2. Implement Contour Segmentation

```csharp
// Segment contours into regions and identify holes
private List<ContourInfo> SegmentContours(Curve[] contours, double z, double tolerance)
{
    var contourInfos = new List<ContourInfo>();
    int regionId = 0;
    
    // First pass: Create ContourInfo objects and convert to polylines
    foreach (var crv in contours)
    {
        var info = new ContourInfo { Contour = crv, RegionId = regionId++ };
        if (crv.TryGetPolyline(out Polyline poly))
        {
            info.PolyContour = poly;
            info.Points = poly.ToList();
        }
        else
        {
            // For non-polyline curves, approximate with polyline
            info.PolyContour = crv.ToPolyline(tolerance, tolerance, 0, 0).ToPolyline();
            info.Points = info.PolyContour.ToList();
        }
        
        info.IsHole = info.IsClockwise();
        info.CalculateCentroid();
        contourInfos.Add(info);
    }
    
    // Second pass: Identify parent-child relationships (which holes belong to which outer contours)
    foreach (var holeInfo in contourInfos.FindAll(c => c.IsHole))
    {
        foreach (var outerInfo in contourInfos.FindAll(c => !c.IsHole))
        {
            if (PointInPolygon(holeInfo.Centroid, outerInfo.PolyContour))
            {
                holeInfo.RegionId = outerInfo.RegionId;
                break;
            }
        }
    }
    
    return contourInfos;
}

// Helper to check if a point is inside a polygon
private bool PointInPolygon(Point3d point, Polyline polygon)
{
    if (polygon == null || polygon.Count < 3)
        return false;
        
    // Create a test line from point to outside
    double maxX = polygon.Max(p => p.X) + 1.0;
    Line testLine = new Line(point, new Point3d(maxX, point.Y, point.Z));
    
    // Count intersections
    int intersections = 0;
    for (int i = 0; i < polygon.Count - 1; i++)
    {
        Line segment = new Line(polygon[i], polygon[i + 1]);
        if (Intersection.LineLine(testLine, segment, out double _, out double _, 
                                 out double _, out double _, 
                                 RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, true))
        {
            intersections++;
        }
    }
    
    // If odd number of intersections, point is inside
    return (intersections % 2) == 1;
}
```

### 3. Add A* Pathfinding Between Contours

```csharp
// A* data structure for pathfinding
private class PathNode
{
    public Point3d Point { get; set; }
    public double GScore { get; set; } = double.MaxValue;
    public double FScore { get; set; } = double.MaxValue;
    public PathNode Parent { get; set; } = null;
    
    public PathNode(Point3d point)
    {
        Point = point;
    }
}

// Find optimal path between two points using A* algorithm
private List<Point3d> FindPath(Point3d start, Point3d end, List<ContourInfo> obstacles, double z)
{
    // Create a grid for pathfinding (simplification of the continuous space)
    double gridSize = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance * 10;
    int gridWidth = (int)Math.Ceiling((end.X - start.X) / gridSize) + 10;
    int gridHeight = (int)Math.Ceiling((end.Y - start.Y) / gridSize) + 10;
    
    var openSet = new List<PathNode>();
    var closedSet = new HashSet<string>();
    
    // Create start node
    var startNode = new PathNode(start);
    startNode.GScore = 0;
    startNode.FScore = Distance(start, end);
    openSet.Add(startNode);
    
    while (openSet.Count > 0)
    {
        // Get node with lowest f-score
        int currentIndex = 0;
        for (int i = 1; i < openSet.Count; i++)
        {
            if (openSet[i].FScore < openSet[currentIndex].FScore)
                currentIndex = i;
        }
        
        var current = openSet[currentIndex];
        
        // Check if we reached end
        if (Distance(current.Point, end) < gridSize)
        {
            // Reconstruct path
            var path = new List<Point3d>();
            var node = current;
            while (node != null)
            {
                path.Add(node.Point);
                node = node.Parent;
            }
            path.Reverse();
            return path;
        }
        
        // Remove current from open set and add to closed set
        openSet.RemoveAt(currentIndex);
        closedSet.Add($"{current.Point.X},{current.Point.Y}");
        
        // Generate neighbors in 8 directions
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                
                var neighborPoint = new Point3d(
                    current.Point.X + dx * gridSize,
                    current.Point.Y + dy * gridSize,
                    z);
                
                // Skip if point is in closed set
                if (closedSet.Contains($"{neighborPoint.X},{neighborPoint.Y}"))
                    continue;
                
                // Check if point is inside any obstacle
                bool isObstructed = false;
                foreach (var contour in obstacles)
                {
                    if (PointInPolygon(neighborPoint, contour.PolyContour))
                    {
                        isObstructed = contour.IsHole ? false : true;
                        if (isObstructed) break;
                    }
                }
                
                if (isObstructed)
                    continue;
                
                // Calculate tentative g-score
                double tentativeGScore = current.GScore + 
                    (dx != 0 && dy != 0 ? 1.414 : 1.0) * gridSize;
                
                // Find node in open set or create new one
                var neighbor = openSet.FirstOrDefault(n => 
                    Math.Abs(n.Point.X - neighborPoint.X) < 0.001 && 
                    Math.Abs(n.Point.Y - neighborPoint.Y) < 0.001);
                
                bool isNew = false;
                if (neighbor == null)
                {
                    neighbor = new PathNode(neighborPoint);
                    isNew = true;
                }
                
                if (tentativeGScore < neighbor.GScore)
                {
                    // This path is better
                    neighbor.Parent = current;
                    neighbor.GScore = tentativeGScore;
                    neighbor.FScore = tentativeGScore + Distance(neighborPoint, end);
                    
                    if (isNew)
                        openSet.Add(neighbor);
                }
            }
        }
    }
    
    // No path found
    return new List<Point3d> { start, end };
}

private double Distance(Point3d a, Point3d b)
{
    return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
```

### 4. Implement Optimized Path Generation

```csharp
// Process contours with path optimization
private List<string> ProcessContoursOptimized(List<ContourInfo> contours, double z, 
                                             double feedRate, double travelRate, 
                                             bool enableRetraction, double retractionDist)
{
    var gcode = new List<string>();
    
    if (contours.Count == 0)
        return gcode;
    
    // Sort contours: outer contours first, then holes
    contours.Sort((a, b) => {
        if (a.IsHole != b.IsHole)
            return a.IsHole ? 1 : -1;
        return a.RegionId.CompareTo(b.RegionId);
    });
    
    // Start with the first contour
    var currentPoint = contours[0].PolyContour[0];
    gcode.Add($"G0 X{currentPoint.X:F3} Y{currentPoint.Y:F3} F{travelRate}");
    
    // Process each contour
    foreach (var contour in contours)
    {
        // Find closest point on contour to current position
        int closestIdx = 0;
        double minDist = double.MaxValue;
        
        for (int i = 0; i < contour.Points.Count; i++)
        {
            double dist = Distance(currentPoint, contour.Points[i]);
            if (dist < minDist)
            {
                minDist = dist;
                closestIdx = i;
            }
        }
        
        // Reorder polyline to start from closest point
        var orderedPoints = new List<Point3d>();
        for (int i = 0; i < contour.Points.Count; i++)
        {
            int idx = (closestIdx + i) % contour.Points.Count;
            orderedPoints.Add(contour.Points[idx]);
        }
        
        // Find path to starting point of contour
        var pathToContour = FindPath(currentPoint, orderedPoints[0], 
                                    contours.Where(c => c.RegionId != contour.RegionId).ToList(), z);
        
        // Add retraction if enabled and path is long enough
        if (enableRetraction && pathToContour.Count > 1 && 
            Distance(pathToContour[0], pathToContour[pathToContour.Count-1]) > 5.0)
        {
            gcode.Add($"; Retraction");
            gcode.Add($"G1 E-{retractionDist:F3} F{feedRate * 2}");
        }
        
        // Add air move to contour
        gcode.Add("; Air move to next contour");
        foreach (var point in pathToContour)
        {
            gcode.Add($"G0 X{point.X:F3} Y{point.Y:F3} F{travelRate}");
        }
        
        // Recover retraction if enabled
        if (enableRetraction && pathToContour.Count > 1 && 
            Distance(pathToContour[0], pathToContour[pathToContour.Count-1]) > 5.0)
        {
            gcode.Add($"; Recover retraction");
            gcode.Add($"G1 E{retractionDist:F3} F{feedRate * 2}");
        }
        
        // Add contour itself
        gcode.Add("; Contour path - " + (contour.IsHole ? "Hole" : "Outer"));
        foreach (var point in orderedPoints)
        {
            gcode.Add($"G1 X{point.X:F3} Y{point.Y:F3} F{feedRate}");
            currentPoint = point;
        }
        
        // Close the loop
        gcode.Add($"G1 X{orderedPoints[0].X:F3} Y{orderedPoints[0].Y:F3} F{feedRate}");
    }
    
    return gcode;
}
```

### 5. Update Your SolveInstance Method

```csharp
protected override void SolveInstance(IGH_DataAccess DA)
{
    // Retrieve input geometry (Brep or Mesh)
    GH_ObjectWrapper geoWrapper = null;
    DA.GetData(0, ref geoWrapper);
    
    // Slice settings
    double initialHeight = 0.5;
    double layerHeight = 0.5;
    bool fillBottom = true;
    double feedRate = 1500;
    double tolerance = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
    
    // New parameters
    bool optimizePaths = true;
    int infillPattern = 0;
    double infillDensity = 20;
    bool enableRetraction = true;
    double retractionDist = 5.0;
    double travelSpeed = 3000;
    
    // Get input data
    DA.GetData(1, ref initialHeight);
    DA.GetData(2, ref layerHeight);
    DA.GetData(3, ref fillBottom);
    DA.GetData(4, ref feedRate);
    
    // Get new parameters if added
    if (Params.Input.Count > 5) DA.GetData(5, ref optimizePaths);
    if (Params.Input.Count > 6) DA.GetData(6, ref infillPattern);
    if (Params.Input.Count > 7) DA.GetData(7, ref infillDensity);
    if (Params.Input.Count > 8) DA.GetData(8, ref enableRetraction);
    if (Params.Input.Count > 9) DA.GetData(9, ref retractionDist);
    if (Params.Input.Count > 10) DA.GetData(10, ref travelSpeed);
    
    var gcode = new List<string>();
    var allSections = new List<Curve>();
    
    // Validate geometry
    if (geoWrapper == null || geoWrapper.Value == null)
    {
        gcode.Add("; No geometry provided");
        DA.SetDataList(0, gcode);
        DA.SetDataList(1, allSections);
        return;
    }
    
    // Unwrap any GH_GeometricGoo to raw Rhino object
    object data = geoWrapper.Value;
    var goo = data as IGH_GeometricGoo;
    if (goo != null)
        data = goo.ScriptVariable();
    
    // Detect Brep or Mesh
    Brep br = data as Brep;
    Mesh mesh = data as Mesh;
    if (br == null && mesh == null)
    {
        gcode.Add("; Unsupported geometry type");
        DA.SetDataList(0, gcode);
        DA.SetDataList(1, allSections);
        return;
    }
    
    // Determine max Z
    double maxZ = br != null
        ? br.GetBoundingBox(true).Max.Z
        : mesh.GetBoundingBox(true).Max.Z;
    
    // Add header G-code
    gcode.Add("; G-code generated with optimized path planning");
    gcode.Add("G90 ; Absolute positioning");
    gcode.Add("G21 ; Set units to millimeters");
    
    // Slice layers
    bool firstLayer = true;
    for (double z = initialHeight; z <= maxZ + tolerance; z += layerHeight)
    {
        if (firstLayer && !fillBottom)
        {
            firstLayer = false;
            continue;
        }
        firstLayer = false;
        
        // Create slicing plane and get contour curves
        Plane plane = new Plane(new Point3d(0, 0, z), Vector3d.ZAxis);
        Curve[] contours = null;
        
        // Intersection
        if (br != null)
        {
            Point3d[] pts;
            Intersection.BrepPlane(br, plane, tolerance, out contours, out pts);
        }
        else
        {
            contours = Mesh.CreateContourCurves(mesh, plane, tolerance);
        }
        
        // Skip if no contours
        if (contours == null || contours.Length == 0)
        {
            gcode.Add($"; No intersection at Z={z:F3}");
            continue;
        }
        
        // Add layer transition
        gcode.Add($"; Layer at Z={z:F3}");
        gcode.Add($"G1 Z{z:F3} F{feedRate}");
        
        // Add curves to preview
        allSections.AddRange(contours);
        
        if (optimizePaths)
        {
            // Segment contours and identify relationships
            var contourInfos = SegmentContours(contours, z, tolerance);
            
            // Handle bottom layer infill if needed
            if (fillBottom && Math.Abs(z - initialHeight) < tolerance)
            {
                // Add infill - can be expanded with different patterns based on infillPattern
                GenerateInfill(contourInfos, z, infillPattern, infillDensity, feedRate, ref gcode, ref allSections);
            }
            
            // Process contours with optimized path planning
            var layerGcode = ProcessContoursOptimized(contourInfos, z, 
                                                     feedRate, travelSpeed, 
                                                     enableRetraction, retractionDist);
            gcode.AddRange(layerGcode);
        }
        else
        {
            // Original code path for non-optimized G-code generation
            foreach (var crv in contours)
            {
                // Try to convert to polyline for G-code; otherwise use original for preview
                crv.TryGetPolyline(out Polyline pl);
                bool isPoly = pl != null && pl.Count >= 2;
                
                if (isPoly)
                {
                    // Rapid move to start
                    gcode.Add($"G0 X{pl[0].X:F3} Y{pl[0].Y:F3} F{travelSpeed}");
                    // Moves along path
                    for (int i = 1; i < pl.Count; i++)
                        gcode.Add($"G1 X{pl[i].X:F3} Y{pl[i].Y:F3} F{feedRate}");
                }
            }
            
            // Handle bottom layer infill
            if (fillBottom && Math.Abs(z - initialHeight) < tolerance)
            {
                // Original infill code
                foreach (var crv in contours)
                {
                    // Only polyline contours supported for infill
                    if (!crv.TryGetPolyline(out Polyline pl) || pl.Count < 3) continue;
                    var plCurve = pl.ToPolylineCurve();
                    var bbox = plCurve.GetBoundingBox(true);
                    double minY = bbox.Min.Y;
                    double maxY = bbox.Max.Y;
                    double spacing = layerHeight;
                    for (double y = minY + spacing / 2.0; y <= maxY; y += spacing)
                    {
                        var scan = new LineCurve(new Line(
                            new Point3d(bbox.Min.X, y, z),
                            new Point3d(bbox.Max.X, y, z)));
                        var inters = Intersection.CurveCurve(plCurve, scan, tolerance, tolerance);
                        if (inters == null || inters.Count < 2) continue;
                        var pts = new List<(double, Point3d)>();
                        foreach (var ev in inters)
                        {
                            if (ev.IsPoint) pts.Add((ev.ParameterB, ev.PointB));
                        }
                        if (pts.Count < 2) continue;
                        pts.Sort((a, b) => a.Item1.CompareTo(b.Item1));
                        for (int i = 0; i + 1 < pts.Count; i += 2)
                        {
                            var p0 = pts[i].Item2;
                            var p1 = pts[i + 1].Item2;
                            // Rapid move to start of infill segment
                            gcode.Add($"G0 X{p0.X:F3} Y{p0.Y:F3} F{travelSpeed}");
                            // Infill extrusion
                            gcode.Add($"G1 X{p1.X:F3} Y{p1.Y:F3} F{feedRate}");
                            allSections.Add(new LineCurve(p0, p1));
                        }
                    }
                }
            }
        }
    }
    
    // Add footer
    gcode.Add("; End of G-code");
    
    // Output results
    DA.SetDataList(0, gcode);
    
    // Unite curve segments for smoother preview/path
    Curve[] outputPaths;
    if (allSections.Count > 0)
        outputPaths = Curve.JoinCurves(allSections.ToArray(), tolerance);
    else
        outputPaths = Array.Empty<Curve>();
    DA.SetDataList(1, outputPaths);
}

// Helper method for generating infill patterns
private void GenerateInfill(List<ContourInfo> contours, double z, int pattern, double density, 
                           double feedRate, ref List<string> gcode, ref List<Curve> allSections)
{
    // Only process non-hole contours for infill
    foreach (var contour in contours.Where(c => !c.IsHole))
    {
        if (!contour.Contour.TryGetPolyline(out Polyline pl) || pl.Count < 3) continue;
        
        var plCurve = pl.ToPolylineCurve();
        var bbox = plCurve.GetBoundingBox(true);
        
        // Calculate spacing based on density (0-100%)
        double spacing = 20 * (100 - density) / 100;
        spacing = Math.Max(spacing, 0.5); // Minimum spacing
        
        // Different infill patterns
        switch (pattern)
        {
            case 0: // Linear
                GenerateLinearInfill(plCurve, bbox, z, spacing, feedRate, ref gcode, ref allSections);
                break;
            case 1: // Grid
                GenerateLinearInfill(plCurve, bbox, z, spacing, feedRate, ref gcode, ref allSections);
                // Generate perpendicular lines for grid
                var rotatedBbox = bbox;
                double temp = rotatedBbox.Min.X;
                rotatedBbox.Min.X = rotatedBbox.Min.Y;
                rotatedBbox.Min.Y = temp;
                temp = rotatedBbox.Max.X;
                rotatedBbox.Max.X = rotatedBbox.Max.Y;
                rotatedBbox.Max.Y = temp;
                GenerateLinearInfill(plCurve, rotatedBbox, z, spacing, feedRate, ref gcode, ref allSections, true);
                break;
            case 2: // Concentric
                GenerateConcentricInfill(contour, z, spacing, feedRate, ref gcode, ref allSections);
                break;
            default:
                GenerateLinearInfill(plCurve, bbox, z, spacing, feedRate, ref gcode, ref allSections);
                break;
        }
    }
}

// Helper for linear infill
private void GenerateLinearInfill(Curve plCurve, BoundingBox bbox, double z, double spacing, 
                                double feedRate, ref List<string> gcode, ref List<Curve> allSections, 
                                bool rotated = false)
{
    double travelSpeed = feedRate * 2;
    
    for (double coord = bbox.Min.Y + spacing / 2.0; coord <= bbox.Max.Y; coord += spacing)
    {
        Line scanLine;
        if (!rotated)
        {
            scanLine = new Line(
                new Point3d(bbox.Min.X - 1, coord, z),
                new Point3d(bbox.Max.X + 1, coord, z));
        }
        else
        {
            scanLine = new Line(
                new Point3d(coord, bbox.Min.X - 1, z),
                new Point3d(coord, bbox.Max.X + 1, z));
        }
        
        var scan = new LineCurve(scanLine);
        var inters = Intersection.CurveCurve(plCurve, scan, RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 
                                            RhinoDoc.ActiveDoc.ModelAbsoluteTolerance);
        
        if (inters == null || inters.Count < 2) continue;
        
        var pts = new List<(double, Point3d)>();
        foreach (var ev in inters)
        {
            if (ev.IsPoint) pts.Add((ev.ParameterB, ev.PointB));
        }
        
        if (pts.Count < 2) continue;
        pts.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        
        for (int i = 0; i + 1 < pts.Count; i += 2)
        {
            var p0 = pts[i].Item2;
            var p1 = pts[i + 1].Item2;
            
            // Rapid move to start of infill segment
            gcode.Add($"G0 X{p0.X:F3} Y{p0.Y:F3} F{travelSpeed}");
            // Infill extrusion
            gcode.Add($"G1 X{p1.X:F3} Y{p1.Y:F3} F{feedRate}");
            
            allSections.Add(new LineCurve(p0, p1));
        }
    }
}

// Helper for concentric infill
private void GenerateConcentricInfill(ContourInfo contour, double z, double spacing, 
                                    double feedRate, ref List<string> gcode, ref List<Curve> allSections)
{
    double travelSpeed = feedRate * 2;
    Curve outerCurve = contour.Contour;
    
    // Create offset curves for concentric infill
    for (double offset = spacing; offset < 1000; offset += spacing)
    {
        Curve[] offsetCurves = outerCurve.Offset(
            Plane.WorldXY, 
            -offset, 
            RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, 
            CurveOffsetCornerStyle.Sharp);
        
        if (offsetCurves == null || offsetCurves.Length == 0)
            break;
        
        foreach (var offsetCurve in offsetCurves)
        {
            if (offsetCurve.TryGetPolyline(out Polyline offsetPoly))
            {
                gcode.Add($"; Concentric infill at offset {offset:F3}");
                gcode.Add($"G0 X{offsetPoly[0].X:F3} Y{offsetPoly[0].Y:F3} F{travelSpeed}");
                
                for (int i = 1; i < offsetPoly.Count; i++)
                {
                    gcode.Add($"G1 X{offsetPoly[i].X:F3} Y{offsetPoly[i].Y:F3} F{feedRate}");
                }
                
                gcode.Add($"G1 X{offsetPoly[0].X:F3} Y{offsetPoly[0].Y:F3} F{feedRate}");
                allSections.Add(offsetCurve);
            }
        }
    }
}