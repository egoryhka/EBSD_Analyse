using System;
using EBSD_Analyse;
using Graphics;

namespace App
{
    class Program
    {
        static void Main(string[] args)
        {
            using (Window window = new Window(500, 500))
            {
                window.Run();
            }
        }
    }
}
