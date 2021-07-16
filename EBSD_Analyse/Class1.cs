using System;
using System.Linq;
using OpenTK.Mathematics;

namespace EBSD_Analyse
{
    public class Analyzer
    {
        public const float ScanStep = 0.05f;

        public EBSD_Point[,] Ebsd_points;

        public Analyzer() { }
        public Analyzer(EBSD_Point[,] ebsd_Points)
        {
            Ebsd_points = ebsd_Points;
        }

        public Color[,] GetColors()
        {
            int width = Ebsd_points.GetLength(0);
            int height = Ebsd_points.GetLength(1);

            Color[,] colors = new Color[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    EBSD_Point point = Ebsd_points[x, y];
                    colors[x, y] = new Color(Math.Clamp((int)(255 * point.Ph1 / 360d), 0, 255),
                                             Math.Clamp((int)(255 * point.Ph2 / 360d), 0, 255),
                                             Math.Clamp((int)(255 * point.Ph3 / 360d), 0, 255));
                }
            }
            return colors;
        }
    }


    public struct EBSD_Point
    {
        public double X, Y, Ph1, Ph2, Ph3, MAD;

        public EBSD_Point(double x, double y, double ph1, double ph2, double ph3, double mad)
        {
            X = x; Y = y; Ph1 = ph1; Ph2 = ph2; Ph3 = ph3; MAD = mad;
        }
    }

    public struct Color
    {
        public int R, G, B;
        public Color(int r, int g, int b)
        {
            R = r; G = g; B = b;
        }
    }
}