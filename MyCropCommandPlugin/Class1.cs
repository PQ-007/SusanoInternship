using System;

using System.Collections.Generic;

using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;

using Autodesk.AutoCAD.DatabaseServices;

using Autodesk.AutoCAD.EditorInput;

using Autodesk.AutoCAD.Geometry;

using Autodesk.AutoCAD.GraphicsInterface;

using Autodesk.AutoCAD.Runtime;



[assembly: CommandClass(typeof(MAEDA.CommandCropByClick))]



namespace MAEDA

{

    public class CommandCropByClick

    {

        public static bool running = true;

        private static Editor _editorForMonitor;

        private static List<TransientMarker> _transientGraphics = new List<TransientMarker>();

        private static string _searchString = "通り符号";



        [CommandMethod("CROPCLICK")]

        public void RunCommand()

        {

            Document doc = Application.DocumentManager.MdiActiveDocument;

            _editorForMonitor = doc.Editor;



            running = true;



            _editorForMonitor.WriteMessage("\nCROPCLICK command started. Hover over grid cells to preview, click to crop, or press Esc/Right-Click -> Cancel to exit.\n");



            try

            {

                while (running)

                {

                    if (!Solve())

                    {

                        running = false;

                    }

                }

            }

            finally

            {

                ClearTransientGraphics();

                _editorForMonitor.WriteMessage("\nCROPCLICK command ended.\n");

            }

        }



        public bool Solve()

        {

            Document doc = Application.DocumentManager.MdiActiveDocument;

            Database db = doc.Database;

            Editor ed = doc.Editor;





            ed.PointMonitor += OnPointMonitor;



            PromptPointOptions ppo = new PromptPointOptions("\nClick a point inside the grid (hover for preview) or Press Esc/Right-Click -> Cancel to exit:");

            PromptPointResult ppr = ed.GetPoint(ppo);



            ed.PointMonitor -= OnPointMonitor;

            ClearTransientGraphics();



            if (ppr.Status == PromptStatus.Cancel)

            {

                ed.WriteMessage("\nUser cancelled the point selection. Exiting command...\n");

                return false;

            }

            else if (ppr.Status != PromptStatus.OK)

            {

                ed.WriteMessage("\nGetPoint failed with status: " + ppr.Status.ToString() + ". Exiting command.\n");

                return false;

            }



            Point3d clickedPointGlobal = ppr.Value;

            ed.WriteMessage($"\nClicked point (global): {clickedPointGlobal}");



            using (Transaction tr = db.TransactionManager.StartTransaction())

            {

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);



                BlockReference targetBlockRef = FindTargetBlock(clickedPointGlobal, ms, tr);



                if (targetBlockRef == null)

                {

                    ed.WriteMessage($"\nNo block references containing '{_searchString}' were found near the clicked point.");

                    tr.Abort();

                    return true;

                }



                ed.WriteMessage($"\nProcessing block: {targetBlockRef.Name} (Handle: {targetBlockRef.Handle}) - Distance from clicked point: {targetBlockRef.Position.DistanceTo(clickedPointGlobal):F2}");



                Matrix3d inverseTransform = targetBlockRef.BlockTransform.Inverse();

                Point3d clickedPointLocal = clickedPointGlobal.TransformBy(inverseTransform);

                ed.WriteMessage($"Clicked point (block local): {clickedPointLocal}");



                List<Line> blockLines = GetBlockLines(targetBlockRef, tr);



                if (blockLines.Count == 0)

                {

                    ed.WriteMessage("\nNo lines found within the target block.");

                    tr.Abort();

                    return true;

                }



                List<Point3d> surroundingPoints = FindSurroundingCellPointssForRotatedGrid(clickedPointLocal, blockLines);



                if (surroundingPoints.Count == 4)

                {

                    ed.WriteMessage("\nFour lines surrounding the clicked point were found. Creating a rectangle and applying crop:");



                    ms.UpgradeOpen();



                    List<Point3d> originalVertices = new List<Point3d>();

                    foreach (Point3d point in surroundingPoints)

                    {

                        Point3d transPoint = point.TransformBy(targetBlockRef.BlockTransform);

                        originalVertices.Add(transPoint);

                    }



                    if (originalVertices.Count == 4)

                    {

                        Autodesk.AutoCAD.DatabaseServices.Polyline originalPoly = new Autodesk.AutoCAD.DatabaseServices.Polyline();

                        originalPoly.ColorIndex = 1; // Red color

                        foreach (Point3d pt in SortPointsForPolyline(originalVertices))

                        {

                            originalPoly.AddVertexAt(originalPoly.NumberOfVertices, new Point2d(pt.X, pt.Y), 0, 0, 0);

                        }

                        originalPoly.Closed = true;

                        ms.AppendEntity(originalPoly);

                        tr.AddNewlyCreatedDBObject(originalPoly, true);

                        ed.WriteMessage($"  - Original rectangle (Polyline) created.");



                        double inflatePercentage = 0.05;

                        List<Point3d> inflatedVertices = InflateRectangle(originalVertices, inflatePercentage);



                        if (inflatedVertices.Count == 4)

                        {

                            Autodesk.AutoCAD.DatabaseServices.Polyline inflatedPoly = new Autodesk.AutoCAD.DatabaseServices.Polyline();

                            inflatedPoly.ColorIndex = 5; // Blue color

                            foreach (Point3d pt in SortPointsForPolyline(inflatedVertices))

                            {

                                inflatedPoly.AddVertexAt(inflatedPoly.NumberOfVertices, new Point2d(pt.X, pt.Y), 0, 0, 0);

                            }

                            inflatedPoly.Closed = true;

                            ms.AppendEntity(inflatedPoly);

                            tr.AddNewlyCreatedDBObject(inflatedPoly, true);

                            ed.WriteMessage($"  - Inflated rectangle (Polyline) created (blue).");



                            PointCloudEx targetPointCloud = FindPointCloudEx(ms, tr);



                            if (targetPointCloud != null)

                            {

                                double unitConversionFactor = 0.001;

                                Point3dCollection clipPoints = new Point3dCollection();

                                foreach (Point3d pnt in SortPointsForPolyline(inflatedVertices))

                                {

                                    clipPoints.Add(new Point3d(pnt.X * unitConversionFactor, pnt.Y * unitConversionFactor, pnt.Z * unitConversionFactor));

                                }



                                PointCloudCrop newCrop = null;





                                if (newCrop != null)

                                {

                                    newCrop.Vertices = clipPoints;

                                    Plane cropPlane = new Plane(clipPoints[0], Vector3d.ZAxis);

                                    newCrop.CropPlane = cropPlane;

                                    newCrop.CropType = PointCloudCropType.Polygonal;

                                    newCrop.Inside = true;

                                    newCrop.Inverted = false;



                                    try

                                    {

                                        targetPointCloud.clearCropping();

                                        targetPointCloud.addCroppingBoundary(newCrop);

                                        targetPointCloud.ShowCropped = true;

                                        ed.WriteMessage($"  - Crop applied to PointCloudEx object.");

                                    }

                                    catch (Autodesk.AutoCAD.Runtime.Exception ex)

                                    {

                                        ed.WriteMessage($"\nError: An issue occurred while applying PointCloudEx crop: {ex.Message}");

                                    }

                                }

                                else

                                {

                                    ed.WriteMessage("\nFailed to create PointCloudCrop object.");

                                }

                            }

                            else

                            {

                                ed.WriteMessage("\nNo PointCloudEx object found in ModelSpace.");

                            }

                        }

                        else

                        {

                            ed.WriteMessage("\nCould not extract 4 vertices to form the inflated rectangle.");

                        }

                    }

                    else

                    {

                        ed.WriteMessage("\nCould not extract 4 vertices to form the rectangle.");

                    }

                }

                else

                {

                    ed.WriteMessage("\nCould not find lines forming a rectangle surrounding the clicked point.");

                }



                tr.Commit();

                return true;

            }

        }



        private static void OnPointMonitor(object sender, PointMonitorEventArgs e)

        {

            ClearTransientGraphics();



            if (_editorForMonitor == null || _editorForMonitor.Document == null) return;



            Document doc = _editorForMonitor.Document;

            Database db = doc.Database;



            using (Transaction tr = db.TransactionManager.StartTransaction())

            {

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);



                Point3d currentCursorPoint = e.Context.RawPoint;



                BlockReference targetBlockRef = FindTargetBlock(currentCursorPoint, ms, tr);



                if (targetBlockRef != null)

                {

                    Matrix3d inverseTransform = targetBlockRef.BlockTransform.Inverse();

                    Point3d cursorPointLocal = currentCursorPoint.TransformBy(inverseTransform);



                    List<Line> blockLines = GetBlockLines(targetBlockRef, tr);



                    if (blockLines.Count > 0)

                    {

                        List<Point3d> surroundingPoints = new CommandCropByClick().FindSurroundingCellPointssForRotatedGrid(cursorPointLocal, blockLines);



                        if (surroundingPoints.Count == 4)

                        {

                            List<Point3d> globalVertices = new List<Point3d>();

                            foreach (Point3d point in surroundingPoints)

                            {

                                globalVertices.Add(point.TransformBy(targetBlockRef.BlockTransform));

                            }



                            List<Point3d> sortedGlobalVertices = new CommandCropByClick().SortPointsForPolyline(globalVertices);



                            Autodesk.AutoCAD.DatabaseServices.Polyline hoverPoly = new Autodesk.AutoCAD.DatabaseServices.Polyline();

                            foreach (Point3d pt in sortedGlobalVertices)

                            {

                                hoverPoly.AddVertexAt(hoverPoly.NumberOfVertices, new Point2d(pt.X, pt.Y), 0, 0, 0);

                            }

                            hoverPoly.Closed = true;



                            // Simplified color: using ACI color index directly (e.g., 3 for Green)

                            TransientMarker marker = new TransientMarker(hoverPoly, 3, true); // Green (ACI 3)

                            _transientGraphics.Add(marker);

                            marker.Draw();

                        }

                    }

                }

            }

        }



        private static void ClearTransientGraphics()

        {

            foreach (TransientMarker marker in _transientGraphics)

            {

                marker.Erase();

            }

            _transientGraphics.Clear();

            if (_editorForMonitor != null)

            {

                // Replace 'InvalidateGraphics' with 'Regen' to refresh the graphics.

                _editorForMonitor.Regen();

            }

        }



        // --- Helper Methods (unchanged) ---



        private static BlockReference FindTargetBlock(Point3d point, BlockTableRecord ms, Transaction tr)

        {

            BlockReference targetBlockRef = null;

            List<BlockReference> foundBlockRefs = new List<BlockReference>();

            foreach (ObjectId objId in ms)

            {

                if (objId.ObjectClass.Name == "AcDbBlockReference")

                {

                    BlockReference br = (BlockReference)tr.GetObject(objId, OpenMode.ForRead);

                    if (br.Name.Contains(_searchString))

                    {

                        foundBlockRefs.Add(br);

                    }

                }

            }



            if (foundBlockRefs.Count == 0) return null;



            double minDistance = double.MaxValue;

            foreach (BlockReference br in foundBlockRefs)

            {

                double distance = br.Position.DistanceTo(point);

                if (distance < minDistance)

                {

                    minDistance = distance;

                    targetBlockRef = br;

                }

            }

            return targetBlockRef;

        }



        private static List<Line> GetBlockLines(BlockReference targetBlockRef, Transaction tr)

        {

            List<Line> blockLines = new List<Line>();

            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(targetBlockRef.BlockTableRecord, OpenMode.ForRead);

            foreach (ObjectId entityId in btr)

            {

                Entity ent = (Entity)tr.GetObject(entityId, OpenMode.ForRead);

                if (ent is Line line)

                {

                    blockLines.Add(line);

                }

            }

            return blockLines;

        }



        private static PointCloudEx FindPointCloudEx(BlockTableRecord ms, Transaction tr)

        {

            foreach (ObjectId objId in ms)

            {

                if (objId.ObjectClass.Name == "AcDbPointCloudEx")

                {

                    PointCloudEx pcEx = (PointCloudEx)tr.GetObject(objId, OpenMode.ForWrite);

                    if (pcEx != null)

                    {

                        return pcEx;

                    }

                }

            }

            return null;

        }



        private List<Point3d> FindSurroundingCellPointssForRotatedGrid(Point3d clickedPointLocal, List<Line> blockLines)

        {

            double maxDistance = 10000.0;

            List<Line> candidateLines = blockLines

              .Where(l => l.GetClosestPointTo(clickedPointLocal, false).DistanceTo(clickedPointLocal) < maxDistance)

              .ToList();



            if (candidateLines.Count < 4) return new List<Point3d>();



            HashSet<Point3d> allIntersectionPoints = new HashSet<Point3d>(new Point3dComparer(0.001));



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



            List<Point3d> closestFourCorners = allIntersectionPoints

              .OrderBy(p => p.DistanceTo(clickedPointLocal))

              .Take(4)

              .ToList();



            if (closestFourCorners.Count != 4) return new List<Point3d>();



            return closestFourCorners;

        }



        private List<Point3d> SortPointsForPolyline(List<Point3d> points)

        {

            if (points.Count != 4) return points;



            Point3d centroid = Point3d.Origin;

            foreach (Point3d p in points)

            {

                centroid = centroid + (p.GetAsVector() / points.Count);

            }



            return points.OrderBy(p =>

            {

                Vector3d vec = p - centroid;

                return Math.Atan2(vec.Y, vec.X);

            }).ToList();

        }



        private List<Point3d> InflateRectangle(List<Point3d> originalVertices, double percentage)

        {

            if (originalVertices == null || originalVertices.Count != 4)

            {

                return new List<Point3d>();

            }



            Point3d centroid = Point3d.Origin;

            foreach (Point3d p in originalVertices)

            {

                centroid = centroid + (p.GetAsVector() / originalVertices.Count);

            }



            List<Point3d> inflatedVertices = new List<Point3d>();

            foreach (Point3d p in originalVertices)

            {

                Vector3d vecFromCentroid = p - centroid;

                Vector3d inflatedVec = vecFromCentroid * (1.0 + percentage);

                Point3d newP = centroid + inflatedVec;

                inflatedVertices.Add(newP);

            }

            return inflatedVertices;

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

                return p1.IsEqualTo(p2, new Tolerance(_toleranceValue, _toleranceValue));

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



    /// <summary>

    /// Helper class to manage TransientManager graphics.

    /// Simplified to use ACI color index directly.

    /// </summary>

    public class TransientMarker

    {

        private Autodesk.AutoCAD.DatabaseServices.Polyline _polyline;

        private TransientManager _transientManager;

        private Int32 _gId; // Graphics ID

        private short _colorIndex; // Changed from Autodesk.AutoCAD.Colors.Color to short

        private bool _asOverlay;

        private Autodesk.AutoCAD.DatabaseServices.Polyline hoverPoly;

        private int v1;

        private bool v2;



        public TransientMarker(Autodesk.AutoCAD.DatabaseServices.Polyline polyline, short colorIndex, bool asOverlay) // Constructor change

        {

            _polyline = polyline;

            _colorIndex = colorIndex; // Assign color index

            _asOverlay = asOverlay;

            _transientManager = TransientManager.CurrentTransientManager;

            _gId = 0;

        }



        public TransientMarker(Autodesk.AutoCAD.DatabaseServices.Polyline hoverPoly, int v1, bool v2)

        {

            this.hoverPoly = hoverPoly;

            this.v1 = v1;

            this.v2 = v2;

        }



        public void Draw()

        {

            if (_polyline != null)

            {

                IntegerCollection intCollection = new IntegerCollection();



                // Fix for CS0029: The AddTransient method returns a bool, not an int.

                // The _gId field should store a unique identifier for the transient graphics.

                // Replace the assignment with a conditional check to ensure the transient is added successfully.

                bool success = _transientManager.AddTransient(_polyline, TransientDrawingMode.Main, 0, intCollection);

                if (success)

                {

                    _gId = 1; // Assign a non-zero value to indicate success.

                }

                else

                {

                    _gId = 0; // Assign zero to indicate failure.

                }

            }

        }



        public void Erase()

        {

            if (_polyline != null && _gId != 0)

            {

                IntegerCollection viewportNumbers = new IntegerCollection(); // Create an empty IntegerCollection

                _transientManager.EraseTransient(_polyline, viewportNumbers); // Pass the IntegerCollection instead of the integer

                _gId = 0;

            }

        }

    }

}
hovering logic is not on intersection it must show selecting polygon