﻿using OfficeOpenXml;
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

        private void Initialize()
        {
            ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            analyzer = new Analyzer();
        }

        public MainWindow()
        {
            Initialize();
            InitializeComponent();


            MapVariantChoose.ItemsSource = Enum.GetValues(typeof(MapVariants)).Cast<MapVariants>();
            MapVariantChoose.SelectedIndex = 0;
        }

        // Open
        #region File_Opening
        private void DropPanel_Drop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            FilePath = files[0];
        }

        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Multiselect = false;
            dlg.FileName = "data.xlsx";
            dlg.DefaultExt = ".xlsx";
            dlg.Filter = "Excel Worksheets|*.xlsx";

            bool? result = dlg.ShowDialog();

            if (result == true) FilePath = dlg.FileName;
        }

        private void ReadEBSDExcel(object sender, DoWorkEventArgs e)
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
                        var bc = worksheet.Cells[k + 2, 10].Value;


                        if (x == null || y == null || ph1 == null || ph2 == null || ph3 == null || mad == null)
                        {
                            fileCorruption = true;
                            ebsd_Points[_x, _y] = new EBSD_Point(0, 0, 0, 0, 0, 0, 0);
                        }
                        else
                        {
                            double X = (double)x;
                            double Y = (double)y;

                            float Ph1 = (float)(double)ph1;
                            float Ph2 = (float)(double)ph2;
                            float Ph3 = (float)(double)ph3;

                            double Mad = (double)mad;
                            int Bc = Convert.ToInt32(bc);

                            ebsd_Points[_x, _y] = new EBSD_Point(X, Y, Ph1, Ph2, Ph3, Mad, Bc);
                        }

                        k++;
                    }
                        (sender as BackgroundWorker).ReportProgress(101 * k / rowCount); // Progress Change
                }
                e.Result = ebsd_Points;

                if (fileCorruption) MessageBox.Show("Файл повреждён!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка чтения файла! \n {ex.ToString()}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally
            {
                package.Dispose();
            }
        }
        #endregion File_Opening

        // Processing
        #region Processing
        private void OpenEbsdFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return; // Bad Path...

            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += ReadEBSDExcel;
            worker.ProgressChanged += OnWorkProgressChanged;
            worker.RunWorkerCompleted += OnWorkComplete;

            worker.RunWorkerAsync(new ExcelPackage(new System.IO.FileInfo(path)));
        }

        private void UpdateImage()
        {
            if (analyzer.Ebsd_points == null) return; // No data

            int width = analyzer.width;
            int height = analyzer.height;

            var colors = analyzer.GetColorMap((MapVariants)MapVariantChoose.SelectedItem); // gpu work 
            Bitmap bmp = new Bitmap(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var col = new EBSD_Analyse.Color(colors[x, y].r, colors[x, y].g, colors[x, y].b);
                    var SysColor = Color.FromArgb(col.R, col.G, col.B);
                    bmp.SetPixel(x, y, SysColor);
                }
            }
            EBSD_Image.Source = CreateBitmapSourceFromBitmap(bmp);
            bmp.Save("Ebsd_Image");
        }
        #endregion Processing

        // Processing_Events
        #region Processing_Events
        private void OnWorkProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            ProgressBar.Value = e.ProgressPercentage;
        }

        private void ProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (e.NewValue >= 100 || e.OldValue >= 100)
            {
                ProgressBar.Visibility = Visibility.Hidden;
                ProgressBar.Value = 0;
            }
            else ProgressBar.Visibility = Visibility.Visible;
        }

        private void OnWorkComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Result != null)
            {
                analyzer.Ebsd_points = (EBSD_Point[,])e.Result;
                UpdateImage();
            }
        }
        #endregion Processing_Events

        // Events
        #region Events
        private void MapVariantChoose_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateImage();
        }

        #endregion Events

        // Helpers
        #region Helpers
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
        #endregion Helpers


        // Shaders
        #region Shaders


        #endregion Shaders





    }
}
