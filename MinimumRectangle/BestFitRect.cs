using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;

namespace week1
{
    public partial class Form1 : Form
    {
        private int dotNum = 0;
        private Point[] convexHullPoints;
        private List<Point> dotCoordinates;
        private Random random = new Random();
        private int rectX, rectY, rectWidth, rectHeight;
        private bool showRectangle = false;
        private bool showRectangle2 = false;

        public Form1()
        {
            InitializeComponent();
            dotCoordinates = new List<Point>();
        }
        private void Form1_Load(object sender, EventArgs e)
        {
        }
        private void random_Click(object sender, EventArgs e)
        {
            dotCoordinates.Clear();

            if (dotNum > 0)
            {
                for (int i = 0; i < dotNum; i++)
                {
                    int x = random.Next(45, 500);
                    int y = random.Next(60, 380);
                    Debug.WriteLine($"x: {x}, y: {y}");
                    dotCoordinates.Add(new Point(x, y));
                }
                Debug.WriteLine($"Generated {dotCoordinates.Count} dots.");
            }
            else
            {
                MessageBox.Show("Please enter a number greater than 0 for dots.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            this.Invalidate();
        }
        private void minRect1_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("minRect1 button clicked.");

            if (dotCoordinates.Any())
            {
                int minX = dotCoordinates.Min(p => p.X);
                int minY = dotCoordinates.Min(p => p.Y);
                int maxX = dotCoordinates.Max(p => p.X);
                int maxY = dotCoordinates.Max(p => p.Y);

                rectWidth = maxX - minX;
                rectHeight = maxY - minY;

                rectX = minX + 5;
                rectY = minY + 5;

                showRectangle = true;
                Debug.WriteLine($"Min X: {minX}, Min Y: {minY}, Width: {rectWidth}, Height: {rectHeight}");

                Invalidate();
            }
        }
        private void minRect2_Click(object sender, EventArgs e)
        {
            if (dotCoordinates.Any())
            {
                var result = ConvexHull.FindMinimumEnclosingRectangle(dotCoordinates);
                convexHullPoints = result.RectanglePoints;
                foreach (Point dot in convexHullPoints)
                {
                    Debug.WriteLine(dot.X + "," + dot.Y);
                }

                double minArea = result.Area;
                Debug.WriteLine($"Minimum enclosing rectangle area: {minArea}");

                
                showRectangle2 = true;

               
                Invalidate();
            }
        }
        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            string text = textBox1.Text.Trim();

            if (Int32.TryParse(text, out int parsedNum))
            {
                dotNum = parsedNum;
                Debug.WriteLine($"dotNum set to: {dotNum}");
            }
            else
            {
                dotNum = 0;
                Debug.WriteLine("Invalid input in textBox1. dotNum reset to 0.");
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Graphics g = e.Graphics)
            {

                using (Pen p = new Pen(Color.Gray))
                {
                    g.DrawLine(p, 35, 60, 550, 60);
                    g.DrawLine(p, 35, 60, 35, 400);
                    g.DrawLine(p, 550, 60, 550, 400);
                    g.DrawLine(p, 550, 400, 35, 400);
                }

           
                if (dotCoordinates != null && dotCoordinates.Any())
                {
                    using (SolidBrush brush = new SolidBrush(Color.Blue))
                    {
                        foreach (Point p in dotCoordinates)
                        {
                            g.FillEllipse(brush, p.X, p.Y, 10, 10);
                        }
                    }
                }

                if (showRectangle)
                {
                    using (Pen rectPen = new Pen(Color.Red))
                    {
                        g.DrawRectangle(rectPen, rectX, rectY, rectWidth, rectHeight);
                    }
                }

            
                if (showRectangle2)
                {
                    using (Pen pen = new Pen(Color.Green))
                    {

                        for (int i = 0; i < convexHullPoints.Count() - 1; i++)
                        {
                            g.DrawLine(pen, convexHullPoints[i].X + 5, convexHullPoints[i].Y + 5, convexHullPoints[i + 1].X + 5, convexHullPoints[i + 1].Y + 5);
                        }
                        g.DrawLine(pen, convexHullPoints[0].X + 5, convexHullPoints[0].Y + 5, convexHullPoints.Last().X + 5, convexHullPoints.Last().Y + 5);
                    }
                }
            }
        }

        // --- ConvexHullAndMER Class ---
        public static class ConvexHull
        {
            public struct MERResult
            {
                public Point[] RectanglePoints;
                public double Area;
            }
            public static List<Point> FindConvexHull(List<Point> points)
            {
                // Sort the points lexicographically (by X and then Y)
                points.Sort((a, b) => a.X == b.X ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

                // Build the lower hull
                List<Point> lower = new List<Point>();
                foreach (var point in points)
                {
                    while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], point) <= 0)
                        lower.RemoveAt(lower.Count - 1);
                    lower.Add(point);
                }

                // Build the upper hull
                List<Point> upper = new List<Point>();
                for (int i = points.Count - 1; i >= 0; i--)
                {
                    while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], points[i]) <= 0)
                        upper.RemoveAt(upper.Count - 1);
                    upper.Add(points[i]);
                }

                // Remove the last point because it's repeated at the beginning
                lower.RemoveAt(lower.Count - 1);
                upper.RemoveAt(upper.Count - 1);

                // Combine the lower and upper hulls to get the full convex hull
                lower.AddRange(upper);
                return lower;
            }

            private static int Cross(Point o, Point a, Point b)
            {
                return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            }

            // Function to calculate the area of the bounding box given two perpendicular vectors
            private static double CalculateBoundingBoxArea(Point[] hull)
            {
                int n = hull.Length;
                double minArea = double.MaxValue;

                // Rotate calipers
                for (int i = 0; i < n; i++)
                {
                    Point p1 = hull[i];
                    Point p2 = hull[(i + 1) % n];

                    // Direction of the edge
                    double dx = p2.X - p1.X;
                    double dy = p2.Y - p1.Y;

                    // Rotate the hull to align with this edge
                    double minX = double.MaxValue, maxX = double.MinValue;
                    double minY = double.MaxValue, maxY = double.MinValue;

                    // Calculate the bounding box in this orientation
                    for (int j = 0; j < n; j++)
                    {
                        double projX = (hull[j].X - p1.X) * dx + (hull[j].Y - p1.Y) * dy;
                        double projY = (hull[j].X - p1.X) * -dy + (hull[j].Y - p1.Y) * dx;

                        minX = Math.Min(minX, projX);
                        maxX = Math.Max(maxX, projX);
                        minY = Math.Min(minY, projY);
                        maxY = Math.Max(maxY, projY);
                    }

                    double width = maxX - minX;
                    double height = maxY - minY;
                    double area = width * height;

                    // Track the smallest area found
                    minArea = Math.Min(minArea, area);
                }

                return minArea;
            }

            public static MERResult FindMinimumEnclosingRectangle(List<Point> points)
            {
                // Step 1: Compute the convex hull
                List<Point> hull = FindConvexHull(points);

                // Step 2: Apply rotating calipers to find the minimum enclosing rectangle
                double minArea = CalculateBoundingBoxArea(hull.ToArray());

                // Step 3: Return the result
                return new MERResult
                {
                    Area = minArea,
                    RectanglePoints = hull.ToArray()  // We return the convex hull for simplicity (you can extract specific rectangle points)
                };
            }
        }
    }
}
