using Cloo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace EBSD_Analyse
{
    public class Analyzer
    {

        public static float[] Work()
        {
            //Установка параметров, инициализирующих видеокарты при работе. В Platforms[1] должен стоять индекс
            //указывающий на используемую платформу
            ComputeContextPropertyList Properties = new ComputeContextPropertyList(ComputePlatform.Platforms[1]);
            ComputeContext Context = new ComputeContext(ComputeDeviceTypes.All, Properties, null, IntPtr.Zero);

            //Текст програмы, исполняющейся на устройстве (GPU или CPU). Именно эта программа будет выполнять паралельные
            //вычисления и будет складывать вектора. Программа написанна на языке, основанном на C99 специально под OpenCL.
            string shaderProgram = @"
                        
                            __kernel void floatVectorSum(__global float3 *in, __global float3 *out)
                            {

                                int i = get_global_id(0);

                                out[i] = in[i].x;
                            }

                            ";
            //Список устройств, для которых мы будем компилировать написанную в vecSum программу
            List<ComputeDevice> Devices = new List<ComputeDevice>();
            Devices.Add(ComputePlatform.Platforms[1].Devices[0]);
            
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
            ComputeKernel kernel = program.CreateKernel("floatVectorSum");

            //Инициализация и присвоение векторов, которые мы будем складывать.
            float[] v1 = new float[100];
            for (int i = 0; i < v1.Length; i++)
            {
                v1[i] = i;
            }

            //Загрузка данных в указатели для дальнейшего использования.
            ComputeBuffer<float> computeBuffer = new ComputeBuffer<float>(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.UseHostPointer, v1);

            //Объявляем какие данные будут использоваться в программе vecSum
            kernel.SetMemoryArgument(0, computeBuffer);

            //Создание програмной очереди. Не забудте указать устройство, на котором будет исполняться программа!
            ComputeCommandQueue Queue = new ComputeCommandQueue(Context, Cloo.ComputePlatform.Platforms[1].Devices[0], Cloo.ComputeCommandQueueFlags.None);

            //Старт. Execute запускает программу-ядро vecSum указанное колличество раз (v1.Length)
            Queue.Execute(kernel, null, new long[] { v1.Length }, null, null);

            //Считывание данных из памяти устройства.
            float[] result = new float[100];

            GCHandle gC = GCHandle.Alloc(result, GCHandleType.Pinned);
            Queue.Read(computeBuffer, true, 0, 100, gC.AddrOfPinnedObject(), null);

            return result;
        }


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