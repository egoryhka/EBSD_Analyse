using OfficeOpenXml;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using EBSD_Analyse;
using System.Drawing;
using Color = System.Drawing.Color;
using System.ComponentModel;
using Newtonsoft.Json;
using System.Windows.Data;
using System.IO;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using OxyPlot.Wpf;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Axes;

namespace WpfApp
{


    public class FileInfo
    {
        public string name { get; set; }
        public string dataPath { get; set; }
        public BitmapFrame image { get; set; }
    }
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    public partial class MainWindow : Window
    {
        private FileInfo[] ReadFilesHistory(string path)
        {
            FileInfo[] history;
            using (StreamReader sr = new StreamReader(path))
            {
                string json = sr.ReadToEnd();
                FileInfo[] info = null;
                try
                {
                    info = JsonConvert.DeserializeObject<FileInfo[]>(json);
                }
                catch { MessageBox.Show("Не удалось загрузить часть недавно открытых файлов, пожалуйста перезагрузите их вручную"); }
                history = info;
            }
            return history;
        }

        private void SaveFilesHistory(string path, FileInfo[] history)
        {
            using (StreamWriter sw = new StreamWriter(path))
            {

                string json = JsonConvert.SerializeObject(history);

                sw.Write(json);
            }
        }


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

        const string historyPath = "FilesHistory";
        public List<FileInfo> FilesHistory = new List<FileInfo>();
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


            if (!File.Exists(historyPath))
            {
                File.Create(historyPath);
            }
            else
            {
                FileInfo[] history = ReadFilesHistory(historyPath);

                if (history != null)
                {
                    FilesHistory.AddRange(history);

                    FilesList.ItemsSource = FilesHistory;
                }
            }

            MapVariantChoose.ItemsSource = Enum.GetValues(typeof(MapVariants)).Cast<MapVariants>();

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
                        var phase = worksheet.Cells[k + 2, 2].Value;

                        var x = worksheet.Cells[k + 2, 3].Value;
                        var y = worksheet.Cells[k + 2, 4].Value;

                        var ph1 = worksheet.Cells[k + 2, 5].Value;
                        var ph2 = worksheet.Cells[k + 2, 6].Value;
                        var ph3 = worksheet.Cells[k + 2, 7].Value;

                        var mad = worksheet.Cells[k + 2, 8].Value;
                        var bc = worksheet.Cells[k + 2, 10].Value;


                        if (x == null || y == null || ph1 == null || ph2 == null || ph3 == null || mad == null || phase == null)
                        {
                            fileCorruption = true;
                            ebsd_Points[_x, _y] = new EBSD_Point(0, 0, 0, 0, 0, 0, 0, 0);
                        }
                        else
                        {
                            float X = (float)(double)x;
                            float Y = (float)(double)y;

                            float Ph1 = (float)(double)ph1;
                            float Ph2 = (float)(double)ph2;
                            float Ph3 = (float)(double)ph3;

                            float Mad = (float)(double)mad;
                            int Bc = Convert.ToInt32(bc);
                            int Phase = Convert.ToInt32(phase);

                            ebsd_Points[_x, _y] = new EBSD_Point(X, Y, Ph1, Ph2, Ph3, Mad, Bc, Phase);
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

        // Save
        #region File_Saving
        private void ToJson(Analyzer_Data data, string path)
        {
            string json = JsonConvert.SerializeObject(data);
            using (StreamWriter sw = new StreamWriter(path))
            { sw.Write(json); }
        }
        private Analyzer_Data? FromJson(string path)
        {
            string json;
            Analyzer_Data? data = null;
            try
            {
                using (StreamReader sr = new StreamReader(path))
                { json = sr.ReadToEnd(); }
                data = JsonConvert.DeserializeObject<Analyzer_Data>(json);
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка чтения файла! \n {ex.ToString()}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }

            return data;
        }

        #endregion File_Saving

        // Processing
        #region Processing
        private void OpenEbsdFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return; // Bad Path...
            try
            {
                BackgroundWorker worker = new BackgroundWorker();
                worker.WorkerReportsProgress = true;
                worker.DoWork += ReadEBSDExcel;
                worker.ProgressChanged += OnWorkProgressChanged;
                worker.RunWorkerCompleted += OnWorkComplete;

                worker.RunWorkerAsync(new ExcelPackage(new System.IO.FileInfo(path)));
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка чтения файла! \n {ex.ToString()}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void UpdateImage()
        {
            if (analyzer.Data.Ebsd_points == null) return; // No data

            int width = analyzer.Data.Width;
            int height = analyzer.Data.Height;

            var colors = analyzer.GetColorMap((MapVariants)MapVariantChoose.SelectedItem); // gpu work 

            if (ShowGrainMaskCheckbox.IsChecked == true)
            {
                float mot;
                if (float.TryParse(MissOrientationTreshold.Text, out mot))
                {
                    colors = analyzer.ApplyGrainFilter(colors, mot);
                }
            }
            Bitmap bmp = ByteArrayToBitmap(colors, width, height);
            EBSD_Image.Source = CreateBitmapSourceFromBitmap(bmp);

            if (!File.Exists(Title + ".png"))
            {
                bmp.Save(Title + ".png");
            }

            FileInfo fileInfo = new FileInfo() { dataPath = Title + ".ebsd", image = BitmapFrame.Create(new Uri(Path.GetFullPath(Title + ".png"))), name = Title };
            if (FilesHistory.FirstOrDefault(x => x.name == fileInfo.name) == null)
            {
                FilesHistory.Add(fileInfo);
                SaveFilesHistory(historyPath, FilesHistory.ToArray());
            }

            FilesList.ItemsSource = FilesHistory;
            FilesList.Items.Refresh();
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
                analyzer.Data = new Analyzer_Data((EBSD_Point[,])e.Result);

                //                    Имя текущего открытого файла
                ToJson(analyzer.Data, Title + ".ebsd");

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

        private void ExtrapolateButton_Click(object sender, RoutedEventArgs e)
        {
            if (analyzer.Data.Ebsd_points == null || analyzer == null) { MessageBox.Show("Не с чем работать"); return; }
            analyzer.Extrapolate((int)ExtrapolateSlider.Value);

            UpdateImage();
        }

        private void EBSD_Image_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            System.Windows.Point newPosition = e.GetPosition(EBSD_Image);

            BitmapSource bitmapSource = EBSD_Image.Source as BitmapSource;
            if (bitmapSource != null)
            {
                int x = (int)newPosition.X;
                int y = (int)newPosition.Y;

                xLable.Content = "X: " + x;
                yLable.Content = "Y: " + y;

                if (x <= analyzer.Data.Width - 1 && x >= 0 && y <= analyzer.Data.Height - 1 && y >= 0)
                {
                    Euler pointOrientation = analyzer.Data.Eulers[x + y * analyzer.Data.Width];

                    cube_xRotation.Angle = pointOrientation.X;
                    cube_yRotation.Angle = pointOrientation.Y;
                    cube_zRotation.Angle = pointOrientation.Z;
                }
            }
        }

        bool increase = false;
        double scale = 1.03125d;
        double ZoomFactor = 1;
        double minZoom = 1;
        double maxZoom = 10;
        private void EBSD_Image_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            e.Handled = true;


            ZoomFactor = ImageZoomTransform.ScaleX;
            if (e.Delta < 0)
            {
                increase = false;
                ZoomFactor /= scale;
            }
            else
            {
                increase = true;
                ZoomFactor *= scale;
            }

            ZoomFactor = Math.Clamp(ZoomFactor, minZoom, maxZoom);
            ImageSizeLabel.Content = "Size: " + Math.Round(ZoomFactor * 100, 0) + "%";

            Zoom(ZoomFactor);
        }

        private void Zoom(double zoom)
        {
            ImageZoomTransform.ScaleX = zoom;
            ImageZoomTransform.ScaleY = zoom;

            AdjustScroll();
        }


        private void AdjustScroll()
        {

            if (increase)
            {
                scroll.ScrollToHorizontalOffset((scroll.HorizontalOffset) * scale);
                scroll.ScrollToVerticalOffset((scroll.VerticalOffset) * scale);
            }
            else
            {
                scroll.ScrollToHorizontalOffset((scroll.HorizontalOffset) / scale);
                scroll.ScrollToVerticalOffset((scroll.VerticalOffset) / scale);
            }

        }
        private void GrainsDefineButton_Click(object sender, RoutedEventArgs e)
        {
            float mot;
            if (!float.TryParse(MissOrientationTreshold.Text, out mot)) return;

            analyzer.RecalculateGrains(mot);
            // и т.д

            GrainsInfoList.ItemsSource = analyzer.Data.Grains;

            int[] phases = analyzer.Data.Grains.Select(x => x.phase).Distinct().ToArray();

            List<ColumnItem> items = new List<ColumnItem>();
            foreach (int phase in phases)
            {
                items.Add(new ColumnItem(analyzer.Data.Grains.Where(x => x.phase == phase).Average(x => x.size)));
            }


            var categoryAxis = new OxyPlot.Wpf.CategoryAxis { Position = AxisPosition.Bottom, AbsoluteMaximum = 10, AbsoluteMinimum = -10 };
            categoryAxis.ItemsSource = phases;

            var valueAxis = new OxyPlot.Wpf.LinearAxis { Position = AxisPosition.Left, MinimumPadding = 0, MaximumPadding = 1f, AbsoluteMinimum = 0, AbsoluteMaximum = items.Max(x => x.Value) + 500 };


            GrainSizeChart.Axes.Clear();
            GrainSizeChart.Axes.Add(categoryAxis);
            GrainSizeChart.Axes.Add(valueAxis);

            GrainSizeChart.Series[0].ItemsSource = items;

            UpdateImage();
        }


        private static readonly Regex _regex = new Regex("[^0-9,-]+"); //regex that matches disallowed text
        private static bool IsTextAllowed(string text)
        {
            return !_regex.IsMatch(text);
        }

        private void MissOrientationTreshold_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
        }

        private void MissOrientationTreshold_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateImage();
        }

        private void ShowGrainMaskCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            UpdateImage();
        }

        private void ShowGrainMaskCheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateImage();
        }


        private void EBSD_Image_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {

            if (analyzer == null || analyzer.Data.Grains.Count == 0) return;

            System.Windows.Point pt = e.GetPosition(EBSD_Image);
            Grain grain = analyzer.Data.Grains.FirstOrDefault(x => (x.Points.Contains(new System.Numerics.Vector2((int)pt.X, (int)pt.Y)) || x.Edges.Contains(new System.Numerics.Vector2((int)pt.X, (int)pt.Y))));

            SelectGrain(grain);

            if (GrainsInfoList.Items.Contains(grain))
            {
                GrainsInfoList.SelectedItem = grain;
                GrainsInfoList.ScrollIntoView(GrainsInfoList.SelectedItem);
            }

        }


        private void SelectGrain(Grain grain)
        {
            if (grain.Edges == null || grain.Points == null) return;

            UpdateImage();

            var d = new DataObject(DataFormats.Bitmap, EBSD_Image.Source, true);
            var bmp = d.GetData("System.Drawing.Bitmap") as System.Drawing.Bitmap;

            foreach (var p in grain.Edges)
            {
                bmp.SetPixel((int)p.X, (int)p.Y, Color.White);
            }

            EBSD_Image.Source = CreateBitmapSourceFromBitmap(bmp);
        }

        private void FilesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count <= 0) return;
            var item = e.AddedItems[0];
            if (item != null)
            {
                FileInfo info = item as FileInfo;
                Analyzer_Data? data = FromJson(info.dataPath);
                if (data == null)
                {
                    MessageBox.Show("Не удаётся открыть!");
                    FilesHistory.Remove(info);
                    SaveFilesHistory(historyPath, FilesHistory.ToArray());
                    FilesList.ItemsSource = FilesList.ItemsSource.OfType<FileInfo>().Where(x => x != info);
                    return;
                }
                analyzer.Data = (Analyzer_Data)data;
                Title = info.name;
                GrainsInfoList.ItemsSource = null;
                UpdateImage();
            }
        }
        private void GrainsInfoList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
                SelectGrain((Grain)e.AddedItems[0]);
        }

        private void GrainTableIdSortButton_Click(object sender, RoutedEventArgs e)
        {
            GrainsInfoList.ItemsSource = GrainsInfoList.ItemsSource.OfType<Grain>().OrderBy(x => x.id);
        }

        private void GrainTableSqrSortButton_Click(object sender, RoutedEventArgs e)
        {
            GrainsInfoList.ItemsSource = GrainsInfoList.ItemsSource.OfType<Grain>().OrderBy(x => x.size);
        }

        private void GrainTablePhaseSortButton_Click(object sender, RoutedEventArgs e)
        {
            GrainsInfoList.ItemsSource = GrainsInfoList.ItemsSource.OfType<Grain>().OrderBy(x => x.phase);
        }

        #endregion Events

        // Helpers
        #region Helpers
        public static Bitmap ByteArrayToBitmap(byte[] bytes, int width, int height)
        {
            if (bytes == null) return null;
            Bitmap bmp = new Bitmap(width, height);
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            System.Drawing.Imaging.BitmapData bmpData =
                bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            IntPtr ptr = bmpData.Scan0;
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, bytes.Length);
            bmp.UnlockBits(bmpData);

            return bmp;
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



        #endregion Helpers



        // Из интернета ----------------------------------------------------




    }








}
