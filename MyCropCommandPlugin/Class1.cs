using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry; 
using System.Collections.Generic; 
using System.Linq; 
using System;
using Autodesk.AutoCAD.GraphicsInterface;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using System.Runtime.CompilerServices;

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

            Vector3d cross = ap.CrossProduct(ab);
            if (cross.Length > tolerance) return false;

            double dot = ap.DotProduct(ab);
            if (dot < -tolerance || dot > ab.LengthSqrd + tolerance) return true;
            else return false;
        }

        public static double GetDistance(Point2d p1, Point2d p2)
        {
            return Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
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
        private List<Line> FindEnclosedPolygon(Point3d clickedPointGlobal, List<Line> blockLines, Editor ed, Transaction tr, BlockTableRecord ms, BlockReference blockRef)
        {
            // Convert clickedPointGlobal to local coordinates of the block reference
            Matrix3d inverseTransform = blockRef.BlockTransform.Inverse();
            Point3d clickedPointLocal = clickedPointGlobal.TransformBy(inverseTransform);
            ed.WriteMessage($"\nClickedPoint(BlockLocal): {clickedPointLocal}");

            _intersectionPointToLinesMap = new Dictionary<Point3d, List<Line>>(new Point3dComparer(geometricTolerance));

            // Find lines within a maxDistance from the clicked point
            double maxDistance = 13000.0;
            List<Line> candidateLines = blockLines
                .Where(l => l.GetClosestPointTo(clickedPointLocal, false).DistanceTo(clickedPointLocal) < maxDistance)
                .ToList();
            ed.WriteMessage($"\nDEBUG: Found {candidateLines.Count} candidate lines within {maxDistance:F2} of clickedPointLocal.");

            HashSet<Point3d> allIPoints = new HashSet<Point3d>(new Point3dComparer(geometricTolerance));

            for (int i = 0; i < candidateLines.Count; i++)
            {
                for (int j = i + 1; j < candidateLines.Count; j++)
                {
                    Point3dCollection intersections = new Point3dCollection();
                    candidateLines[i].IntersectWith(candidateLines[j], Intersect.OnBothOperands, intersections, IntPtr.Zero, IntPtr.Zero);
                   
                    foreach (Point3d pt in intersections)
                    {
                        allIPoints.Add(pt);
                    }
                }
            }

            ???
            //List<Dictionary<Point3d, List<Line>>> IPointToLinesMap = new List<Dictionary<Point3d, List<Line>>>();
            //foreach (Point3d pt in allIPoints)
            //{
            //    List<Line> temp = new List<Line>();
            //    foreach (Line line in candidateLines)
            //    {
            //        if (isPointOnLine(line, pt))
            //        {
            //            temp.Add(line);
            //        }
            //    }
            //    IPointToLinesMap.Add({pt, temp});
            //}

            


            ed.WriteMessage($"\nDEBUG: Found {allIPoints.Count} unique intersection points.");

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

            return new List<Line>();
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
        
    }
}
