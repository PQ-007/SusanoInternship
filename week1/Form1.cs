using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace week1
{
    public partial class Form1 : Form
    {
        private int dotNum = 0;
        List<Point> convexHullPoints;
        private Point[] resultPoints;
        private List<Point> dotCoordinates;
        private Random random = new Random();
        private int rectX, rectY, rectWidth, rectHeight;
        private bool show1 = false;
        private bool show2 = false;
        private bool show3 = false;
        private bool dotPainted = true;

        public Form1()
        {
            InitializeComponent();
            dotCoordinates = new List<Point>();
            convexHullPoints = new List<Point>();
        }
        private void Form1_Load(object sender, EventArgs e)
        {
        }
        private void random_Click(object sender, EventArgs e)
        {
            dotCoordinates.Clear();
            convexHullPoints.Clear();

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
                dotPainted = false;
            }
            else if(dotCoordinates.Count() > 0)
            {   
                
                MessageBox.Show("Please enter a number greater than 0 for dots.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            this.Invalidate();
        }
        private void minRect1_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("minRect1 button clicked.");
            show2 = false;
            show3 = false;

            if (dotCoordinates.Any())
            {
                int minX = dotCoordinates.Min(p => p.X);
                int minY = dotCoordinates.Min(p => p.Y);
                int maxX = dotCoordinates.Max(p => p.X);
                int maxY = dotCoordinates.Max(p => p.Y);

                rectWidth = maxX - minX;
                rectHeight = maxY - minY;

                rectX = minX;
                rectY = minY;

                show1 = true;
                Debug.WriteLine($"Min X: {minX}, Min Y: {minY}, Width: {rectWidth}, Height: {rectHeight}");

                Invalidate();
            }
        }
        private void minRect2_Click(object sender, EventArgs e)
        {
            show1 = false;
            show3 = false;
            if (dotCoordinates.Any())
            {
                var result = ConvexHull.FindMinimumEnclosingRectangle(dotCoordinates);
                resultPoints = result.RectanglePoints;
                Debug.WriteLine("Minimum Enclosing Rectangle Points:");
                foreach (Point dot in convexHullPoints)
                {
                    Debug.WriteLine(dot.X + "," + dot.Y);
                }

                double minArea = result.Area;
                Debug.WriteLine($"Minimum enclosing rectangle area: {minArea}");

                show2 = true;

                Invalidate();
            }
        }
        private void minPolygon_Click(object sender, EventArgs e)
        {   show1 = false;
            show2 = false;
            if (convexHullPoints.Any())
            {
                Debug.WriteLine("minPolygon button clicked.");
                convexHullPoints = ConvexHull.FindConvexHull(dotCoordinates);
                show3 = true;
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

                if (dotCoordinates != null && dotCoordinates.Any() && !dotPainted)
                {
                    using (SolidBrush brush = new SolidBrush(Color.Blue))
                    {
                        foreach (Point p in dotCoordinates)
                        {
                            g.FillEllipse(brush, p.X-5, p.Y-5, 10, 10);
                        }
                        dotPainted = true;
                    }
                }

                if (show1)
                {
                    using (Pen rectPen = new Pen(Color.Red))
                    {
                        g.DrawRectangle(rectPen, rectX, rectY, rectWidth, rectHeight);
                    }
                }

                if (show2 && resultPoints.Length == 4)
                {
                    using (Pen pen = new Pen(Color.Green, 2))
                    {
                        g.DrawPolygon(pen, resultPoints);
                    }
                }
                if (show3)
                {
                    
                    using (Pen pen = new Pen(Color.Purple, 3))
                    {

                        for(int i = 0; i < convexHullPoints.Count() - 1; i++)
                        {
                            g.DrawLine(pen, convexHullPoints[i].X, convexHullPoints[i].Y, convexHullPoints[i + 1].X, convexHullPoints[i + 1].Y);
                        }
                        g.DrawLine(pen, convexHullPoints[0].X, convexHullPoints[0].Y, convexHullPoints.Last().X, convexHullPoints.Last().Y);

                    }
                }
            }
        }

        public static class ConvexHull
        {
            public struct MERResult
            {
                public Point[] RectanglePoints;
                public double Area;
            }

            public static List<Point> FindConvexHull(List<Point> points)
            {
                if (points.Count <= 2) return points;

                points.Sort((a, b) => {
                    int cmp = a.X.CompareTo(b.X);
                    return cmp != 0 ? cmp : a.Y.CompareTo(b.Y);
                });

                List<Point> lower = new List<Point>();
                foreach (var point in points)
                {
                    while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], point) <= 0)
                        lower.RemoveAt(lower.Count - 1);
                    lower.Add(point);
                }

                List<Point> upper = new List<Point>();
                for (int i = points.Count - 1; i >= 0; i--)
                {
                    var point = points[i];
                    while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], point) <= 0)
                        upper.RemoveAt(upper.Count - 1);
                    upper.Add(point);
                }

                lower.RemoveAt(lower.Count - 1);
                upper.RemoveAt(upper.Count - 1);

                lower.AddRange(upper);
                return lower;
            }

            private static double Cross(Point o, Point a, Point b)
            {
                return (double)(a.X - o.X) * (b.Y - o.Y) - (double)(a.Y - o.Y) * (b.X - o.X);
            }

            private static double DistSq(Point p1, Point p2)
            {
                return (double)(p1.X - p2.X) * (p1.X - p2.X) + (double)(p1.Y - p2.Y) * (p1.Y - p2.Y);
            }

            public static MERResult FindMinimumEnclosingRectangle(List<Point> points)
            {
                if (points == null || points.Count == 0)
                {
                    return new MERResult { RectanglePoints = new Point[0], Area = 0 };
                }

                List<Point> hull = FindConvexHull(points);

                if (hull.Count <= 1)
                {
                    return new MERResult { RectanglePoints = new Point[0], Area = 0 };
                }
                else if (hull.Count == 2)
                {
                    Point p1 = hull[0];
                    Point p2 = hull[1];
                    int thickness = 1;
                    Point[] rectPoints = new Point[4];
                    double angle = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
                    double cosAngle = Math.Cos(angle);
                    double sinAngle = Math.Sin(angle);

                    double offsetX = -thickness * sinAngle;
                    double offsetY = thickness * cosAngle;

                    rectPoints[0] = new Point((int)(p1.X - offsetX), (int)(p1.Y - offsetY));
                    rectPoints[1] = new Point((int)(p2.X - offsetX), (int)(p2.Y - offsetY));
                    rectPoints[2] = new Point((int)(p2.X + offsetX), (int)(p2.Y + offsetY));
                    rectPoints[3] = new Point((int)(p1.X + offsetX), (int)(p1.Y + offsetY));

                    return new MERResult { RectanglePoints = rectPoints, Area = DistSq(p1, p2) * thickness };
                }

                double minArea = double.MaxValue;
                Point[] minRectPoints = new Point[4];

                for (int hIdx = 0; hIdx < hull.Count; hIdx++)
                {
                    Point p1 = hull[hIdx];
                    Point p2 = hull[(hIdx + 1) % hull.Count];

                    double edgeDx = p2.X - p1.X;
                    double edgeDy = p2.Y - p1.Y;

                    double edgeLength = Math.Sqrt(edgeDx * edgeDx + edgeDy * edgeDy);
                    if (edgeLength == 0) continue;

                    double unitDx = edgeDx / edgeLength;
                    double unitDy = edgeDy / edgeLength;

                    double perpDx = -unitDy;
                    double perpDy = unitDx;

                    double currentMinProjEdge = double.MaxValue;
                    double currentMaxProjEdge = double.MinValue;
                    double currentMinProjPerp = double.MaxValue;
                    double currentMaxProjPerp = double.MinValue;

                    foreach (Point hp in hull)
                    {
                        double projEdge = (hp.X - p1.X) * unitDx + (hp.Y - p1.Y) * unitDy;
                        currentMinProjEdge = Math.Min(currentMinProjEdge, projEdge);
                        currentMaxProjEdge = Math.Max(currentMaxProjEdge, projEdge);

                        double projPerp = (hp.X - p1.X) * perpDx + (hp.Y - p1.Y) * perpDy;
                        currentMinProjPerp = Math.Min(currentMinProjPerp, projPerp);
                        currentMaxProjPerp = Math.Max(currentMaxProjPerp, projPerp);
                    }

                    double width = currentMaxProjEdge - currentMinProjEdge;
                    double height = currentMaxProjPerp - currentMinProjPerp;
                    double currentArea = width * height;

                    if (currentArea < minArea)
                    {
                        minArea = currentArea;

                        Point pMinEdgeMinPerp = new Point(
                            (int)(p1.X + currentMinProjEdge * unitDx + currentMinProjPerp * perpDx),
                            (int)(p1.Y + currentMinProjEdge * unitDy + currentMinProjPerp * perpDy)
                        );
                        Point pMaxEdgeMinPerp = new Point(
                            (int)(p1.X + currentMaxProjEdge * unitDx + currentMinProjPerp * perpDx),
                            (int)(p1.Y + currentMaxProjEdge * unitDy + currentMinProjPerp * perpDy)
                        );
                        Point pMaxEdgeMaxPerp = new Point(
                            (int)(p1.X + currentMaxProjEdge * unitDx + currentMaxProjPerp * perpDx),
                            (int)(p1.Y + currentMaxProjEdge * unitDy + currentMaxProjPerp * perpDy)
                        );
                        Point pMinEdgeMaxPerp = new Point(
                            (int)(p1.X + currentMinProjEdge * unitDx + currentMaxProjPerp * perpDx),
                            (int)(p1.Y + currentMinProjEdge * unitDy + currentMaxProjPerp * perpDy)
                        );

                        minRectPoints[0] = pMinEdgeMinPerp;
                        minRectPoints[1] = pMaxEdgeMinPerp;
                        minRectPoints[2] = pMaxEdgeMaxPerp;
                        minRectPoints[3] = pMinEdgeMaxPerp;
                    }
                }

                return new MERResult
                {
                    Area = minArea,
                    RectanglePoints = minRectPoints
                };
            }
        }
    }
}