using OfficeOpenXml;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EBSD_Analyse;
using System.Drawing;
using Color = System.Drawing.Color;
using System.ComponentModel;

namespace WpfApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public string FilePath
        {
            get { return _filePath; }
            set
            {
                _filePath = value;
                string name = System.IO.Path.GetFileName(value);
                Title = name;
                OpenEbsdFile(value);
            }
        }
        private string _filePath;

        private Analyzer analyzer;

        public MainWindow()
        {

            ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

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

            ProgressBar.Value = 0;
            ProgressBar.Visibility = Visibility.Visible;

            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += ReadEBSDExcel;
            worker.ProgressChanged += worker_ProgressChanged;
            worker.RunWorkerCompleted += bgw_RunWorkerCompleted;

            worker.RunWorkerAsync(new ExcelPackage(new System.IO.FileInfo(path)));
        }


        void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            ProgressBar.Value = e.ProgressPercentage;
        }

        private void bgw_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            ProgressBar.Visibility = Visibility.Hidden;

            if (e.Result == null) return;

            (double maxPh1, double maxPh2, double maxPh3, EBSD_Point[,] points) result = ((double, double, double, EBSD_Point[,]))e.Result;

            analyzer.maxPh1 = result.maxPh1;
            analyzer.maxPh2 = result.maxPh2;
            analyzer.maxPh3 = result.maxPh3;
            analyzer.Ebsd_points = result.points;
            UpdateImage();
        }


        void ReadEBSDExcel(object sender, DoWorkEventArgs e)
        {
            ExcelPackage package = (ExcelPackage)e.Argument;

            try
            {
                ExcelWorksheet worksheet = package.Workbook.Worksheets[0];
                int rowCount = worksheet.Dimension.Rows;

                int xSize = worksheet.Cells[1, 4, rowCount, 4].Count(c => c.Text == "0");
                int ySize = rowCount / xSize;

                EBSD_Point[,] ebsd_Points = new EBSD_Point[xSize, ySize];

                bool fileCorruption = false;

                double maxPh1 = 0;
                double maxPh2 = 0;
                double maxPh3 = 0;


                int k = 0;
                for (int _y = 0; _y < ySize; _y++)
                {
                    for (int _x = 0; _x < xSize; _x++)
                    {
                        var x = worksheet.Cells[k + 2, 3].Value;
                        var y = worksheet.Cells[k + 2, 4].Value;

                        var ph1 = worksheet.Cells[k + 2, 5].Value;
                        var ph2 = worksheet.Cells[k + 2, 6].Value;
                        var ph3 = worksheet.Cells[k + 2, 7].Value;

                        var mad = worksheet.Cells[k + 2, 8].Value;

                        if (x == null || y == null || ph1 == null || ph2 == null || ph3 == null || mad == null)
                        {
                            fileCorruption = true;
                            ebsd_Points[_x, _y] = new EBSD_Point(0, 0, 0, 0, 0, 0);
                        }
                        else
                        {
                            double X = (double)x;
                            double Y = (double)y;

                            double Ph1 = (double)ph1;
                            double Ph2 = (double)ph2;
                            double Ph3 = (double)ph3;

                            double Mad = (double)mad;

                            maxPh1 = Ph1 > maxPh1 ? Ph1 : maxPh1;
                            maxPh2 = Ph2 > maxPh2 ? Ph2 : maxPh2;
                            maxPh3 = Ph3 > maxPh3 ? Ph3 : maxPh3;

                            ebsd_Points[_x, _y] = new EBSD_Point(X, Y, Ph1, Ph2, Ph3, Mad);
                        }

                        k++;
                    }
                        (sender as BackgroundWorker).ReportProgress(101 * k / rowCount);
                }
                e.Result = (maxPh1, maxPh2, maxPh3, ebsd_Points);

                if (fileCorruption) MessageBox.Show("Файл повреждён!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { MessageBox.Show("Ошибка чтения файла!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally
            {
                package.Dispose();
            }
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

            EBSD_Image.Source = CreateBitmapSourceFromBitmap(bmp);

            bmp.Save("Ebsd_Image");
        }

        public static BitmapSource CreateBitmapSourceFromBitmap(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException("bitmap");

            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                bitmap.GetHbitmap(),
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }

    }
}
