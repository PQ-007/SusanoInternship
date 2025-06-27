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

        private Dictionary<Point3d, List<Line>> _intersectionPointToLinesMap;

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
            double angleTolerance = 5.0 * (Math.PI / 180.0);

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
                // Calculate midpoints manually (since Point3d + Point3d is not allowed)
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

                // Sort by Y descending (top-to-bottom), then X ascending (left-to-right)
                int yCompare = midB.Y.CompareTo(midA.Y); // Higher Y = top
                if (yCompare != 0) return yCompare;
                return midA.X.CompareTo(midB.X); // Lower X = left
            });

            // Sort vertical lines (left-to-right, then top-to-bottom)
            verticalLines.Sort((a, b) =>
            {
                // Calculate midpoints manually
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

                // Sort by X ascending (left-to-right), then Y descending (top-to-bottom)
                int xCompare = midA.X.CompareTo(midB.X); // Lower X = left
                if (xCompare != 0) return xCompare;
                return midB.Y.CompareTo(midA.Y); // Higher Y = top
            });

            ed.WriteMessage($"\nSorted {horizontalLines.Count} horizontal and {verticalLines.Count} vertical lines.");


            HashSet<Point3d> allIPoints = new HashSet<Point3d>(new Point3dComparer(geometricTolerance));


            // Initialize the 2D list with correct dimensions
            List<List<Point3d?>> allIPoints2DList = new List<List<Point3d?>>(); // Outer list correctly holds lists of nullable Point3d
            for (int i = 0; i < horizontalLines.Count; i++)
            {
                // The inner list must also be of type List<Point3d?>
                allIPoints2DList.Add(new List<Point3d?>());
                for (int j = 0; j < verticalLines.Count; j++)
                {
                    allIPoints2DList[i].Add(null); // Now this is valid because the inner list holds Point3d?
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
            ed.WriteMessage($"\n//-----------------//");
            ed.WriteMessage($"\n");
            for (int i = 0; i < horizontalLines.Count; i++)
            {
                for (int j = 0; j < verticalLines.Count; j++)
                {
                    ed.WriteMessage($"{ allIPoints2DList[i][j]}");
                }
                ed.WriteMessage($"\n");
            }
            ed.WriteMessage($"\n");
            ed.WriteMessage($"\n//-----------------//");
            

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

            // Visual Debugging: Draw grid cells (cyan rectangles) with  2% bigger margin
            double marginOffset = 50.0; 

            if (gridData.Count > 0)
            {
                ed.WriteMessage($"\nDEBUG: Drawing {gridData.Count} grid cells (cyan rectangles) with offset.\n");
                ms.UpgradeOpen();
                foreach (Point3d[] cell in gridData)
                {
                    using (Polyline poly = new Polyline())
                    {
                        double centerX = cell.Average(p => p.X);
                        double centerY = cell.Average(p => p.Y);
                        Point3d center = new Point3d(centerX, centerY, 0); 

                        for (int k = 0; k < cell.Length; k++)
                        {
                            Point3d globalCellPoint = cell[k].TransformBy(blockRef.BlockTransform);
                            Vector3d direction = globalCellPoint - center;
                            if (direction.Length > 0)
                            {
                                direction = direction.GetNormal();
                            }
                            Point3d offsetGlobalPoint = globalCellPoint - (direction * marginOffset);
                            poly.AddVertexAt(k, new Point2d(offsetGlobalPoint.X, offsetGlobalPoint.Y), 0, 0, 0);
                        }
                        poly.Closed = true;
                        poly.ColorIndex = 4; // Cyan

                        ms.AppendEntity(poly);
                        tr.AddNewlyCreatedDBObject(poly, true);
                    }
                }
                ms.DowngradeOpen();
            }


            return gridData;
        }

        public static List<Line> RayCastingInGridCells(Point3d clickedPointGlobal, List<Point3d[]> gridData, Editor ed, Transaction tr, BlockTableRecord ms, BlockReference blockRef)
        {
            List<Line> niceCell = new List<Line>();

            return niceCell;
        }
        private List<Line> FindEnclosedPolygon(Point3d clickedPointGlobal, List<Line> blockLines, Editor ed, Transaction tr, BlockTableRecord ms, BlockReference blockRef)
        {
            List<Point3d[]> gridData = LinesToConnectedGridPointData(clickedPointGlobal, blockLines, ed, tr, ms, blockRef);

            List<Line> result = RayCastingInGridCells(clickedPointGlobal, gridData, ed, tr, ms, blockRef);




            return new List<Line>(); // Return as before, or modify if you need the graph further up
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
                List<List<Line>> gridCellPolygons = new List<List<Line>>();
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
                    gridCellPolygons.Add(FindEnclosedPolygon(clickedPointGlobal, blockLines, ed, tr, ms, blockRef));
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
                int xHash = Math.Round(p.X / _toleranceValue).GetHashCode();
                int yHash = Math.Round(p.Y / _toleranceValue).GetHashCode();
                int zHash = Math.Round(p.Z / _toleranceValue).GetHashCode();
                return xHash ^ yHash ^ zHash;
            }
        }

        // Helper for comparing Point3d within tolerance directly
        private static bool Equals(Point3d p1, Point3d p2, double tolerance)
        {
            return CommandCropByClick.IsEqualTo(p1.X, p2.X, tolerance) &&
                   CommandCropByClick.IsEqualTo(p1.Y, p2.Y, tolerance) &&
                   CommandCropByClick.IsEqualTo(p1.Z, p2.Z, tolerance);
        }   
    }
}