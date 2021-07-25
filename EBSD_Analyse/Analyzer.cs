using Cloo;
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace EBSD_Analyse
{
    public class Analyzer
    {

        public static Color[,] Work()
        {
            //Установка параметров, инициализирующих видеокарты при работе. В Platforms[1] должен стоять индекс
            //указывающий на используемую платформу
            ComputeContextPropertyList Properties = new ComputeContextPropertyList(ComputePlatform.Platforms[0]);
            ComputeContext Context = new ComputeContext(ComputeDeviceTypes.All, Properties, null, IntPtr.Zero);

            //Текст програмы, исполняющейся на устройстве (GPU или CPU). Именно эта программа будет выполнять паралельные
            //вычисления и будет складывать вектора. Программа написанна на языке, основанном на C99 специально под OpenCL.
            string shaderProgram = @"

                            __kernel void Euler2Color(__read_only image2d_t in, __write_only image2d_t out)
                            {
                                const sampler_t smp = CLK_NORMALIZED_COORDS_FALSE | //Natural coordinates
                                                      CLK_ADDRESS_CLAMP | //Clamp to zeros
                                                      CLK_FILTER_NEAREST; //Don't interpolate

                                int2 coord = (int2)(get_global_id(0), get_global_id(1)); 


                                int4 col = convert_int4(read_imagef (in, smp, coord));

                                write_imagei (out, coord, col);
                            }

                            ";

            //Список устройств, для которых мы будем компилировать написанную в vecSum программу
            List<ComputeDevice> Devices = new List<ComputeDevice>();
            Devices.Add(ComputePlatform.Platforms[0].Devices[0]);

            //Компиляция программы
            ComputeProgram program = null;
            try
            {
                program = new ComputeProgram(Context, shaderProgram);
                program.Build(Devices, "", null, IntPtr.Zero);
            }
            catch
            { }

            //Инициализация новой программы
            ComputeKernel kernel = program.CreateKernel("Euler2Color");

            //Создание програмной очереди. Не забудте указать устройство, на котором будет исполняться программа!
            ComputeCommandQueue Queue = new ComputeCommandQueue(Context, Cloo.ComputePlatform.Platforms[0].Devices[0], Cloo.ComputeCommandQueueFlags.None);

            //Инициализация массивов
            ComputeImage2D pointsImg;
            ComputeImage2D colorsImg;

            float[] points = new float[400]; // input
            Color[,] colors = new Color[10,10]; // output

            for (int i = 0; i < points.Length - 2; i += 3)
            {
                points[i] = 100f;
                points[i + 1] = 120f;
                points[i + 2] = 150f;

            }

            unsafe
            {
                fixed (float* imgPtr = points)
                {
                    ComputeImageFormat inputFormat = new ComputeImageFormat(ComputeImageChannelOrder.Rgba, ComputeImageChannelType.Float);
                    ComputeImageFormat outputFormat = new ComputeImageFormat(ComputeImageChannelOrder.Rgba, ComputeImageChannelType.SignedInt32);

                    pointsImg = new ComputeImage2D(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, inputFormat, 10, 10, 10 * 4 * sizeof(float), (IntPtr)imgPtr);

                    colorsImg = new ComputeImage2D(Context, ComputeMemoryFlags.ReadWrite, outputFormat, 10, 10, 0, IntPtr.Zero);
                }

                kernel.SetMemoryArgument(0, pointsImg);
                kernel.SetMemoryArgument(1, colorsImg);

                Queue.Execute(kernel, null, new long[] { 10, 10 }, null, null);


                fixed (Color* imgPtr = colors)
                {
                    Queue.ReadFromImage(colorsImg, (IntPtr)imgPtr, true, null);
                }
            }
      
            return colors;
        }
        
        //////////////////////////////////////////////////////////

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
                    colors[x, y] = new Color((int)(255 * point.Euler.X / 360d),
                                             (int)(255 * point.Euler.Y / 90d),
                                             (int)(255 * point.Euler.Z / 90d));
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
        public double X, Y, MAD;
        public Euler Euler;
        public int BC;

        public EBSD_Point(double x, double y, double ph1, double ph2, double ph3, double mad, int bc)
        {
            X = x; Y = y; Euler.X = ph1; Euler.Y = ph2; Euler.Z = ph3; MAD = mad; BC = bc;
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
    public struct Euler
    {
        public double X, Y, Z;
        public Euler(double x, double y, double z)
        {
            X = x; Y = y; Z = z;
        }
    }


}