using OfficeOpenXml;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EBSD_Analyse;
using System.Drawing;
using Color = System.Drawing.Color;

namespace WpfApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public string FilePath { get { return _filePath; } set { _filePath = value; OpenedFileLabel.Content = System.IO.Path.GetFileName(value); OpenEbsdFile(value); } }
        private string _filePath;

        private Analyzer analyzer;

        public MainWindow()
        {

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            InitializeComponent();

            analyzer = new Analyzer();
        }

        private void DropPanel_Drop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            FilePath = files[0];
        }

        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            // Configure open file dialog box
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.FileName = "data.xlsx"; // Default file name
            dlg.DefaultExt = ".xlsx"; // Default file extension
            dlg.Filter = "Excel Worksheets|*.xlsx";

            // Show open file dialog box
            Nullable<bool> result = dlg.ShowDialog();

            // Process open file dialog box results
            if (result == true)
            {
                // Open document
                FilePath = dlg.FileName;
            }
        }


        private void OpenEbsdFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return; // Bad Path...

            //try
            {
                using (ExcelPackage package = new ExcelPackage(new System.IO.FileInfo(path)))
                {
                    ExcelWorksheet worksheet = package.Workbook.Worksheets[0];
                    int rowCount = worksheet.Dimension.Rows;

                    int xSize = worksheet.Cells[1, 4, rowCount, 4].Count(c => c.Text == "0");
                    int ySize = rowCount / xSize;

                    EBSD_Point[,] ebsd_Points = new EBSD_Point[xSize, ySize];

                    int k = 0;
                    for (int _y = 0; _y < ySize; _y++)
                    {
                        for (int _x = 0; _x < xSize; _x++)
                        {
                            double x = (double)(worksheet.Cells[k + 2, 3].Value);
                            double y = (double)(worksheet.Cells[k + 2, 4].Value);

                            double ph1 = (double)(worksheet.Cells[k + 2, 5].Value);
                            double ph2 = (double)(worksheet.Cells[k + 2, 6].Value);
                            double ph3 = (double)(worksheet.Cells[k + 2, 7].Value);

                            double mad = (double)(worksheet.Cells[k + 2, 8].Value);

                            ebsd_Points[_x, _y] = new EBSD_Point(x, y, ph1, ph2, ph3, mad);

                            k++;
                        }
                    }
                    analyzer.Ebsd_points = ebsd_Points;
                }
            }
            //catch { throw new Exception("Ошибка чтения файла"); }

            UpdateImage();

        }

        private void UpdateImage()
        {

            if (analyzer.Ebsd_points == null) return; // No data

            int width = analyzer.Ebsd_points.GetLength(0);
            int height = analyzer.Ebsd_points.GetLength(1);

            var colors = analyzer.GetColors();

            Bitmap bmp = new Bitmap(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var color = Color.FromArgb(colors[x, y].R, colors[x, y].G, colors[x, y].B);
                    bmp.SetPixel(x, y, color);

                }
            }
                    EBSD_Image.Source = GetBitmapSource(bmp);

        }

        private void UpdateEbsdImageButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateImage();
        }


        public static BitmapSource GetBitmapSource(System.Drawing.Bitmap bitmap)
        {
            var bitmapData = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);

            var bitmapSource = BitmapSource.Create(
                bitmapData.Width, bitmapData.Height,
                bitmap.HorizontalResolution, bitmap.VerticalResolution,
                PixelFormats.Bgr24, null,
                bitmapData.Scan0, bitmapData.Stride * bitmapData.Height, bitmapData.Stride);

            bitmap.UnlockBits(bitmapData);

            return bitmapSource;
        }

    }
}
