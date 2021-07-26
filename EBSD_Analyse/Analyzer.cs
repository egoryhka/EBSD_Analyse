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

        public (int r, int g, int b)[,] GetColorMap(MapVariants mapVariant)
        {
            //Установка параметров, инициализирующих видеокарты при работе. В Platforms[1] должен стоять индекс
            //указывающий на используемую платформу
            ComputeContextPropertyList Properties = new ComputeContextPropertyList(ComputePlatform.Platforms[0]);
            ComputeContext Context = new ComputeContext(ComputeDeviceTypes.All, Properties, null, IntPtr.Zero);

            //Текст програмы, исполняющейся на устройстве (GPU или CPU). Именно эта программа будет выполнять паралельные
            //вычисления и будет складывать вектора. Программа написанна на языке, основанном на C99 специально под OpenCL.
            string euler2col = @"

                            __kernel void Euler2Color(__read_only image2d_t in, __write_only image2d_t out)
                            {
                                const sampler_t smp = CLK_NORMALIZED_COORDS_FALSE | //Natural coordinates
                                                      CLK_ADDRESS_CLAMP | //Clamp to zeros
                                                      CLK_FILTER_NEAREST; //Don't interpolate

                                int2 coord = (int2)(get_global_id(0), get_global_id(1)); 

                                float4 eul = read_imagef (in, smp, coord);

                                int4 col = convert_int4((float4)(255.0f*eul.x/360.0f, 255.0f*eul.y/90.0f, 255.0f*eul.z/90.0f, eul.w));

                                write_imagei (out, coord, col);
                            }

                            ";

            string bc2col = @"

                            __kernel void Euler2Color(__read_only image2d_t in, __write_only image2d_t out)
                            {
                                const sampler_t smp = CLK_NORMALIZED_COORDS_FALSE | //Natural coordinates
                                                      CLK_ADDRESS_CLAMP | //Clamp to zeros
                                                      CLK_FILTER_NEAREST; //Don't interpolate

                                int2 coord = (int2)(get_global_id(0), get_global_id(1)); 

                                float4 eul = read_imagef (in, smp, coord);

                                int4 col = convert_int4((float4)(255.0f*eul.x/90.0f, 255.0f*eul.y/90.0f, 255.0f*eul.z/90.0f, eul.w));

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
               
                switch (mapVariant)
                {
                    case MapVariants.BC: program = new ComputeProgram(Context, bc2col); break;
                    case MapVariants.Euler: program = new ComputeProgram(Context, euler2col); break;
                }
                
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
            Euler[] eulers = Ebsd_points.Cast<EBSD_Point>().Select(x => x.Euler).ToArray();

            float[] points = new float[4 * eulers.Length]; // input
            (int r, int g, int b)[,] colors = new (int r, int g, int b)[width, height]; // output

            int k = 0;
            for (int i = 0; i < points.Length - 3; i += 4)
            {
                points[i] = eulers[k].X;
                points[i + 1] = eulers[k].Y;
                points[i + 2] = eulers[k].Z;
                k++;
            }

            unsafe
            {
                fixed (float* imgPtr = points)
                {
                    ComputeImageFormat inputFormat = new ComputeImageFormat(ComputeImageChannelOrder.Rgba, ComputeImageChannelType.Float);
                    ComputeImageFormat outputFormat = new ComputeImageFormat(ComputeImageChannelOrder.Rgba, ComputeImageChannelType.SignedInt32);

                    pointsImg = new ComputeImage2D(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, inputFormat, width, height, width * 4 * sizeof(float), (IntPtr)imgPtr);

                    colorsImg = new ComputeImage2D(Context, ComputeMemoryFlags.ReadWrite, outputFormat, width, height, 0, IntPtr.Zero);
                }

                kernel.SetMemoryArgument(0, pointsImg);
                kernel.SetMemoryArgument(1, colorsImg);

                Queue.Execute(kernel, null, new long[] { width, height }, null, null);


                fixed ((int r, int g, int b)* imgPtr = colors)
                {
                    Queue.ReadFromImage(colorsImg, (IntPtr)imgPtr, true, null);
                }
            }

            return colors;
        }

        //////////////////////////////////////////////////////////

        public EBSD_Point[,] Ebsd_points;
        public int width => Ebsd_points.GetLength(0);
        public int height => Ebsd_points.GetLength(1);

        public Analyzer() { }
        public Analyzer(EBSD_Point[,] ebsd_Points)
        {
            Ebsd_points = ebsd_Points;
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

        public EBSD_Point(double x, double y, float ph1, float ph2, float ph3, double mad, int bc)
        {
            X = x; Y = y; Euler = new Euler(ph1, ph2, ph3); MAD = mad; BC = bc;
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
        public float X, Y, Z;
        public Euler(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }
    }


}