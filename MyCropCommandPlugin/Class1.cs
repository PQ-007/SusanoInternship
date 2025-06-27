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

        private const double maxDistance = 13000.0;

        private bool onManyBlockRef = false;

        public static bool IsEqualTo(double d1, double d2, double tolerance)
        {
            return Math.Abs(d1 - d2) < tolerance;
        }

        public static bool IsZero(double d, double tolerance)
        {
            return Math.Abs(d) < tolerance;
        }
        public static bool isPointOnLine(Line line, Point3d pt, double tolerance = 1e-9)
        {
            Vector3d ap = pt - line.StartPoint;
            Vector3d ab = line.EndPoint - line.StartPoint;

            // Check if point is collinear with the line segment
            Vector3d cross = ap.CrossProduct(ab);
            if (cross.Length > tolerance) return false;

            // Check if point is within the bounds of the line segment
            double dot = ap.DotProduct(ab);
            if (dot < -tolerance || dot > ab.LengthSqrd + tolerance) return false;
            return true;
        }
        
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
        public static double GetDistance(Point2d p1, Point2d p2)
        {
            return Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
        }

        public static List<Point3d[]> LinesToConnectedGridPointData(Point3d clickedPointGlobal, List<Line> blockLines, Editor ed, Transaction tr, BlockTableRecord ms, BlockReference blockRef)
        {
            // Convert clickedPointGlobal to local coordinates of the block reference
            Matrix3d inverseTransform = blockRef.BlockTransform.Inverse();
            Point3d clickedPointLocal = clickedPointGlobal.TransformBy(inverseTransform);
            ed.WriteMessage($"\nClickedPoint(BlockLocal): {clickedPointLocal}");

            // Find lines within a maxDistance from the clicked point
            List<Line> candidateLines = blockLines
                .Where(l => l.GetClosestPointTo(clickedPointLocal, false).DistanceTo(clickedPointLocal) < maxDistance)
                .ToList();
            ed.WriteMessage($"\nDEBUG: Found {candidateLines.Count} candidate lines within {maxDistance:F2} of clickedPointLocal.");

            // Sorting candidateLines to iterate in it effectively
            List<Line> horizontalLines = new List<Line>();
            List<Line> verticalLines = new List<Line>();
            double angleTolerance = 5.0 * (Math.PI / 180.0); // 5 degrees tolerance

            foreach (Line line in candidateLines)
            {
                Vector3d direction = line.EndPoint - line.StartPoint;
                double angle = Math.Atan2(direction.Y, direction.X);

                // Normalize angle to [0, π)
                angle = (angle < 0) ? angle + Math.PI : angle;

                // Check if line is horizontal (within tolerance of 0° or 180°)
                if (angle < angleTolerance || Math.Abs(angle - Math.PI) < angleTolerance)
                {
                    horizontalLines.Add(line);
                }
                // Check if line is vertical (within tolerance of 90°)
                else if (Math.Abs(angle - (Math.PI / 2)) < angleTolerance)
                {
                    verticalLines.Add(line);
                }
            }

            // Sort horizontal lines (top-to-bottom, then left-to-right)
            horizontalLines.Sort((a, b) =>
            {
                Point3d midA = new Point3d(
                    (a.StartPoint.X + a.EndPoint.X) * 0.5,
                    (a.StartPoint.Y + a.EndPoint.Y) * 0.5,
                    (a.StartPoint.Z + a.EndPoint.Z) * 0.5
                );

                Point3d midB = new Point3d(
                    (b.StartPoint.X + b.EndPoint.X) * 0.5,
                    (b.StartPoint.Y + b.EndPoint.Y) * 0.5,
                    (b.StartPoint.Z + b.EndPoint.Z) * 0.5
                );

                // Sort by Y descending (higher Y is "top"), then X ascending (lower X is "left")
                int yCompare = midB.Y.CompareTo(midA.Y);
                if (yCompare != 0) return yCompare;
                return midA.X.CompareTo(midB.X);
            });

            // Sort vertical lines (left-to-right, then top-to-bottom)
            verticalLines.Sort((a, b) =>
            {
                Point3d midA = new Point3d(
                    (a.StartPoint.X + a.EndPoint.X) * 0.5,
                    (a.StartPoint.Y + a.EndPoint.Y) * 0.5,
                    (a.StartPoint.Z + a.EndPoint.Z) * 0.5
                );

                Point3d midB = new Point3d(
                    (b.StartPoint.X + b.EndPoint.X) * 0.5,
                    (b.StartPoint.Y + b.EndPoint.Y) * 0.5,
                    (b.StartPoint.Z + b.EndPoint.Z) * 0.5
                );

                // Sort by X ascending (lower X is "left"), then Y descending (higher Y is "top")
                int xCompare = midA.X.CompareTo(midB.X);
                if (xCompare != 0) return xCompare;
                return midB.Y.CompareTo(midA.Y);
            });

            ed.WriteMessage($"\nSorted {horizontalLines.Count} horizontal and {verticalLines.Count} vertical lines.");

            HashSet<Point3d> allIPoints = new HashSet<Point3d>(new Point3dComparer(geometricTolerance));

            // Initialize the 2D list with correct dimensions
            List<List<Point3d?>> allIPoints2DList = new List<List<Point3d?>>();
            for (int i = 0; i < horizontalLines.Count; i++)
            {
                allIPoints2DList.Add(new List<Point3d?>());
                for (int j = 0; j < verticalLines.Count; j++)
                {
                    allIPoints2DList[i].Add(null);
                }
            }

            // Finding intersections and populate the 2D list and print
            for (int i = 0; i < horizontalLines.Count; i++)
            {
                for (int j = 0; j < verticalLines.Count; j++)
                {
                    Point3dCollection intersections = new Point3dCollection();
                    horizontalLines[i].IntersectWith(
                        verticalLines[j],
                        Intersect.OnBothOperands,
                        intersections,
                        IntPtr.Zero,
                        IntPtr.Zero
                    );

                    if (intersections.Count == 1)
                    {
                        allIPoints2DList[i][j] = intersections[0];
                        allIPoints.Add(intersections[0]);
                    }
                }
            }
            ed.WriteMessage($"\n//--- Raw Intersection Points (Local Block Coordinates) ---//");
            for (int i = 0; i < horizontalLines.Count; i++)
            {
                for (int j = 0; j < verticalLines.Count; j++)
                {
                    ed.WriteMessage($"[{i},{j}]: {allIPoints2DList[i][j]} ");
                }
                ed.WriteMessage($"\n");
            }
            ed.WriteMessage($"\n//---------------------------------------------------------//");

            // --- Visual Debugging: Draw raw intersection points (magenta circles) ---
            double debugCircleRadius = maxDistance * 0.01;
            if (debugCircleRadius < 1.0) debugCircleRadius = 1.0;
            if (allIPoints.Count > 0)
            {
                ed.WriteMessage($"\nDEBUG: Drawing {allIPoints.Count} raw intersection points (magenta circles). Radius: {debugCircleRadius:F2}\n");
                ms.UpgradeOpen();
                foreach (Point3d pt in allIPoints)
                {
                    Point3d globalPt = pt.TransformBy(blockRef.BlockTransform);
                    using (Circle c = new Circle(globalPt, Vector3d.ZAxis, debugCircleRadius))
                    {
                        c.ColorIndex = 6; // Magenta
                        ms.AppendEntity(c);
                        tr.AddNewlyCreatedDBObject(c, true);
                    }
                }
                ms.DowngradeOpen();
            }

            // --- Bundling four intersection points into one cell ---
            List<Point3d[]> gridData = new List<Point3d[]>();

            for (int i = 0; i < allIPoints2DList.Count - 1; i++)
            {
                for (int j = 0; j < allIPoints2DList[i].Count - 1; j++)
                {
                    Point3d? p1 = allIPoints2DList[i][j];
                    Point3d? p2 = allIPoints2DList[i][j + 1];
                    Point3d? p3 = allIPoints2DList[i + 1][j + 1];
                    Point3d? p4 = allIPoints2DList[i + 1][j];
                    if (p1.HasValue && p2.HasValue && p3.HasValue && p4.HasValue)
                    {
                        gridData.Add(new Point3d[] { p1.Value, p2.Value, p3.Value, p4.Value });
                    }
                }
            }

            // Visual Debugging: Draw grid cells (cyan rectangles) with 2% bigger margin
            double marginOffset = 50.0; // This margin offset should be handled carefully if you want exact polygon bounds.
                                        // For visual debugging, it's fine. For actual cropping, use the exact bounds.

            //if (gridData.Count > 0)
            //{
            //    ed.WriteMessage($"\nDEBUG: Drawing {gridData.Count} grid cells (cyan rectangles) with offset.\n");
            //    ms.UpgradeOpen();
            //    foreach (Point3d[] cell in gridData)
            //    {
            //        using (Polyline poly = new Polyline())
            //        {
            //            // Calculate center for offset direction (in local block coordinates)
            //            double centerX = cell.Average(p => p.X);
            //            double centerY = cell.Average(p => p.Y);
            //            Point3d centerLocal = new Point3d(centerX, centerY, 0);

            //            // Collect points for the polyline, applying offset and transforming to global
            //            // The order is important to form a closed polygon
            //            Point3d[] orderedCellPoints = new Point3d[4];
            //            // Assuming gridData is ordered as {top-left, top-right, bottom-right, bottom-left}
            //            // based on the sorting logic and how allIPoints2DList is populated
            //            // p1 (i,j) -> top-left
            //            // p2 (i,j+1) -> top-right
            //            // p3 (i+1,j+1) -> bottom-right
            //            // p4 (i+1,j) -> bottom-left
            //            orderedCellPoints[0] = cell[0]; // p1
            //            orderedCellPoints[1] = cell[1]; // p2
            //            orderedCellPoints[2] = cell[2]; // p3
            //            orderedCellPoints[3] = cell[3]; // p4


            //            for (int k = 0; k < orderedCellPoints.Length; k++)
            //            {
            //                Point3d localCellPoint = orderedCellPoints[k];

            //                // Calculate direction vector from cell center to the current point
            //                Vector3d direction = localCellPoint - centerLocal;
            //                if (direction.Length > SmallTolerance) // Avoid division by zero for coincident points
            //                {
            //                    direction = direction.GetNormal();
            //                }

            //                // Apply offset (towards the center)
            //                Point3d offsetLocalPoint = localCellPoint - (direction * marginOffset);

            //                // Transform to global coordinates for drawing
            //                Point3d globalOffsetPoint = offsetLocalPoint.TransformBy(blockRef.BlockTransform);
            //                poly.AddVertexAt(k, new Point2d(globalOffsetPoint.X, globalOffsetPoint.Y), 0, 0, 0);
            //            }
            //            poly.Closed = true;
            //            poly.ColorIndex = 4; // Cyan

            //            ms.AppendEntity(poly);
            //            tr.AddNewlyCreatedDBObject(poly, true);
            //        }
            //    }
            //    ms.DowngradeOpen();
            //}

            return gridData; // Returns the list of 4-point arrays representing cells in BLOCK LOCAL coordinates
        }

        public static List<Line> RayCastingInGridCells(Point3d clickedPointGlobal, List<Point3d[]> gridData, Editor ed, Transaction tr, BlockTableRecord ms, BlockReference blockRef)
        {
            // 1. Transform clickedPointGlobal to the block's local coordinates
            Matrix3d inverseTransform = blockRef.BlockTransform.Inverse();
            Point3d clickedPointLocal = clickedPointGlobal.TransformBy(inverseTransform);

            ed.WriteMessage($"\nDEBUG: RayCasting - Clicked Point Local (for this block): {clickedPointLocal}");

            // Iterate through each potential grid cell
            foreach (Point3d[] cell in gridData)
            {
                // Determine min/max X and Y from the cell's points in local coordinates.
                // Assuming `cell` contains points that form a rectangle or a quadrilateral close to it.
                // The points are expected to be {p1, p2, p3, p4} in some order (e.g., TL, TR, BR, BL or similar).
                double minX = cell.Min(p => p.X);
                double maxX = cell.Max(p => p.X);
                double minY = cell.Min(p => p.Y);
                double maxY = cell.Max(p => p.Y);

                double clickTolerance = 1.0;

                // Perform a simple bounding box check. For axis-aligned rectangles, this is sufficient.
                // We use tolerance for inclusive boundary checks.   
                bool inX = clickedPointLocal.X >= minX - clickTolerance && clickedPointLocal.X <= maxX + clickTolerance;
                bool inY = clickedPointLocal.Y >= minY - clickTolerance && clickedPointLocal.Y <= maxY + clickTolerance;

                if (inX && inY)
                {
                    ed.WriteMessage($"\nDEBUG: Clicked point {clickedPointLocal} found inside a grid cell in local coordinates.");

                    // This is the cell the user clicked inside.
                    // Create the boundary lines for this cell. These lines should be in GLOBAL coordinates.
                    List<Line> enclosedPolygonLines = new List<Line>();

                    // Transform local cell points back to global coordinates to create the lines
                    Point3d gp1 = cell[0].TransformBy(blockRef.BlockTransform);
                    Point3d gp2 = cell[1].TransformBy(blockRef.BlockTransform);
                    Point3d gp3 = cell[2].TransformBy(blockRef.BlockTransform);
                    Point3d gp4 = cell[3].TransformBy(blockRef.BlockTransform);

                    // Create lines forming the rectangle
                    enclosedPolygonLines.Add(new Line(gp1, gp2));
                    enclosedPolygonLines.Add(new Line(gp2, gp3));
                    enclosedPolygonLines.Add(new Line(gp3, gp4));
                    enclosedPolygonLines.Add(new Line(gp4, gp1));

                    //// Optional: Draw the identified cell in a distinct color (e.g., red) for final verification
                    //ms.UpgradeOpen();
                    //using (Polyline identifiedPoly = new Polyline())
                    //{
                    //    identifiedPoly.AddVertexAt(0, new Point2d(gp1.X, gp1.Y), 0, 0, 0);
                    //    identifiedPoly.AddVertexAt(1, new Point2d(gp2.X, gp2.Y), 0, 0, 0);
                    //    identifiedPoly.AddVertexAt(2, new Point2d(gp3.X, gp3.Y), 0, 0, 0);
                    //    identifiedPoly.AddVertexAt(3, new Point2d(gp4.X, gp4.Y), 0, 0, 0);
                    //    identifiedPoly.Closed = true;
                    //    identifiedPoly.ColorIndex = 1; // Red (ACI color index 1)
                    //    ms.AppendEntity(identifiedPoly);
                    //    tr.AddNewlyCreatedDBObject(identifiedPoly, true);
                    //}
                    //ms.DowngradeOpen();

                    return enclosedPolygonLines;
                }
            }

            ed.WriteMessage($"\nDEBUG: Clicked point {clickedPointLocal} not found inside any grid cell for this block (in local coordinates).");
            return new List<Line>(); // Return empty list if no cell found
        }

        private List<Line> FindEnclosedPolygon(Point3d clickedPointGlobal, List<Line> blockLines, Editor ed, Transaction tr, BlockTableRecord ms, BlockReference blockRef)
        {
            // gridData contains the 4 corner points of each cell in the BLOCK'S LOCAL COORDINATES
            List<Point3d[]> gridData = LinesToConnectedGridPointData(clickedPointGlobal, blockLines, ed, tr, ms, blockRef);

            // Pass blockRef to RayCastingInGridCells so it can transform the clicked point
            List<Line> enclosedCellLines = RayCastingInGridCells(clickedPointGlobal, gridData, ed, tr, ms, blockRef);

            // Return the lines of the found cell. If nothing was found, it will be an empty list.
            return enclosedCellLines;
        }

        private Polyline GetUnifiedOuterPolylineFromLines(List<Line> lines, Editor ed, Transaction tr, BlockTableRecord ms)
        {
            double tolerance = SmallTolerance;
            var curves = new DBObjectCollection();
            foreach (var line in lines)
                curves.Add((Curve)line.Clone());
            var regions = Region.CreateFromCurves(curves);

            if (regions.Count == 0)
            {
                ed.WriteMessage("\n❌ Region creation failed. Check line connectivity.");
                return null;
            }

            Region finalRegion = regions[0] as Region;
            for (int i = 1; i < regions.Count; i++)
            {
                Region r = regions[i] as Region;
                try
                {
                    finalRegion.BooleanOperation(BooleanOperationType.BoolUnite, r);
                }
                catch
                {
                    ed.WriteMessage($"\n⚠️ Boolean union failed for region {i}. Skipping.");
                }
            }

            var exploded = new DBObjectCollection();
            finalRegion.Explode(exploded);

            var curvesList = exploded.Cast<Curve>().ToList();
            if (curvesList.Count == 0)
            {
                ed.WriteMessage("\n⚠️ No boundary curves found.");
                return null;
            }

            List<Point3d> ordered = new List<Point3d>();
            Curve current = curvesList[0];
            ordered.Add(current.StartPoint);
            ordered.Add(current.EndPoint);
            curvesList.RemoveAt(0);

            while (curvesList.Count > 0)
            {
                Point3d last = ordered.Last();
                int index = curvesList.FindIndex(c =>
                    c.StartPoint.IsEqualTo(last, new Tolerance(tolerance, tolerance)) ||
                    c.EndPoint.IsEqualTo(last, new Tolerance(tolerance, tolerance)));

                if (index == -1)
                {
                    ed.WriteMessage("\n⚠️ Cannot find next connected segment.");
                    break;
                }
                Curve next = curvesList[index];
                curvesList.RemoveAt(index);

                if (next.StartPoint.IsEqualTo(last, new Tolerance(tolerance, tolerance)))
                    ordered.Add(next.EndPoint);
                else
                    ordered.Add(next.StartPoint);
            }

            var pl = new Polyline();

            for (int i = 0; i < ordered.Count; i++)

                pl.AddVertexAt(i, new Point2d(ordered[i].X, ordered[i].Y), 0, 0, 0);

            pl.Closed = true;
            pl.ColorIndex = 4;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl,true);

            // Inflated the Polygon
            double inflatePercentange = 0.05;
            List<Point3d> inflatedVertices = InflateRectangle(ordered, inflatePercentange);

            Polyline inflatedPoly = new Polyline();
            inflatedPoly.ColorIndex = 1; 
            foreach (Point3d pt in inflatedVertices)
            {
                inflatedPoly.AddVertexAt(inflatedPoly.NumberOfVertices, new Point2d(pt.X, pt.Y), 0, 0, 0);
            }
            inflatedPoly.Closed = true;
            ms.AppendEntity(inflatedPoly);
            tr.AddNewlyCreatedDBObject(inflatedPoly, true);

            return pl;

        }

        
        [CommandMethod("CROPCLICK")]
        public void RunCommand()
        {
            // Get the active document, database, and editor
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Define the search string for block names
            string searchString = "通り符号";

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Open the BlockTable and ModelSpace for reading
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // Taking blockRefs from autoCAD
                List<BlockReference> foundBlockRefs = new List<BlockReference>();
                foreach (ObjectId objId in ms)
                {
                    if (objId.ObjectClass.Name == "AcDbBlockReference")
                    {
                        BlockReference br = (BlockReference)tr.GetObject(objId, OpenMode.ForRead);
                        // Check if the block name contains the search string and if it has any lines
                        if (br.Name.Contains(searchString))
                        {
                            foundBlockRefs.Add(br);
                        }
                    }
                }
                if (foundBlockRefs.Count == 0)
                {
                    ed.WriteMessage($"\n'BlockRef not found'");
                    return;
                }

                // Taking user input as Point3d to select gridCell
                PromptPointResult ppr = ed.GetPoint("\nTap on that grid node to crop: ");
                if (ppr.Status != PromptStatus.OK)
                {
                    return;
                }
                Point3d clickedPointGlobal = ppr.Value;
                ed.WriteMessage($"\nClickedPoint(Global): {clickedPointGlobal}");

                //Taking grid cell polygons from foundBlockRefs
                List<Line> allFoundGridCellPolygons = new List<Line>();
                foreach (BlockReference blockRef in foundBlockRefs)
                {
                    // Open the block reference for read
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead);

                    List<Line> blockLines = new List<Line>();
                    // Iterate through the block's entities
                    foreach (ObjectId objId in btr)
                    {
                        Entity entity = (Entity)tr.GetObject(objId, OpenMode.ForRead);
                        if (entity is Line line && line.Layer == "★4通り芯")
                        {
                            blockLines.Add(line);
                        }
                    }
                    if (blockLines.Any()) // Only process blocks that actually contain lines on the target layer
                    {
                        List<Line> foundPolygon = FindEnclosedPolygon(clickedPointGlobal, blockLines, ed, tr, ms, blockRef);
                        if (foundPolygon.Any())
                        {   foreach (Line line in foundPolygon) {
                                allFoundGridCellPolygons.Add(line);
                            }
                            
                        }
                    }
                }

                if (allFoundGridCellPolygons.Count == 0)
                {
                    ed.WriteMessage("\nNo grid cell found containing the clicked point in any relevant block.");
                }
                else if (allFoundGridCellPolygons.Count > 1)
                {
                    ed.WriteMessage("\nWarning: Multiple grid cells found containing the clicked point. Using the first one found.");
                    // Flatten 2D list to linear

                    Polyline lastRes = GetUnifiedOuterPolylineFromLines(allFoundGridCellPolygons, ed, tr, ms);
                }

                tr.Commit();
            }
        }

        private class Point3dComparer : IEqualityComparer<Point3d>
        {
            private readonly double _toleranceValue;

            public Point3dComparer(double toleranceValue)
            {
                _toleranceValue = toleranceValue;
            }

            public bool Equals(Point3d p1, Point3d p2)
            {
                return CommandCropByClick.IsEqualTo(p1.X, p2.X, _toleranceValue) &&
                       CommandCropByClick.IsEqualTo(p1.Y, p2.Y, _toleranceValue) &&
                       CommandCropByClick.IsEqualTo(p1.Z, p2.Z, _toleranceValue);
            }

            public int GetHashCode(Point3d p)
            {
                // Simple hashing by rounding to tolerance; more robust for larger tolerances.
                // For very small tolerances, direct double hash codes might be more appropriate,
                // but for Point3d comparisons with tolerance, this is a common approach.
                int xHash = Math.Round(p.X / _toleranceValue).GetHashCode();
                int yHash = Math.Round(p.Y / _toleranceValue).GetHashCode();
                int zHash = Math.Round(p.Z / _toleranceValue).GetHashCode();
                return xHash ^ yHash ^ zHash;
            }
        }

        
    }
}