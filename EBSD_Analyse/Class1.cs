using System;
using System.Linq;
using OpenTK.Mathematics;

namespace EBSD_Analyse
{
    public class Analyzer
    {
        public EBSD_Point[,] Ebsd_points;

        public Analyzer() { }
        public Analyzer(EBSD_Point[,] ebsd_Points)
        {
            Ebsd_points = ebsd_Points;
        }

        public Color[,] GetColorMap(MapVariants mapVariant)
        {
            switch (mapVariant)
            {
                case MapVariants.BC: return GetBC_Map();
                case MapVariants.Euler: return GetEuler_Map();
                default: return null;
            }
        }

        private Color[,] GetBC_Map()
        {
            int width = Ebsd_points.GetLength(0);
            int height = Ebsd_points.GetLength(1);

            Color[,] colors = new Color[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    EBSD_Point point = Ebsd_points[x, y];
                    //colors[x, y] = new Color((int)(255 * point.Ph1 / 360d),
                    //                         (int)(255 * point.Ph2 / 90d),
                    //                         (int)(255 * point.Ph3 / 90d));
                    colors[x, y] = new Color(point.BC, point.BC, point.BC);
                }
            }
            return colors;
        }
        private Color[,] GetEuler_Map()
        {
            int width = Ebsd_points.GetLength(0);
            int height = Ebsd_points.GetLength(1);

            Color[,] colors = new Color[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    EBSD_Point point = Ebsd_points[x, y];
                    colors[x, y] = new Color((int)(255 * point.Ph1 / 360d),
                                             (int)(255 * point.Ph2 / 90d),
                                             (int)(255 * point.Ph3 / 90d));
                }
            }
            return colors;
        }
    }

    public enum MapVariants
    {
        BC, Euler,
    }

    public struct EBSD_Point
    {
        public double X, Y, Ph1, Ph2, Ph3, MAD;
        public int BC;

        public EBSD_Point(double x, double y, double ph1, double ph2, double ph3, double mad, int bc)
        {
            X = x; Y = y; Ph1 = ph1; Ph2 = ph2; Ph3 = ph3; MAD = mad; BC = bc;
        }
    }

    public struct Color
    {
        public int R, G, B;
        public Color(int r, int g, int b)
        {
            R = Math.Clamp(r, 0, 255);
            G = Math.Clamp(g, 0, 255);
            B = Math.Clamp(b, 0, 255);
        }
    }

}