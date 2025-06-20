using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry; 
using System.Collections.Generic; 
using System.Linq; 
using System; 

[assembly: CommandClass(typeof(MAEDA.CommandCropByClick))]

namespace MAEDA
{
    public class LineEquation2D
    {
        public double A { get; private set; }
        public double B { get; private set; }
        public double C { get; private set; }

        // Constructor to derive equation from two Point2d
        public LineEquation2D(Point2d p1, Point2d p2)
        {
            // Calculate coefficients
            A = p2.Y - p1.Y;
            B = p1.X - p2.X;
            C = -A * p1.X - B * p1.Y;

            // Normalize the equation (A^2 + B^2 = 1) to make comparison easier
            double magnitude = Math.Sqrt(A * A + B * B);
            if (magnitude > CommandCropByClick.SmallTolerance) // Use a small, consistent tolerance like 1e-9 for normalization
            {
                A /= magnitude;
                B /= magnitude;
                C /= magnitude;
            }
            else // Degenerate line (points are coincident or too close)
            {
                A = 0; B = 0; C = 0; // Represents no valid line
            }
        }

        // Method to check if two LineEquation2D objects represent the same infinite line
        public bool IsCoincident(LineEquation2D other, double tolerance)
        {
            // After normalization, A, B, C should be approximately equal
            // or exactly negative of each other (due to flipped normal vector direction).
            // This is the check where the main geometricTolerance is critical for 'C'
            bool aEqual = CommandCropByClick.IsEqualTo(this.A, other.A, tolerance);
            bool bEqual = CommandCropByClick.IsEqualTo(this.B, other.B, tolerance);
            bool cEqual = CommandCropByClick.IsEqualTo(this.C, other.C, tolerance);

            bool aNegativeEqual = CommandCropByClick.IsEqualTo(this.A, -other.A, tolerance);
            bool bNegativeEqual = CommandCropByClick.IsEqualTo(this.B, -other.B, tolerance);
            bool cNegativeEqual = CommandCropByClick.IsEqualTo(this.C, -other.C, tolerance);

            return (aEqual && bEqual && cEqual) || (aNegativeEqual && bNegativeEqual && cNegativeEqual);
        }

        // Helper to check if a Point2d lies on this line equation (within tolerance)
        public bool ContainsPoint(Point2d p, double tolerance)
        {
            return CommandCropByClick.IsZero(A * p.X + B * p.Y + C, tolerance);
        }
    }
public class CommandCropByClick
    {
        public const double SmallTolerance = 1e-9;

        private const double geometricTolerance = 10.0; 

        public static bool IsEqualTo(double d1, double d2, double tolerance)
        {
            return Math.Abs(d1 - d2) < tolerance;
        }

        public static bool IsZero(double d, double tolerance)
        {
            return Math.Abs(d) < tolerance;
        }

        public static double GetDistance(Point2d p1, Point2d p2)
        {
            return Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
        }

        private static bool IsPointInsidePolygonCustom(IEnumerable<Point2d> polygonVertices, Point2d point, double tolerance)
        {
            List<Point2d> vertices = polygonVertices.ToList();
            if (vertices.Count < 3) return false;

            // First, check if point is on any edge (within tolerance)
            for (int i = 0; i < vertices.Count; i++)
            {
                Point2d p1 = vertices[i];
                Point2d p2 = vertices[(i + 1) % vertices.Count];

                if (IsPointOnSegment2D(point, p1, p2, tolerance))
                {
                    return true; // Point is on the boundary
                }
            }

            // Ray Casting Algorithm for strict inside check (not on boundary)
            bool inside = false;
            for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
            {
                Point2d p_i = vertices[i];
                Point2d p_j = vertices[j];

                // Check if the ray from point.Y crosses the segment (p_i.Y, p_j.Y)
                // And if point.X is to the left of the segment (if it crosses)
                if (((p_i.Y > point.Y) != (p_j.Y > point.Y)) &&
                    (point.X < (p_j.X - p_i.X) * (point.Y - p_i.Y) / (p_j.Y - p_i.Y) + p_i.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static bool IsPointOnSegment2D(Point2d pt, Point2d segStart, Point2d segEnd, double tolerance)
        {
            // 1. Check for degenerate segment (start and end points are very close)
            if (GetDistance(segStart, segEnd) < SmallTolerance)
            {
                return GetDistance(pt, segStart) < tolerance; // Is pt coincident with segStart?
            }

            // 2. Collinearity check: Cross product of (pt - segStart) and (segEnd - segStart)
            // If collinear, the cross product should be zero.
            Vector2d vec1 = pt - segStart;
            Vector2d vec2 = segEnd - segStart;
            double crossProduct = vec1.X * vec2.Y - vec1.Y * vec2.X;

            // Use the provided tolerance for the IsZero check
            if (!IsZero(crossProduct, tolerance)) // Crucial for large coords and rotations
            {
                return false; // Not collinear within tolerance
            }

            // 3. Check if point is within the segment's bounding box (inflated by tolerance)
            // This ensures the point is ON the segment, not just the infinite line.
            double minX = Math.Min(segStart.X, segEnd.X);
            double maxX = Math.Max(segStart.X, segEnd.X);
            double minY = Math.Min(segStart.Y, segEnd.Y);
            double maxY = Math.Max(segStart.Y, segEnd.Y);

            return pt.X >= (minX - tolerance) && pt.X <= (maxX + tolerance) &&
                   pt.Y >= (minY - tolerance) && pt.Y <= (maxY + tolerance);
        }

        [CommandMethod("CROPCLICK")]
        public void RunCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            string searchString = "通り符号"; 

            BlockReference targetBlockRef = null; 

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Prompt the user to select a block reference (entity)

                PromptEntityOptions peo = new PromptEntityOptions("\nSelect the grid block to process: ");
                peo.SetRejectMessage($"\nInvalid selection. Please select a block reference containing '{searchString}'.");
                // Crucial: Only allow BlockReference objects to be selected
                peo.AddAllowedClass(typeof(BlockReference), true);

                PromptEntityResult per = ed.GetEntity(peo);

                if (per.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nBlock selection cancelled.");
                    return; 
                }

                targetBlockRef = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;

                // Validate if it's the specific type of block you're looking for by name
                if (targetBlockRef == null || !targetBlockRef.Name.Contains(searchString))
                {
                    ed.WriteMessage($"\nSelected object is not a block reference containing '{searchString}'.");
                    return;
                }

                ed.WriteMessage($"\nSuccessfully selected target Block: {targetBlockRef.Name} (Handle: {targetBlockRef.Handle})");

                PromptPointResult ppr = ed.GetPoint("\nTap on that grid node to crop: ");
                if (ppr.Status != PromptStatus.OK)
                {
                    return;
                }
                Point3d clickedPointGlobal = ppr.Value;
                ed.WriteMessage($"\nClickedPoint(Global): {clickedPointGlobal}");

                Matrix3d inverseTransform = targetBlockRef.BlockTransform.Inverse();
                Point3d clickedPointLocal = clickedPointGlobal.TransformBy(inverseTransform);
                ed.WriteMessage($"\nClickedPoint(BlockLocal): {clickedPointLocal}");

                List<Line> blockLines = new List<Line>();
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(targetBlockRef.BlockTableRecord, OpenMode.ForRead);
                
                // Collect all Line entities within the block definition
                foreach (ObjectId entityId in btr)
                {
                    Entity ent = (Entity)tr.GetObject(entityId, OpenMode.ForRead);
                    if (ent is Line line)
                    {
                        blockLines.Add(line);
                    }
                }

                if (blockLines.Count == 0)
                {
                    ed.WriteMessage("\nThere is no Line in targetBlock");
                    return;
                }
                // --- Core Logic: Find the surrounding cell ---
                List<Point3d> originalVertices = FindSurroundingCellPointssForRotatedGrid(
                    clickedPointLocal, blockLines, ed, tr, btr, targetBlockRef); // Pass required objects

                if (originalVertices.Count == 4)
                {
                    ed.WriteMessage("\nFour line segments surrounding the click point are identified and a valid cell is detected. A rectangle is created and added to model space in red:.");

                    btr.UpgradeOpen();

                    // Transform local cell vertices to global coordinates for drawing
                    List<Point3d> globalOriginalVertices = new List<Point3d>();
                    foreach (Point3d point in originalVertices)
                    {
                        Point3d transPoint = point.TransformBy(targetBlockRef.BlockTransform);
                        globalOriginalVertices.Add(transPoint);
                    }

                    // Draw the original detected cell
                    Polyline originalPoly = new Polyline();
                    originalPoly.ColorIndex = 1; // Red
                    foreach (Point3d pt in SortPointsForPolyline(globalOriginalVertices))
                    {
                        originalPoly.AddVertexAt(originalPoly.NumberOfVertices, new Point2d(pt.X, pt.Y), 0, 0, 0);
                    }
                    originalPoly.Closed = true;
                    btr.AppendEntity(originalPoly);
                    tr.AddNewlyCreatedDBObject(originalPoly, true);
                    ed.WriteMessage($"  - The original rectangle (Polyline, red) has been created.");

                    // Inflate the rectangle
                    double inflatePercentage = 0.05; // 5% inflation
                    List<Point3d> inflatedVertices = InflateRectangle(globalOriginalVertices, inflatePercentage);

                    if (inflatedVertices.Count == 4)
                    {
                        // Draw the inflated rectangle
                        Polyline inflatedPoly = new Polyline();
                        inflatedPoly.ColorIndex = 5; // Blue
                        foreach (Point3d pt in SortPointsForPolyline(inflatedVertices))
                        {
                            inflatedPoly.AddVertexAt(inflatedPoly.NumberOfVertices, new Point2d(pt.X, pt.Y), 0, 0, 0);
                        }
                        inflatedPoly.Closed = true;
                        btr.AppendEntity(inflatedPoly);
                        tr.AddNewlyCreatedDBObject(inflatedPoly, true);
                        ed.WriteMessage($"  - An expanded rectangle (Polyline, blue) has been created (5% expansion).");

                        // --- PointCloudEx CROP Processing ---
                        dynamic targetPointCloud = null;
                        foreach (ObjectId objId in btr)
                        {
                            // Check for "AcDbPointCloudEx" or other common PointCloud object names
                            if (objId.ObjectClass.Name == "AcDbPointCloudEx" || objId.ObjectClass.Name.Contains("PointCloud"))
                            {
                                try
                                {
                                    // Try to open for write, if it's the right type
                                    targetPointCloud = tr.GetObject(objId, OpenMode.ForWrite);
                                    if (targetPointCloud != null)
                                    {
                                        ed.WriteMessage($"\nPointCloudObject '{targetPointCloud.ActiveFileName ?? targetPointCloud.GetType().Name}' found. Now croping");
                                        break; // Found one, exit loop
                                    }
                                }
                                catch (System.Exception ex)
                                {
                                    ed.WriteMessage($"\nWarning: Could not open PointCloud object for write: {ex.Message}");
                                    targetPointCloud = null; // Reset if failed
                                }
                            }
                        }

                        if (targetPointCloud != null)
                        {
                            try
                            {
                                // Define a large enough height for the clipping volume
                                double clipVolumeHeight = 50000.0; // Adjust as necessary for your Z extent

                                // Create a Point3dCollection for the clipping boundary vertices
                                Point3dCollection clipPoints = new Point3dCollection();
                                // Add bottom plane points (from inflated vertices, with negative Z)
                                clipPoints.Add(new Point3d(inflatedVertices[0].X, inflatedVertices[0].Y, -clipVolumeHeight / 2.0));
                                clipPoints.Add(new Point3d(inflatedVertices[1].X, inflatedVertices[1].Y, -clipVolumeHeight / 2.0));
                                clipPoints.Add(new Point3d(inflatedVertices[2].X, inflatedVertices[2].Y, -clipVolumeHeight / 2.0));
                                clipPoints.Add(new Point3d(inflatedVertices[3].X, inflatedVertices[3].Y, -clipVolumeHeight / 2.0));
                                // Add first point again to close the bottom polygon
                                clipPoints.Add(new Point3d(inflatedVertices[0].X, inflatedVertices[0].Y, -clipVolumeHeight / 2.0));

                                // Add top plane points (from inflated vertices, with positive Z)
                                clipPoints.Add(new Point3d(inflatedVertices[0].X, inflatedVertices[0].Y, clipVolumeHeight / 2.0));
                                clipPoints.Add(new Point3d(inflatedVertices[1].X, inflatedVertices[1].Y, clipVolumeHeight / 2.0));
                                clipPoints.Add(new Point3d(inflatedVertices[2].X, inflatedVertices[2].Y, clipVolumeHeight / 2.0));
                                clipPoints.Add(new Point3d(inflatedVertices[3].X, inflatedVertices[3].Y, clipVolumeHeight / 2.0));
                                // Add first point again to close the top polygon
                                clipPoints.Add(new Point3d(inflatedVertices[0].X, inflatedVertices[0].Y, clipVolumeHeight / 2.0));


                                // Attempt to use reflection to create and apply PointCloudCrop
                                Type pointCloudCropType = Type.GetType("Autodesk.AutoCAD.DatabaseServices.PointCloudCrop, AcDbPointCloudObj"); // Common assembly name
                                if (pointCloudCropType == null)
                                {
                                    // Try another common assembly name or provide specific path if known
                                    pointCloudCropType = Type.GetType("Autodesk.AutoCAD.DatabaseServices.PointCloudCrop, AcMPolygonObj");
                                }
                                if (pointCloudCropType == null)
                                {
                                    // If your PointCloud object is from a specific plugin, its assembly/namespace might differ
                                    // Example if it's MAEDA.PointCloudCrop:
                                    // pointCloudCropType = Type.GetType("MAEDA.PointCloudCrop");
                                }

                                if (pointCloudCropType != null)
                                {
                                    dynamic newCrop = Activator.CreateInstance(pointCloudCropType, new object[] { IntPtr.Zero }); // Pass IntPtr.Zero for internal handle
                                    if (newCrop != null)
                                    {
                                        newCrop.Vertices = clipPoints;
                                        newCrop.CropPlane = new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis); // Explicit World XY plane

                                        // Use reflection to get the Enum value for CropType
                                        Type pointCloudCropTypeEnum = pointCloudCropType.Assembly.GetType("Autodesk.AutoCAD.DatabaseServices.PointCloudCropType");
                                        if (pointCloudCropTypeEnum != null)
                                        {
                                            newCrop.CropType = Enum.Parse(pointCloudCropTypeEnum, "Polygonal");
                                        }
                                        else
                                        {
                                            ed.WriteMessage("\nWarning: Could not find PointCloudCropType enum. Defaulting to Internal/External logic.");
                                        }

                                        newCrop.Inside = true; // Crop inside the polygon
                                        newCrop.Inverted = false; // Not inverted

                                        targetPointCloud.clearCropping(); // Clear existing crops
                                        targetPointCloud.addCroppingBoundary(newCrop); // Apply the new crop
                                        targetPointCloud.ShowCropped = true; // Ensure cropped part is shown

                                        ed.WriteMessage($"  - Crop has been applied to the PointCloud object.");
                                    }
                                    else
                                    {
                                        ed.WriteMessage("\nPointCloud CropFailed to create object. Please check your custom Point Cloud library.");
                                    }
                                }
                                else
                                {
                                    ed.WriteMessage("\nPointCloudCrop Class definition not found, please check the AutoCAD SDK version and the crop API for the PointCloud object.");
                                }
                            }
                            catch (Autodesk.AutoCAD.Runtime.Exception ex)
                            {
                                ed.WriteMessage($"\n Error: AutoCAD runtime problem while applying crop to PointCloud object:. {ex.Message}");
                                ed.WriteMessage($"  Check the AutoCAD SDK version and the crop API for the PointCloud object.");
                            }
                            catch (System.Exception ex)
                            {
                                ed.WriteMessage($"\n Error (General): A problem occurred while applying a crop to a PointCloud object. {ex.Message}");
                                ed.WriteMessage($"  StackTrace: {ex.StackTrace}");
                            }
                        }
                        else
                        {
                            ed.WriteMessage("\n No suitable PointCloud object was found in model space. ");
                        }
                    }
                    else
                    {
                        ed.WriteMessage("\nThe four vertices forming the expanded rectangle could not be extracted.");
                    }
                }
                else
                {
                    ed.WriteMessage("\nA valid rectangle cell surrounding the click point could not be found.");
                }

                tr.Commit(); // Commit the transaction to save changes and draw entities
            }
        }

        private List<Point3d> FindSurroundingCellPointssForRotatedGrid(
            Point3d clickedPointLocal, List<Line> blockLines, Editor ed, Transaction tr, BlockTableRecord ms, BlockReference targetBlockRef)
        {
            double maxDistance = 50000.0; 

            List<Line> candidateLines = blockLines
                .Where(l => l.GetClosestPointTo(clickedPointLocal, false).DistanceTo(clickedPointLocal) < maxDistance)
                .ToList();

            ed.WriteMessage($"\nDEBUG: Found {candidateLines.Count} candidate lines within {maxDistance} of clickedPointLocal.");

            if (candidateLines.Count < 2)
            {
                ed.WriteMessage("\nDEBUG: Not enough candidate lines (less than 2). Returning empty list.");
                return new List<Point3d>();
            }

            double intersectionTolerance = geometricTolerance; 
            HashSet<Point3d> allIntersectionPoints = new HashSet<Point3d>(new Point3dComparer(intersectionTolerance));

            for (int i = 0; i < candidateLines.Count; i++)
            {
                for (int j = i + 1; j < candidateLines.Count; j++)
                {
                    Point3dCollection intersections = new Point3dCollection();
                    candidateLines[i].IntersectWith(candidateLines[j], Intersect.OnBothOperands, intersections, IntPtr.Zero, IntPtr.Zero);

                    foreach (Point3d pt in intersections)
                    {
                        allIntersectionPoints.Add(pt); 
                    }
                }
            }
            ed.WriteMessage($"\nDEBUG: Found {allIntersectionPoints.Count} unique intersection points.");

            if (allIntersectionPoints.Count > 0)
            {
                ed.WriteMessage($"\nDEBUG: Drawing {allIntersectionPoints.Count} raw intersection points (magenta circles).");
                ms.UpgradeOpen(); 
                foreach (Point3d pt in allIntersectionPoints)
                {
                    Point3d globalPt = pt.TransformBy(targetBlockRef.BlockTransform);
                    using (Circle c = new Circle(globalPt, Vector3d.ZAxis, 20.0)) 
                    {
                        c.ColorIndex = 6; // Magenta
                        ms.AppendEntity(c);
                        tr.AddNewlyCreatedDBObject(c, true);
                    }
                }
                ms.DowngradeOpen(); 
            }

            List<Point3d> closestFourCorners = allIntersectionPoints
                .OrderBy(p => p.DistanceTo(clickedPointLocal))
                .Take(4)
                .ToList();

            ed.WriteMessage($"\nDEBUG: After filtering, closestFourCorners count: {closestFourCorners.Count}.");

            if (closestFourCorners.Count != 4)
            {
                ed.WriteMessage("\nDEBUG: Did not find exactly 4 closest intersection points. Returning empty list.");
                return new List<Point3d>();
            }

            ed.WriteMessage($"\nDEBUG: Drawing 4 closest corners (green DBPoints).");
            ms.UpgradeOpen();
            foreach (Point3d pt in closestFourCorners)
            {
                Point3d globalPt = pt.TransformBy(targetBlockRef.BlockTransform);
                using (DBPoint dbPt = new DBPoint(globalPt))
                {
                    dbPt.ColorIndex = 3; 
                    ms.AppendEntity(dbPt);
                    tr.AddNewlyCreatedDBObject(dbPt, true);
                }
            }
            ms.DowngradeOpen();


            List<Point3d> sortedCellCorners = SortPointsForPolyline(closestFourCorners);
            ed.WriteMessage($"\nDEBUG: Sorted cell corners: {string.Join(", ", sortedCellCorners.Select(p => $"({p.X:F1},{p.Y:F1})"))}");

            // --- Visual Debugging: Draw the temporary sorted polyline (yellow) ---
            if (sortedCellCorners.Count == 4)
            {
                ed.WriteMessage($"\nDEBUG: Drawing temporary sorted polyline (yellow).");
                ms.UpgradeOpen();
                Polyline tempSortedPoly = new Polyline();
                tempSortedPoly.ColorIndex = 2; // Yellow
                foreach (Point3d pt in sortedCellCorners)
                {
                    Point3d globalPt = pt.TransformBy(targetBlockRef.BlockTransform);
                    tempSortedPoly.AddVertexAt(tempSortedPoly.NumberOfVertices, new Point2d(globalPt.X, globalPt.Y), 0, 0, 0);
                }
                tempSortedPoly.Closed = true;
                ms.AppendEntity(tempSortedPoly);
                tr.AddNewlyCreatedDBObject(tempSortedPoly, true);
                ms.DowngradeOpen();
            }


            // 5. Verify that the sorted points are indeed connected by actual lines from blockLines
            // This is crucial for ensuring the detected corners truly form a cell defined by existing lines.
            List<Line> foundCellLines = new List<Line>();
            bool allSegmentsFound = true;
            Plane worldXYPlane = new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis);

            for (int i = 0; i < sortedCellCorners.Count; i++)
            {
                Point3d startPt3d = sortedCellCorners[i];
                Point3d endPt3d = sortedCellCorners[(i + 1) % sortedCellCorners.Count];

                Point2d startPt2d = new Point2d(startPt3d.X, startPt3d.Y);
                Point2d endPt2d = new Point2d(endPt3d.X, endPt3d.Y);

                ed.WriteMessage($"\nDEBUG: Checking segment from ({startPt2d.X:F1},{startPt2d.Y:F1}) to ({endPt2d.X:F1},{endPt2d.Y:F1}).");

                if (GetDistance(startPt2d, endPt2d) < geometricTolerance) // Use geometricTolerance here
                {
                    ed.WriteMessage($"\nDEBUG: Degenerate segment detected (distance < {geometricTolerance}). Setting allSegmentsFound to false.");
                    allSegmentsFound = false;
                    break;
                }

                LineEquation2D segmentEquation = new LineEquation2D(startPt2d, endPt2d);
                Line matchingBlockLine = null;
                int linesCheckedForSegment = 0;

                foreach (Line blockLine in blockLines)
                {
                    linesCheckedForSegment++;
                    Point2d blockLineStart2d = blockLine.StartPoint.Convert2d(worldXYPlane);
                    Point2d blockLineEnd2d = blockLine.EndPoint.Convert2d(worldXYPlane);
                    LineEquation2D blockLineEq = new LineEquation2D(blockLineStart2d, blockLineEnd2d);

                    // Check if the segment is collinear with the blockLine
                    if (segmentEquation.IsCoincident(blockLineEq, geometricTolerance))
                    {
                        ed.WriteMessage($"\nDEBUG:   - Segment coincident with blockLine from ({blockLineStart2d.X:F1},{blockLineStart2d.Y:F1}) to ({blockLineEnd2d.X:F1},{blockLineEnd2d.Y:F1}).");
                        // Check if the segment's endpoints lie on the blockLine segment (within tolerance)
                        if (IsPointOnSegment2D(startPt2d, blockLineStart2d, blockLineEnd2d, geometricTolerance) &&
                            IsPointOnSegment2D(endPt2d, blockLineStart2d, blockLineEnd2d, geometricTolerance))
                        {
                            ed.WriteMessage($"\nDEBUG:     - Both segment points found ON blockLine segment. Match found!");
                            matchingBlockLine = blockLine;
                            break; // Found matching line for this segment, move to next segment
                        }
                        else
                        {
                            ed.WriteMessage($"\nDEBUG:     - Segment points NOT both found on blockLine segment extent. (Endpoints not on blockLine segment)");
                        }
                    }
                }

                if (matchingBlockLine == null)
                {
                    ed.WriteMessage($"\nDEBUG: No matching blockLine found for segment {i} after checking {linesCheckedForSegment} lines. Setting allSegmentsFound to false.");
                    allSegmentsFound = false;
                    break;
                }
                foundCellLines.Add(matchingBlockLine);
            }

            if (!allSegmentsFound || foundCellLines.Count != 4)
            {
                ed.WriteMessage($"\nDEBUG: allSegmentsFound is {allSegmentsFound} or foundCellLines count ({foundCellLines.Count}) is not 4. Returning empty list.");
                return new List<Point3d>();
            }

            // 6. Optional rectangle validation (if enabled)
            // Uncomment and use these if you want to strictly enforce parallelogram/rectangle properties
            // The existing debug output should tell you if these conditions are met anyway.
            Vector2d v1_2d = new Vector2d(sortedCellCorners[1].X - sortedCellCorners[0].X, sortedCellCorners[1].Y - sortedCellCorners[0].Y);
            Vector2d v2_2d = new Vector2d(sortedCellCorners[2].X - sortedCellCorners[1].X, sortedCellCorners[2].Y - sortedCellCorners[1].Y);
            Vector2d v3_2d = new Vector2d(sortedCellCorners[3].X - sortedCellCorners[2].X, sortedCellCorners[3].Y - sortedCellCorners[2].Y);
            Vector2d v4_2d = new Vector2d(sortedCellCorners[0].X - sortedCellCorners[3].X, sortedCellCorners[0].Y - sortedCellCorners[3].Y);

            bool isParallelogram = IsEqualTo(v1_2d.Length, v3_2d.Length, geometricTolerance) && IsEqualTo(v2_2d.Length, v4_2d.Length, geometricTolerance);
            bool isRectangle = IsZero(v1_2d.DotProduct(v2_2d), geometricTolerance * v1_2d.Length * v2_2d.Length); // Scale tolerance by lengths

            ed.WriteMessage($"\nDEBUG: Shape validation - IsParallelogram: {isParallelogram}, IsRectangle: {isRectangle}.");

            // If you want to enforce these conditions, uncomment the returns:
            // if (!isParallelogram) { ed.WriteMessage("\nDEBUG: Not a parallelogram."); return new List<Point3d>(); }
            // if (!isRectangle) { ed.WriteMessage("\nDEBUG: Not a rectangle (non-perpendicular corners)."); return new List<Point3d>(); }


            // 7. Check clicked point inside cell
            List<Point2d> polygonVertices2d = sortedCellCorners.Select(p => new Point2d(p.X, p.Y)).ToList();
            Point2d clickedPoint2d = new Point2d(clickedPointLocal.X, clickedPointLocal.Y);

            if (!IsPointInsidePolygonCustom(polygonVertices2d, clickedPoint2d, geometricTolerance))
            {
                ed.WriteMessage("\nDEBUG: Clicked point is NOT inside the detected cell polygon. Returning empty list.");
                return new List<Point3d>();
            }

            ed.WriteMessage("\nDEBUG: All checks passed. Returning sorted cell corners.");
            return sortedCellCorners;
        }

        // Sorts points to form a consistent polyline (e.g., clockwise or counter-clockwise)
        private List<Point3d> SortPointsForPolyline(List<Point3d> points)
        {
            if (points.Count != 4) return points;

            // Calculate centroid in 2D
            Point2d centroid2d = Point2d.Origin;
            foreach (Point3d p in points)
            {
                centroid2d = centroid2d + (new Point2d(p.X, p.Y).GetAsVector() / points.Count);
            }

            // Sort by angle around the centroid
            return points.OrderBy(p =>
            {
                Vector2d vec2d = new Point2d(p.X, p.Y) - centroid2d;
                return Math.Atan2(vec2d.Y, vec2d.X); // Angle from positive X-axis
            }).ToList();
        }

        // Inflates a 2D rectangle defined by 4 Point3d vertices
        private List<Point3d> InflateRectangle(List<Point3d> originalVertices, double percentage)
        {
            if (originalVertices == null || originalVertices.Count != 4)
            {
                return new List<Point3d>();
            }

            // Calculate centroid in 2D for inflation
            Point2d centroid2d = Point2d.Origin;
            foreach (Point3d p in originalVertices)
            {
                centroid2d = centroid2d + (new Point2d(p.X, p.Y).GetAsVector() / originalVertices.Count);
            }

            List<Point3d> inflatedVertices = new List<Point3d>();
            foreach (Point3d p in originalVertices)
            {
                Point2d p2d = new Point2d(p.X, p.Y);
                Vector2d vecFromCentroid2d = p2d - centroid2d;

                // Scale the vector from centroid to inflate the point
                Vector2d inflatedVec2d = vecFromCentroid2d * (1.0 + percentage);

                // Create the new 3D point, keeping original Z
                Point3d newP = new Point3d(centroid2d.X + inflatedVec2d.X, centroid2d.Y + inflatedVec2d.Y, p.Z);
                inflatedVertices.Add(newP);
            }

            return inflatedVertices;
        }

        // Custom comparer for Point3d to use with HashSet (for unique intersection points)
        // Uses geometricTolerance for comparison
        private class Point3dComparer : IEqualityComparer<Point3d>
        {
            private readonly double _toleranceValue;

            public Point3dComparer(double toleranceValue)
            {
                _toleranceValue = toleranceValue;
            }

            public bool Equals(Point3d p1, Point3d p2)
            {
                // Use custom IsEqualTo for each coordinate, or Point3d.IsEqualTo if reliable
                return CommandCropByClick.IsEqualTo(p1.X, p2.X, _toleranceValue) &&
                       CommandCropByClick.IsEqualTo(p1.Y, p2.Y, _toleranceValue) &&
                       CommandCropByClick.IsEqualTo(p1.Z, p2.Z, _toleranceValue);
                // Note: Autodesk.AutoCAD.Geometry.Point3d.IsEqualTo(Point3d, Tolerance) is also an option
                // but sometimes direct comparison provides more control if it's failing.
                // return p1.IsEqualTo(p2, new Tolerance(_toleranceValue, _toleranceValue));
            }

            public int GetHashCode(Point3d p)
            {
                // A simple hash code based on rounding to tolerance steps
                // This ensures points that are "equal" within tolerance have the same hash code.
                int xHash = Math.Round(p.X / _toleranceValue).GetHashCode();
                int yHash = Math.Round(p.Y / _toleranceValue).GetHashCode();
                int zHash = Math.Round(p.Z / _toleranceValue).GetHashCode();
                return xHash ^ yHash ^ zHash;
            }
        }
    }
}