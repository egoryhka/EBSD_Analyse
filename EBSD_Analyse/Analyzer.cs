using Cloo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace EBSD_Analyse
{
    public struct Analyzer_Data
    {
        public EBSD_Point[,] Ebsd_points;
        public Euler[] Eulers;
        public int[] BCs;
        public int Width;
        public int Height;
        public List<Grain> Grains;

        public Analyzer_Data(EBSD_Point[,] ebsd_points)
        {
            Ebsd_points = ebsd_points;
            Grains = new List<Grain>();

            Width = ebsd_points.GetLength(0);
            Height = ebsd_points.GetLength(1);

            Eulers = new Euler[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Eulers[x + y * Width] = ebsd_points[x, y].Euler;
                }
            }
            //eulers = value.Cast<EBSD_Point>().Select(x => x.Euler).ToArray();

            BCs = new int[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    BCs[x + y * Width] = ebsd_points[x, y].BC;
                }
            }
            //bcs = value.Cast<EBSD_Point>().Select(x => x.BC).ToArray();

        }
    }


    public class Analyzer
    {
        public Analyzer_Data Data;

        private byte[] GrainMask;
        private bool[,] GrainPointsDefined;
        private ComputeContext Context;
        private ComputeProgram Program;


        public Analyzer() => Init();

        private void Init()
        {
            Program = null;

            //Установка параметров, инициализирующих видеокарты при работе. В Platforms[1] должен стоять индекс
            //указывающий на используемую платформу
            ComputeContextPropertyList Properties = new ComputeContextPropertyList(ComputePlatform.Platforms[0]);
            Context = new ComputeContext(ComputeDeviceTypes.All, Properties, null, IntPtr.Zero);

            //Текст програмы, исполняющейся на GPU
            string prog = @"

                 struct Euler
                 {
                     float x;
                     float y;
                     float z;
                 }; typedef struct Euler euler;

                 euler eul_sum(euler a, euler b){
                    return (euler){a.x+b.x,a.y+b.y,a.z+b.z};
                 }

                 __kernel void Euler2Color(__global char* out, int width, int height,
                 __read_only image2d_t in)
                 {
                     const sampler_t smp = CLK_NORMALIZED_COORDS_FALSE | //Natural coordinates
                                           CLK_ADDRESS_CLAMP | //Clamp to zeros
                                           CLK_FILTER_NEAREST; //Don't interpolate

                     int x = get_global_id(0);
                     int y = get_global_id(1);

                     int2 coord = (int2)(x, y); 
                     float4 eul = read_imagef (in, smp, coord);

                     int4 col = convert_int4((float4)(255.0f*eul.x/360.0f, 255.0f*eul.y/90.0f, 255.0f*eul.z/90.0f, 0));

                     int linearId = (int)((x + y * width) * 4);

                     out[linearId] = col.x; // R
                     out[linearId+1] = col.y; // G
                     out[linearId+2] = col.z; // B
                     out[linearId+3] = 255; // A

                 }

             //------------------------------------------------------------

                 __kernel void Bc2Color(__global char* out, int width, int height,
                 __read_only image2d_t in)
                 {
                     const sampler_t smp = CLK_NORMALIZED_COORDS_FALSE | //Natural coordinates
                                           CLK_ADDRESS_CLAMP | //Clamp to zeros
                                           CLK_FILTER_NEAREST; //Don't interpolate

                     int x = get_global_id(0);
                     int y = get_global_id(1);

                     int2 coord = (int2)(x, y); 
                     int4 BC = read_imagei (in, smp, coord);

                     int4 col = (int4)(BC.x,BC.x,BC.x,0);

                     int linearId = (int)((x + y * width) * 4);

                     out[linearId] = col.x; // R
                     out[linearId+1] = col.y; // G
                     out[linearId+2] = col.z; // B
                     out[linearId+3] = 255; // A

                 }

             //------------------------------------------------------------


                 __kernel void Extrapolate(__global euler* out, int width, int height,
                 __global euler* in)
                 {
                     int id = get_global_id(0);

                     euler eul = in[id];

                     if(eul.x > 0 && eul.y > 0 && eul.z > 0) 
                     { 
                         out[id] = eul;
                     }
                     else
                     {
                        int k = 0;
                        euler sum = { 0, 0, 0};
                         
                        bool can_up = id > width;
                        bool can_left = (id % width) != 0;
                        bool can_right = ((id+1) % width) != 0;
                        bool can_down = id < (width * height - width);

                        bool can_upLeft = can_up && can_left;
                        bool can_upRifht = can_up && can_right;
                        bool can_downLeft = can_down && can_left;
                        bool can_downRight = can_down && can_right;

                        int up = id-width;
                        int left = id-1;
                        int right = id+1;
                        int down = id+width;

                        int upLeft = id-width-1;
                        int upRight = id-width+1;
                        int downLeft = id+width-1;
                        int downRight = id+width+1;

                        if(can_up && (in[up].x > 0 || in[up].y > 0 || in[up].z >0)) { out[id] = in[up]; }    
                        if(can_left && (in[left].x > 0 || in[left].y > 0 || in[left].z >0)) { out[id] = in[left]; }    
                        if(can_right && (in[right].x > 0 || in[right].y > 0 || in[right].z >0)) { out[id] = in[right]; }    
                        if(can_down && (in[down].x > 0 || in[down].y > 0 || in[down].z >0)) { out[id] = in[down]; }    
                        
                        //if(can_up && (in[up].x > 0 || in[up].y > 0 || in[up].z >0)) {k++; sum = eul_sum(sum, in[up]); }    
                        //if(can_left && (in[left].x > 0 || in[left].y > 0 || in[left].z >0)) {k++; sum = eul_sum(sum, in[left]); }    
                        //if(can_right && (in[right].x > 0 || in[right].y > 0 || in[right].z >0)) {k++; sum = eul_sum(sum, in[right]); }    
                        //if(can_down && (in[down].x > 0 || in[down].y > 0 || in[down].z >0)) {k++; sum = eul_sum(sum, in[down]); }    

                        //if(can_upLeft && (in[upLeft].x > 0 || in[upLeft].y > 0 || in[upLeft].z >0)) {k++; sum = eul_sum(sum, in[upLeft]); }    
                        //if(can_upRifht && (in[upRight].x > 0 || in[upRight].y > 0 || in[upRight].z >0)) {k++; sum = eul_sum(sum, in[upRight]); }    
                        //if(can_downLeft && (in[downLeft].x > 0 || in[downLeft].y > 0 || in[downLeft].z >0)) {k++; sum = eul_sum(sum, in[downLeft]); }    
                        //if(can_downRight && (in[downRight].x > 0 || in[downRight].y > 0 || in[downRight].z >0)) {k++; sum = eul_sum(sum, in[downRight]); }    

                        //if(k >= 4){
                        //    out[id] = (euler){ sum.x / k, sum.y / k, sum.z / k};
                        //}

                     } 
                     
                     
                 }

             //------------------------------------------------------------


                float3 rotateVector(float3 a, float3 eul){
                   
                    eul=(float3)(radians(eul.x),radians(eul.y),radians(eul.z));
                    a = (float3)(a.x * cos(eul.x) - a.y * sin(eul.x), a.x * sin(eul.x) + a.y * cos(eul.x), a.z); // Z - rotation
                    a = (float3)(a.x * cos(eul.y) - a.z * sin(eul.y), a.y, -a.x * sin(eul.y) + a.z * cos(eul.y)); // Y - rotation
                    a = (float3)(a.x, a.y * cos(eul.z) - a.z * sin(eul.z), a.y * sin(eul.z) + a.z * cos(eul.z)); // X - rotation
                        return a;
                }

                float angleBetween(float3 eul1, float3 eul2){
                    
                    float3 a = (float3)(1,1,1);
                    float3 b = (float3)(1,1,1);

                    a = rotateVector(a,eul1);
                    b = rotateVector(b,eul2);

                    return degrees(acos(dot(a,b)/(length(a)*length(b))));
                }

                 __kernel void GetGrainMask(__global char* out, int width, int height,
                 __global euler* in, float MissOrientationTreshold)
                 {
                     int id = get_global_id(0);

                     float3 eul = (float3)(in[id].x,in[id].y,in[id].z);

                     float isEdge = 0;

                     bool can_up = id > width;
                     bool can_left = (id % width) != 0;
                     bool can_right = ((id+1) % width) != 0;
                     bool can_down = id < (width * height - width);
                    
                     int up = id-width;
                     int left = id-1;
                     int right = id+1;
                     int down = id+width;
                    
                     if(can_up && isEdge == 0) { float3 upEuler = (float3)(in[up].x,in[up].y,in[up].z); if(angleBetween(eul,upEuler)>MissOrientationTreshold) isEdge=1; }    
                     if(can_left && isEdge == 0) { float3 leftEuler = (float3)(in[left].x,in[left].y,in[left].z); if(angleBetween(eul,leftEuler)>MissOrientationTreshold) isEdge=1; }
                     if(can_right && isEdge == 0) { float3 rightEuler = (float3)(in[right].x,in[right].y,in[right].z); if(angleBetween(eul,rightEuler)>MissOrientationTreshold) isEdge=1; }    
                     if(can_down && isEdge == 0) { float3 downEuler = (float3)(in[down].x,in[down].y,in[down].z); if(angleBetween(eul,downEuler)>MissOrientationTreshold) isEdge=1;  }    

                     out[id] = isEdge;
                 }

             //------------------------------------------------------------

                 __kernel void GrainMaskFilter(__global char* out, int width, int height,
                 __global char* in, __global char* grainMask)
                 {
                     int id = get_global_id(0);
                     int maskId = id/4;
                     if(grainMask[maskId]==0) out[id]=in[id];
                     else out[id] = 255;
                 }

             //------------------------------------------------------------
                            ";

            //Список устройств
            List<ComputeDevice> Devices = new List<ComputeDevice>();
            Devices.Add(ComputePlatform.Platforms[0].Devices[0]);

            //Компиляция программы
            try
            {
                Program = new ComputeProgram(Context, prog);
                Program.Build(Devices, "", null, IntPtr.Zero);
            }
            catch { }
        }

        public byte[] GetColorMap(MapVariants mapVariant)
        {
            ComputeKernel kernel = null;

            // Инициализация новой программы
            try
            {
                switch (mapVariant)
                {
                    case MapVariants.BC: kernel = Program.CreateKernel("Bc2Color"); break;
                    case MapVariants.Euler: kernel = Program.CreateKernel("Euler2Color"); break;
                }
            }
            catch { }

            if (kernel == null) return null;

            // Создание програмной очереди.
            ComputeCommandQueue Queue = new ComputeCommandQueue(Context, Cloo.ComputePlatform.Platforms[0].Devices[0], Cloo.ComputeCommandQueueFlags.None);

            // Инициализация выходного буффера и массива
            ComputeBuffer<byte> colorsBuffer = new ComputeBuffer<byte>(Context, ComputeMemoryFlags.ReadWrite, Data.Width * Data.Height * 4);

            // Установка параметров для расчетов
            kernel.SetMemoryArgument(0, colorsBuffer);
            kernel.SetValueArgument<int>(1, Data.Width);
            kernel.SetValueArgument<int>(2, Data.Height);

            List<ComputeMemory> memoryList = new List<ComputeMemory>();

            int i = 3;
            foreach (var obj in GetKernelArguments(mapVariant))
            {
                if (obj as ComputeMemory != null)
                {
                    ComputeMemory memory = obj as ComputeMemory;
                    kernel.SetMemoryArgument(i, memory);
                    memoryList.Add(memory);
                }
                else
                {
                    kernel.SetValueArgument<int>(i, (int)obj);
                }
                i++;
            }

            // Запуск 
            Queue.Execute(kernel, null, new long[] { Data.Width, Data.Height }, null, null);

            // Считывание результата
            byte[] colors = new byte[Data.Width * Data.Height * 4]; // output

            Queue.ReadFromBuffer(colorsBuffer, ref colors, true, null);

            colorsBuffer.Dispose();

            foreach (var mem in memoryList)
            {
                if (mem != null) mem.Dispose();
            }

            kernel.Dispose();
            Queue.Dispose();

            return colors;
        }

        public byte[] ApplyGrainFilter(byte[] img, float MissOrientationThreshold)
        {
            if (GrainMask == null) return img;
            //GrainMask = CalculateGrainMask(MissOrientationThreshold);

            ComputeKernel kernel = null;

            // Инициализация новой программы
            try
            {
                kernel = Program.CreateKernel("GrainMaskFilter");
            }
            catch { }

            if (kernel == null) return null;

            // Создание програмной очереди.
            ComputeCommandQueue Queue = new ComputeCommandQueue(Context, Cloo.ComputePlatform.Platforms[0].Devices[0], Cloo.ComputeCommandQueueFlags.None);

            // Инициализация буфферов
            ComputeBuffer<byte> inputBuffer;
            ComputeBuffer<byte> grainMaskBuffer;
            ComputeBuffer<byte> outputBuffer;

            inputBuffer = new ComputeBuffer<byte>(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, img);
            grainMaskBuffer = new ComputeBuffer<byte>(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, GrainMask);
            outputBuffer = new ComputeBuffer<byte>(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, new byte[img.Length]);

            // Установка параметров для расчетов
            kernel.SetMemoryArgument(0, outputBuffer);
            kernel.SetValueArgument<int>(1, Data.Width);
            kernel.SetValueArgument<int>(2, Data.Height);
            kernel.SetMemoryArgument(3, inputBuffer);
            kernel.SetMemoryArgument(4, grainMaskBuffer);


            // Запуск 
            Queue.Execute(kernel, null, new long[] { img.Length }, null, null);

            // Считывание результата
            byte[] res = new byte[img.Length];
            Queue.ReadFromBuffer(outputBuffer, ref res, true, null);

            inputBuffer.Dispose();
            outputBuffer.Dispose();
            grainMaskBuffer.Dispose();
            kernel.Dispose();
            Queue.Dispose();

            return res;
        }

        public void Extrapolate(int iterations)
        {
            ComputeKernel kernel = null;

            // Инициализация новой программы
            try
            {
                kernel = Program.CreateKernel("Extrapolate");
            }
            catch { }

            if (kernel == null) return;

            // Создание програмной очереди.
            ComputeCommandQueue Queue = new ComputeCommandQueue(Context, Cloo.ComputePlatform.Platforms[0].Devices[0], Cloo.ComputeCommandQueueFlags.None);

            // Инициализация буфферов
            ComputeBuffer<Euler> inputBuffer;
            ComputeBuffer<Euler> outputBuffer;

            inputBuffer = new ComputeBuffer<Euler>(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, Data.Eulers);
            outputBuffer = new ComputeBuffer<Euler>(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, new Euler[Data.Eulers.Length]);

            // Установка параметров для расчетов
            kernel.SetMemoryArgument(0, outputBuffer);
            kernel.SetValueArgument<int>(1, Data.Width);
            kernel.SetValueArgument<int>(2, Data.Height);
            kernel.SetMemoryArgument(3, inputBuffer);

            // Первый (обязательный) Запуск 
            Queue.Execute(kernel, null, new long[] { Data.Eulers.Length }, null, null);

            // Перенос выхода на вход
            Queue.CopyBuffer(outputBuffer, inputBuffer, null);

            for (int i = 0; i < iterations - 1; i++)
            {
                // Запуск 
                Queue.Execute(kernel, null, new long[] { Data.Eulers.Length }, null, null);
                Queue.CopyBuffer(outputBuffer, inputBuffer, null);
            }


            // Считывание результата
            Euler[] res = new Euler[Data.Eulers.Length];
            Queue.ReadFromBuffer(outputBuffer, ref res, true, null);

            Data.Eulers = res;

            inputBuffer.Dispose();
            outputBuffer.Dispose();
            kernel.Dispose();
            Queue.Dispose();
        }

        public void RecalculateGrains(float MissOrientationThreshold)
        {
            GrainMask = CalculateGrainMask(MissOrientationThreshold);

            // собственно заливка и т.д (долгие операции)
            Data.Grains = new List<Grain>();

            GrainPointsDefined = new bool[Data.Width, Data.Height];

            int grainId = 0;
            for (int y = 0; y < Data.Height; y++)
            {
                for (int x = 0; x < Data.Width; x++)
                {

                    int id_2D = x + y * Data.Width;
                    if (GrainMask[id_2D] != 1 && !GrainPointsDefined[x, y])
                    {
                        Grain grain = new Grain() { Edges = new List<Vector2>(), Points = new List<Vector2>() };
                        FloodFill(new Vector2(x, y), ref grain, MissOrientationThreshold);
                        grain.id = grainId;
                        grainId++;
                        Data.Grains.Add(grain);
                    }
                }
            }

        }
  

        private void FloodFill(Vector2 pt, ref Grain grain, float MissOrientationThreshold)
        {
            Stack<Vector2> pixels = new Stack<Vector2>();
            pixels.Push(pt);

            Dictionary<int, int> phases = new Dictionary<int, int>();

            Euler startPointEuler = new Euler(0, 0, 0);

            if (pt.X < Data.Width && pt.X >= 0 && pt.Y < Data.Height && pt.Y >= 0) startPointEuler = Data.Eulers[(int)pt.X + (int)pt.Y * Data.Width];

            while (pixels.Count > 0)
            {
                Vector2 a = pixels.Pop();
                int x = (int)a.X;
                int y = (int)a.Y;

                if (x < Data.Width && x >= 0 &&
                        y < Data.Height && y >= 0 && GrainPointsDefined[x, y] == false)//make sure we stay within bounds
                {
                    int phase = Data.Ebsd_points[x, y].Phase;

                    if (phases.ContainsKey(phase)) { phases[phase]++; }
                    else { phases.Add(phase, 1); }

                    int linearId = x + y * Data.Width;
                    if (GrainMask[linearId] == 0)
                    {
                        grain.Points.Add(a);
                        GrainPointsDefined[x, y] = true;
                        pixels.Push(new Vector2(x - 1, y));
                        pixels.Push(new Vector2(x + 1, y));
                        pixels.Push(new Vector2(x, y - 1));
                        pixels.Push(new Vector2(x, y + 1));
                    }
                    else
                    {
                        grain.Edges.Add(a);
                    }
                }
            }
            grain.phase = phases.FirstOrDefault(x => x.Value == phases.Max(x => x.Value)).Key;

            return;
        }


        private object[] GetKernelArguments(MapVariants mapVariant)
        {
            switch (mapVariant)
            {
                case MapVariants.BC:
                    {
                        ComputeImage2D bcImg;

                        unsafe
                        {
                            fixed (int* imgPtr = Data.BCs)
                            {
                                ComputeImageFormat inputFormat = new ComputeImageFormat(ComputeImageChannelOrder.R, ComputeImageChannelType.SignedInt32);
                                bcImg = new ComputeImage2D(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, inputFormat, Data.Width, Data.Height, Data.Width * 1 * sizeof(int), (IntPtr)imgPtr);
                            }
                        }
                        return new object[] { bcImg };
                    } // bc preparation
                case MapVariants.Euler:
                    {
                        ComputeImage2D pointsImg;
                        float[] points = new float[4 * Data.Eulers.Length]; // input

                        int k = 0;
                        for (int i = 0; i < points.Length - 3; i += 4)
                        {
                            points[i] = Data.Eulers[k].X;
                            points[i + 1] = Data.Eulers[k].Y;
                            points[i + 2] = Data.Eulers[k].Z;
                            k++;
                        }
                        unsafe
                        {
                            fixed (float* imgPtr = points)
                            {
                                ComputeImageFormat inputFormat = new ComputeImageFormat(ComputeImageChannelOrder.Rgba, ComputeImageChannelType.Float);
                                pointsImg = new ComputeImage2D(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, inputFormat, Data.Width, Data.Height, Data.Width * 4 * sizeof(float), (IntPtr)imgPtr);
                            }
                        }
                        return new object[] { pointsImg };
                    } // euler points preparation
                default: return null;
            }
        }

        private byte[] CalculateGrainMask(float MissOrientationTreshold)
        {
            ComputeKernel kernel = null;

            // Инициализация новой программы
            try
            {
                kernel = Program.CreateKernel("GetGrainMask");
            }
            catch { }

            if (kernel == null) return null;

            // Создание програмной очереди.
            ComputeCommandQueue Queue = new ComputeCommandQueue(Context, Cloo.ComputePlatform.Platforms[0].Devices[0], Cloo.ComputeCommandQueueFlags.None);

            // Инициализация буфферов
            ComputeBuffer<Euler> inputBuffer;
            ComputeBuffer<byte> outputBuffer;

            inputBuffer = new ComputeBuffer<Euler>(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, Data.Eulers);
            outputBuffer = new ComputeBuffer<byte>(Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, new byte[Data.Eulers.Length]);

            // Установка параметров для расчетов
            kernel.SetMemoryArgument(0, outputBuffer);
            kernel.SetValueArgument<int>(1, Data.Width);
            kernel.SetValueArgument<int>(2, Data.Height);
            kernel.SetMemoryArgument(3, inputBuffer);
            kernel.SetValueArgument<float>(4, MissOrientationTreshold);


            // Запуск 
            Queue.Execute(kernel, null, new long[] { Data.Eulers.Length }, null, null);

            // Считывание результата
            byte[] res = new byte[Data.Eulers.Length];
            Queue.ReadFromBuffer(outputBuffer, ref res, true, null);

            inputBuffer.Dispose();
            outputBuffer.Dispose();
            kernel.Dispose();
            Queue.Dispose();

            return res;

        }


    }

    public enum MapVariants
    {
        BC, Euler,
    }

    public struct EBSD_Point
    {
        public float X, Y, MAD;
        public Euler Euler;
        public int BC;
        public int Phase;

        public EBSD_Point(float x, float y, float ph1, float ph2, float ph3, float mad, int bc, int phase)
        {
            X = x; Y = y; Euler = new Euler(ph1, ph2, ph3); MAD = mad; BC = bc; Phase = phase;
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

    public struct Grain
    {
        public List<Vector2> Points;
        public List<Vector2> Edges;

        public int id { get; set; }
        public float size => Points.Count + Edges.Count;
        public int phase { get; set; }
    }


}