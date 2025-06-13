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
    public class CommandCropByClick
    {
        [CommandMethod("CROPCLICK")]
        public void RunCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Part of the logic for finding the target block
            string searchString = "通り符号"; // Example: Search for block names containing "通り符号"
            BlockReference targetBlockRef = null; // The block reference that will be processed

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Get Model Space for reading entities
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // Prompt user to click a point
                PromptPointResult ppr = ed.GetPoint("\nClick a point to identify the grid cell: ");
                if (ppr.Status != PromptStatus.OK)
                {
                    return;
                }
                Point3d clickedPointGlobal = ppr.Value;
                ed.WriteMessage($"\nClicked Point (Global): {clickedPointGlobal}");

                // Find all block references matching the search string
                List<BlockReference> foundBlockRefs = new List<BlockReference>();
                foreach (ObjectId objId in ms)
                {
                    if (objId.ObjectClass.Name == "AcDbBlockReference")
                    {
                        BlockReference br = (BlockReference)tr.GetObject(objId, OpenMode.ForRead);
                        if (br.Name.Contains(searchString))
                        {
                            foundBlockRefs.Add(br);
                        }
                    }
                }

                if (foundBlockRefs.Count == 0)
                {
                    ed.WriteMessage($"\nNo block references containing '{searchString}' were found.");
                    return;
                }

                // Find the closest block reference to the clicked point
                double minDistance = double.MaxValue;
                foreach (BlockReference br in foundBlockRefs)
                {
                    double distance = br.Position.DistanceTo(clickedPointGlobal);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        targetBlockRef = br;
                    }
                }

                if (targetBlockRef == null)
                {
                    ed.WriteMessage($"\nBlock references containing '{searchString}' were found, but the closest one could not be determined.");
                    return;
                }

                ed.WriteMessage($"\nTarget Block for Processing: {targetBlockRef.Name} (Handle: {targetBlockRef.Handle})");

                // Convert clicked point to the target block's local coordinate system
                Matrix3d inverseTransform = targetBlockRef.BlockTransform.Inverse();
                Point3d clickedPointLocal = clickedPointGlobal.TransformBy(inverseTransform);
                ed.WriteMessage($"Clicked Point (Block Local): {clickedPointLocal}");

                List<Point3d> originalRectangleVertices = new List<Point3d>();

                // --- YOUR NEW BOUNDARY BOX FINDING LOGIC GOES HERE ---
                // This is where you will implement your algorithm to find the 4 vertices
                // of the boundary box based on 'clickedPointLocal' and the entities
                // within 'targetBlockRef.BlockTableRecord'.

                // Example placeholder (replace with your actual logic):
                // You will typically need to iterate through the entities in the block definition
                // BlockTableRecord btr = (BlockTableRecord)tr.GetObject(targetBlockRef.BlockTableRecord, OpenMode.ForRead);
                // foreach (ObjectId entityId in btr)
                // {
                //     Entity ent = (Entity)tr.GetObject(entityId, OpenMode.ForRead);
                //     // Analyze 'ent' to determine the boundaries based on 'clickedPointLocal'
                //     // For example, if your grid is made of lines, you'd find the lines
                //     // that form the cell around clickedPointLocal and derive the 4 corners.
                // }

                // For now, let's assume you've calculated these 4 points in GLOBAL coordinates
                // and stored them in 'originalRectangleVertices'.
                // If your algorithm finds them in LOCAL coordinates, make sure to transform them to GLOBAL:
                // originalRectangleVertices.Add(localPoint.TransformBy(targetBlockRef.BlockTransform));

                // --- Placeholder for an example of how you might *mock* finding vertices ---
                // For demonstration, let's just make a simple rectangle for now.
                // In your actual implementation, these points would come from your algorithm.
                // Assuming clickedPointGlobal is within the desired cell.
                double mockGridSize = 1000.0; // Assume a grid cell is 1000x1000 units for this mock
                originalRectangleVertices.Add(new Point3d(clickedPointGlobal.X - mockGridSize / 2, clickedPointGlobal.Y - mockGridSize / 2, 0));
                originalRectangleVertices.Add(new Point3d(clickedPointGlobal.X + mockGridSize / 2, clickedPointGlobal.Y - mockGridSize / 2, 0));
                originalRectangleVertices.Add(new Point3d(clickedPointGlobal.X + mockGridSize / 2, clickedPointGlobal.Y + mockGridSize / 2, 0));
                originalRectangleVertices.Add(new Point3d(clickedPointGlobal.X - mockGridSize / 2, clickedPointGlobal.Y + mockGridSize / 2, 0));
                // --- END OF MOCK/PLACEHOLDER ---

                if (originalRectangleVertices.Count == 4)
                {
                    ed.WriteMessage("\nDetected 4 vertices for the crop boundary.");

                    // Upgrade Model Space to write mode for adding new entities (polylines)
                    ms.UpgradeOpen();

                    // 1. Draw the original detected boundary in red
                    Polyline originalPoly = new Polyline();
                    originalPoly.ColorIndex = 1; // Red
                    foreach (Point3d pt in SortPointsForPolyline(originalRectangleVertices))
                    {
                        originalPoly.AddVertexAt(originalPoly.NumberOfVertices, new Point2d(pt.X, pt.Y), 0, 0, 0);
                    }
                    originalPoly.Closed = true;
                    ms.AppendEntity(originalPoly);
                    tr.AddNewlyCreatedDBObject(originalPoly, true);
                    ed.WriteMessage($" - Original boundary (Polyline) created.");

                    // 2. Inflate the boundary by a certain percentage
                    double inflatePercentage = 0.05; // 5% inflation
                    List<Point3d> inflatedVertices = InflateRectangle(originalRectangleVertices, inflatePercentage);

                    // Draw the inflated boundary in blue
                    if (inflatedVertices.Count == 4)
                    {
                        Polyline inflatedPoly = new Polyline();
                        inflatedPoly.ColorIndex = 5; // Blue
                        foreach (Point3d pt in SortPointsForPolyline(inflatedVertices))
                        {
                            inflatedPoly.AddVertexAt(inflatedPoly.NumberOfVertices, new Point2d(pt.X, pt.Y), 0, 0, 0);
                        }
                        inflatedPoly.Closed = true;
                        ms.AppendEntity(inflatedPoly);
                        tr.AddNewlyCreatedDBObject(inflatedPoly, true);
                        ed.WriteMessage($" - Inflated boundary (Polyline) created (blue).");

                        // --- PointCloudEx Cropping Logic ---
                        PointCloudEx targetPointCloud = null;
                        foreach (ObjectId objId in ms)
                        {
                            if (objId.ObjectClass.Name == "AcDbPointCloudEx") // Class name for PointCloudEx
                            {
                                targetPointCloud = (PointCloudEx)tr.GetObject(objId, OpenMode.ForWrite); // Open in write mode
                                if (targetPointCloud != null)
                                {
                                    ed.WriteMessage($"\nPointCloudEx object '{targetPointCloud.ActiveFileName}' found. Applying crop...");
                                    break; // Use the first PointCloudEx found
                                }
                            }
                        }

                        if (targetPointCloud != null)
                        {
                            // Define 3D clip volume height (adjust as needed to cover your point cloud's Z range)
                            double clipVolumeHeight = 50000.0;
                            Vector3d zAxis = Vector3d.ZAxis;

                            // Scale points for PointCloudEx API if necessary (e.g., mm to meters)
                            // This scaling (0.001) is common if your drawing units are mm and PointCloudEx expects meters.
                            // Adjust this based on your specific unit setup.
                            Point3d pnt1_base = inflatedVertices[0].ScaleBy(0.001, Point3d.Origin);
                            Point3d pnt2_base = inflatedVertices[1].ScaleBy(0.001, Point3d.Origin);
                            Point3d pnt3_base = inflatedVertices[2].ScaleBy(0.001, Point3d.Origin);
                            Point3d pnt4_base = inflatedVertices[3].ScaleBy(0.001, Point3d.Origin);

                            Point3dCollection clipPoints = new Point3dCollection();
                            // Vertices define the 2D polygon for cropping
                            clipPoints.Add(pnt1_base);
                            clipPoints.Add(pnt2_base);
                            clipPoints.Add(pnt3_base);
                            clipPoints.Add(pnt4_base);
                            clipPoints.Add(pnt1_base); // Close the polygon

                            PointCloudCrop newCrop = PointCloudCrop.Create(IntPtr.Zero);
                            if (newCrop != null)
                            {
                                newCrop.Vertices = clipPoints;
                                // The crop plane dictates the "floor" or "base" of the 3D cropping volume
                                Plane cropPlane = new Plane(pnt1_base, zAxis);
                                newCrop.CropPlane = cropPlane;

                                newCrop.CropType = PointCloudCropType.Polygonal; // Crop by polygon shape
                                newCrop.Inside = true;                         // Keep points inside the boundary
                                newCrop.Inverted = false;                      // Do not invert the crop region

                                try
                                {
                                    targetPointCloud.clearCropping();          // Clear any existing crops
                                    targetPointCloud.addCroppingBoundary(newCrop); // Add the new crop
                                    targetPointCloud.ShowCropped = true;       // Make the crop visible

                                    ed.WriteMessage($" - Crop successfully applied to PointCloudEx object.");
                                }
                                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                                {
                                    ed.WriteMessage($"\nError during PointCloudEx cropping: {ex.Message}");
                                    ed.WriteMessage($" Please check your AutoCAD SDK version and PointCloudEx API usage.");
                                }
                            }
                            else
                            {
                                ed.WriteMessage("\nFailed to create PointCloudCrop object.");
                            }
                        }
                        else
                        {
                            ed.WriteMessage("\nNo PointCloudEx object found in Model Space.");
                        }
                    }
                    else
                    {
                        ed.WriteMessage("\nFailed to form a 4-vertex rectangle for inflation.");
                    }
                }
                else
                {
                    ed.WriteMessage("\nCould not extract 4 vertices for the crop boundary. Check your boundary finding logic.");
                }

                // Commit the transaction to save changes to the database
                tr.Commit();
            }
        }

        /// <summary>
        /// Sorts 4 points for correct Polyline creation, typically by angle around their centroid.
        /// This ensures the polyline draws a closed shape without crossing itself.
        /// </summary>
        /// <param name="points">The 4 points to sort (e.g., corners of a rectangle).</param>
        /// <returns>The sorted list of points.</returns>
        private List<Point3d> SortPointsForPolyline(List<Point3d> points)
        {
            if (points.Count != 4) return points;

            // Calculate the centroid of the points
            Point3d centroid = Point3d.Origin;
            foreach (Point3d p in points)
            {
                centroid = centroid + (p.GetAsVector() / points.Count);
            }

            // Sort points by their angle relative to the centroid and the X-axis
            return points.OrderBy(p =>
            {
                Vector3d vec = p - centroid;
                return Math.Atan2(vec.Y, vec.X);
            }).ToList();
        }

        /// <summary>
        /// Inflates a rectangle's vertices by a given percentage away from its centroid.
        /// Useful for creating a slightly larger bounding box for cropping or other operations.
        /// </summary>
        /// <param name="originalVertices">The original rectangle vertices (expected to be 4 points).</param>
        /// <param name="percentage">The percentage to inflate (e.g., 0.05 for 5% inflation).</param>
        /// <returns>The inflated rectangle vertices.</returns>
        private List<Point3d> InflateRectangle(List<Point3d> originalVertices, double percentage)
        {
            if (originalVertices == null || originalVertices.Count != 4)
            {
                return new List<Point3d>();
            }

            // Calculate the centroid of the original rectangle
            Point3d centroid = Point3d.Origin;
            foreach (Point3d p in originalVertices)
            {
                centroid = centroid + (p.GetAsVector() / originalVertices.Count);
            }

            List<Point3d> inflatedVertices = new List<Point3d>();
            foreach (Point3d p in originalVertices)
            {
                // Calculate vector from centroid to current vertex
                Vector3d vecFromCentroid = p - centroid;

                // Scale the vector by (1 + percentage) to move the vertex further from the centroid
                Vector3d inflatedVec = vecFromCentroid * (1.0 + percentage);

                // Calculate the new inflated vertex position
                Point3d newP = centroid + inflatedVec;
                inflatedVertices.Add(newP);
            }

            return inflatedVertices;
        }

        /// <summary>
        /// IEqualityComparer implementation for Point3d objects, considering a tolerance for equality.
        /// This is crucial when working with floating-point coordinates in CAD to correctly identify
        /// coincident points (e.g., in HashSets to remove duplicates).
        /// </summary>
        private class Point3dComparer : IEqualityComparer<Point3d>
        {
            private readonly double _toleranceValue;

            /// <summary>
            /// Initializes a new instance of the Point3dComparer class with a specified tolerance.
            /// </summary>
            /// <param name="toleranceValue">The tolerance value to use for comparing coordinates.</param>
            public Point3dComparer(double toleranceValue)
            {
                _toleranceValue = toleranceValue;
            }

            /// <summary>
            /// Determines whether two Point3d objects are equal, considering the specified tolerance.
            /// </summary>
            /// <param name="p1">The first Point3d to compare.</param>
            /// <param name="p2">The second Point3d to compare.</param>
            /// <returns>True if the points are equal within the tolerance; otherwise, false.</returns>
            public bool Equals(Point3d p1, Point3d p2)
            {
                return p1.IsEqualTo(p2, new Tolerance(_toleranceValue, _toleranceValue));
            }

            /// <summary>
            /// Returns a hash code for the specified Point3d object.
            /// The hash code is generated by rounding coordinates to multiples of the tolerance,
            /// which helps group points that are considered equal by the Equals method.
            /// </summary>
            /// <param name="p">The Point3d for which to get a hash code.</param>
            /// <returns>A hash code for the specified Point3d.</returns>
            public int GetHashCode(Point3d p)
            {
                int xHash = Math.Round(p.X / _toleranceValue).GetHashCode();
                int yHash = Math.Round(p.Y / _toleranceValue).GetHashCode();
                int zHash = Math.Round(p.Z / _toleranceValue).GetHashCode();
                // Combine hash codes using XOR for a good distribution
                return xHash ^ yHash ^ zHash;
            }
        }
    }
}