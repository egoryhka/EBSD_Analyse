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
        public EBSD_Point[,] Ebsd_points
        {
            get { return ebsd_points; }
            set
            {
                ebsd_points = value;
                eulers = value.Cast<EBSD_Point>().Select(x => x.Euler).ToArray();
                bcs = value.Cast<EBSD_Point>().Select(x => x.BC).ToArray();
            }
        }
        private EBSD_Point[,] ebsd_points;
        private Euler[] eulers;
        private int[] bcs;

        public int width => Ebsd_points.GetLength(0);
        public int height => Ebsd_points.GetLength(1);

        private ComputeContext Context;
        private ComputeProgram Program;


        public Analyzer() => Init();

        private void Init()
        {
            Program = null;

            //Установка параметров, инициализирующих видеокарты при работе. В Platforms[1] должен стоять индекс
            //указывающий на используемую платформу
            ComputeContextPropertyList Properties = new ComputeContextPropertyList(ComputePlatform.Platforms[1]);
            Context = new ComputeContext(ComputeDeviceTypes.All, Properties, null, IntPtr.Zero);

            //Текст програмы, исполняющейся на GPU
            string prog = @"

                            __kernel void Euler2Color(__global char* out, int width, int height,
                            __read_only image2d_t in)
                            {
                                const sampler_t smp = CLK_NORMALIZED_COORDS_FALSE | //Natural coordinates
                                                      CLK_ADDRESS_CLAMP | //Clamp to zeros
                                                      CLK_FILTER_NEAREST; //Don't interpolate

                                int x = get_global_id(0);
                                int y = get_global_id(1);

                                int2 coord = (int2)(x, y); 
                                int linearId = (int)((x + y * width) * 4);

                                float4 eul = read_imagef (in, smp, coord);

                                int4 col = convert_int4((float4)(255.0f*eul.x/360.0f, 255.0f*eul.y/90.0f, 255.0f*eul.z/90.0f, 0));

                                out[linearId+3] = 255;
                                out[linearId+2] = col.x;
                                out[linearId+1] = col.y;
                                out[linearId] = col.z;

                            }

                        //------------------------------------------------------------

                            __kernel void Bc2Color(__global char* out, int width, int height,
                            __global int* bc)
                            {
                                int x = get_global_id(0);
                                int y = get_global_id(1);

                                int linearId = (int)((y + x * height) * 4);


                                int4 col = (int4)(bc[linearId/4], bc[linearId/4], bc[linearId/4], 0);

                                out[linearId+3] = 255;
                                out[linearId+2] = col.x;
                                out[linearId+1] = col.y;
                                out[linearId] = col.z;
                            }

                        //------------------------------------------------------------

                            ";

            //Список устройств
            List<ComputeDevice> Devices = new List<ComputeDevice>();
            Devices.Add(ComputePlatform.Platforms[1].Devices[0]);

            //Компиляция программы
            try
            {
                Program = new ComputeProgram(Context, prog);
                Program.Build(Devices, "", null, IntPtr.Zero);
            }
            catch { }
        }

        private object[] GetKernelArguments(MapVariants mapVariant)
        {
            switch (mapVariant)
            {
                case MapVariants.BC:
                    {
                        ComputeBuffer<int> bcBuffer = new ComputeBuffer<int>(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.UseHostPointer, bcs);
                        return new object[] { bcBuffer };
                    } // bc preparation
                case MapVariants.Euler:
                    {
                        ComputeImage2D pointsImg;
                        float[] points = new float[4 * eulers.Length]; // input

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
                                pointsImg = new ComputeImage2D(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, inputFormat, width, height, width * 4 * sizeof(float), (IntPtr)imgPtr);
                            }
                        }
                        return new object[] { pointsImg };
                    } // euler points preparation
                default: return null;
            }
        }

        public byte[] GetColorMap(MapVariants mapVariant)
        {
            ComputeKernel kernel = null;

            // Инициализация новой программы
            //try
            //{
                switch (mapVariant)
                {
                    case MapVariants.BC: kernel = Program.CreateKernel("Bc2Color"); break;
                    case MapVariants.Euler: kernel = Program.CreateKernel("Euler2Color"); break;
                }
            //}
            //catch { }

            if (kernel == null) return null;

            // Создание програмной очереди.
            ComputeCommandQueue Queue = new ComputeCommandQueue(Context, Cloo.ComputePlatform.Platforms[1].Devices[0], Cloo.ComputeCommandQueueFlags.None);

            // Инициализация выходного буффера и массива
            ComputeBuffer<byte> colorsBuffer = new ComputeBuffer<byte>(Context, ComputeMemoryFlags.ReadWrite, width * height * 4);

            // Установка параметров для расчетов
            kernel.SetMemoryArgument(0, colorsBuffer);
            kernel.SetValueArgument<int>(1, width);
            kernel.SetValueArgument<int>(2, height);


            int i = 3;
            foreach (var obj in GetKernelArguments(mapVariant))
            {
                if (obj as ComputeMemory != null)
                    kernel.SetMemoryArgument(i, obj as ComputeMemory);
                else
                {
                    kernel.SetValueArgument<int>(i, (int)obj);
                }
                i++;
            }

            // Запуск 
            Queue.Execute(kernel, null, new long[] { width, height }, null, null);

            // Считывание результата
            byte[] colors = new byte[width * height * 4]; // output

            Queue.ReadFromBuffer(colorsBuffer, ref colors, true, null);

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