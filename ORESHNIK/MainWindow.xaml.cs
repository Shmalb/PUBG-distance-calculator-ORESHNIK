using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WpfPoint = System.Windows.Point;

namespace ORESHNIK
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private enum Mode { Idle, Calibration, Measurement }
        private Mode currentMode = Mode.Idle;

        private WpfPoint? calibrationPoint1 = null;
        private WpfPoint? calibrationPoint2 = null;
        private double calibrationPixelDistance = 0;

        private WpfPoint? measurePoint1 = null;
        private WpfPoint? measurePoint2 = null;

        private const double CALIBRATION_METERS = 100.0;

        private ScreenOverlay overlayWindow = null;

        private Key measureHotkey = Key.F9;
        private Key clearHotkey = Key.F10;
        private Key calibrationHotkey = Key.F8;
        private bool isChangingMeasureKey = false;
        private bool isChangingClearKey = false;
        private bool isChangingCalibrationKey = false;

        private DispatcherTimer topmostTimer;
        private IntPtr windowHandle;
        private Forms.NotifyIcon trayIcon;
        private bool isRussian = true;

        // Отслеживание состояния текстов
        private enum TextState { Default, CalibrationPoint, MeasurementPoint, Result, Cleared }
        private TextState txtResultState = TextState.Default;
        private TextState txtInstructionState = TextState.Default;

        public MainWindow()
        {
            InitializeComponent();

            this.Topmost = true;
            this.Left = SystemParameters.PrimaryScreenWidth - this.Width - 20;
            this.Top = 20;

            this.Loaded += MainWindow_Loaded;
            this.Deactivated += MainWindow_Deactivated;
            this.StateChanged += MainWindow_StateChanged;

            InitializeTrayIcon();
            UpdateCalibrationStatus();
            LoadSettings();
            RegisterHotkeys();
            StartTopmostTimer();
        }

        private void InitializeTrayIcon()
        {
            try
            {
                trayIcon = new Forms.NotifyIcon();
                trayIcon.Icon = System.Drawing.SystemIcons.Application;
                trayIcon.Visible = false;
                trayIcon.Text = "PUBG Distance Calculator";

                Forms.ContextMenuStrip contextMenu = new Forms.ContextMenuStrip();

                Forms.ToolStripMenuItem showItem = new Forms.ToolStripMenuItem("Показать");
                showItem.Click += (s, e) => ShowWindow();
                contextMenu.Items.Add(showItem);

                Forms.ToolStripMenuItem hideItem = new Forms.ToolStripMenuItem("Свернуть");
                hideItem.Click += (s, e) => this.WindowState = WindowState.Minimized;
                contextMenu.Items.Add(hideItem);

                contextMenu.Items.Add(new Forms.ToolStripSeparator());

                Forms.ToolStripMenuItem exitItem = new Forms.ToolStripMenuItem("Выход");
                exitItem.Click += (s, e) => CloseApplication();
                contextMenu.Items.Add(exitItem);

                trayIcon.ContextMenuStrip = contextMenu;
                trayIcon.DoubleClick += (s, e) => ShowWindow();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Не удалось создать иконку трея: " + ex.Message);
                trayIcon = null;
            }
        }

        private void ShowWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.ShowInTaskbar = true;
            this.Topmost = true;
            this.Activate();
            ForceTopmost();
        }

        private void CloseApplication()
        {
            if (topmostTimer != null)
            {
                topmostTimer.Stop();
            }

            UnregisterHotkeys();
            if (overlayWindow != null)
            {
                overlayWindow.Close();
            }

            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }

            System.Windows.Application.Current.Shutdown();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            windowHandle = new WindowInteropHelper(this).Handle;
            ForceTopmost();
        }

        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            if (this.WindowState != WindowState.Minimized)
            {
                this.Topmost = true;
                ForceTopmost();
            }
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                // ОСТАВЛЯЕМ окно в панели задач для восстановления
                this.ShowInTaskbar = true;

                if (trayIcon != null)
                {
                    trayIcon.Visible = true;
                    try
                    {
                        trayIcon.ShowBalloonTip(2000, "PUBG Distance Calculator",
                            "Приложение свернуто. Кликните на окно в панели задач для восстановления.",
                            Forms.ToolTipIcon.Info);
                    }
                    catch { }
                }
            }
            else
            {
                this.ShowInTaskbar = true;
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                }
                this.Topmost = true;
                ForceTopmost();
            }
        }

        private void ForceTopmost()
        {
            if (windowHandle != IntPtr.Zero && this.WindowState != WindowState.Minimized)
            {
                SetWindowPos(windowHandle, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }

        private void StartTopmostTimer()
        {
            topmostTimer = new DispatcherTimer();
            topmostTimer.Interval = TimeSpan.FromMilliseconds(500);
            topmostTimer.Tick += (s, e) =>
            {
                if (!this.IsActive && this.WindowState != WindowState.Minimized)
                {
                    this.Topmost = true;
                    ForceTopmost();
                }
            };
            topmostTimer.Start();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            CloseApplication();
        }

        private void BtnApplyCalibration_Click(object sender, RoutedEventArgs e)
        {
            double pixelValue;
            if (double.TryParse(txtCalibrationPx.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out pixelValue) && pixelValue > 0)
            {
                calibrationPixelDistance = pixelValue;
                UpdateCalibrationStatus();
                SaveSettings();

                txtInstruction.Text = string.Format("Калибровка установлена! Нажмите {0} для измерения расстояний", measureHotkey);
            }
            else
            {
                MessageBox.Show("Введите корректное положительное число!",
                               "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearMarkers();
        }

        private void ClearMarkers()
        {
            calibrationPoint1 = null;
            calibrationPoint2 = null;
            measurePoint1 = null;
            measurePoint2 = null;

            if (currentMode != Mode.Calibration)
            {
                currentMode = Mode.Idle;
            }

            txtResult.ClearValue(TextBlock.TextProperty);
            txtResultState = TextState.Cleared;

            if (calibrationPixelDistance > 0)
            {
                string template = (string)System.Windows.Application.Current.Resources["LinesCleared"];
                txtInstruction.Text = string.Format(template, measureHotkey);
                txtInstructionState = TextState.Default;
            }
            else
            {
                txtInstruction.Text = (string)System.Windows.Application.Current.Resources["LineClearedNoCalib"];
                txtInstructionState = TextState.Default;
            }

            if (overlayWindow != null)
            {
                overlayWindow.ClearMarkers();
            }
        }

        private void RegisterHotkeys()
        {
            if (overlayWindow == null || !overlayWindow.IsVisible)
            {
                overlayWindow = new ScreenOverlay(measureHotkey, clearHotkey, calibrationHotkey);
                overlayWindow.OnMeasureRequested += OverlayWindow_OnMeasureRequested;
                overlayWindow.OnClearRequested += OverlayWindow_OnClearRequested;
                overlayWindow.OnCalibrationRequested += OverlayWindow_OnCalibrationRequested;
                overlayWindow.Show();
            }
            else
            {
                overlayWindow.UpdateHotkeys(measureHotkey, clearHotkey, calibrationHotkey);
            }
        }

        private void UnregisterHotkeys()
        {
            if (overlayWindow != null)
            {
                overlayWindow.OnMeasureRequested -= OverlayWindow_OnMeasureRequested;
                overlayWindow.OnClearRequested -= OverlayWindow_OnClearRequested;
                overlayWindow.OnCalibrationRequested -= OverlayWindow_OnCalibrationRequested;
                overlayWindow.Close();
                overlayWindow = null;
            }
        }

        private void OverlayWindow_OnMeasureRequested(WpfPoint screenPoint)
        {
            this.Dispatcher.Invoke(() =>
            {
                if (calibrationPixelDistance <= 0)
                {
                    MessageBox.Show("Сначала выполните калибровку!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                HandleMeasurementPoint(screenPoint);
            });
        }

        private void OverlayWindow_OnCalibrationRequested(WpfPoint screenPoint)
        {
            this.Dispatcher.Invoke(() =>
            {
                if (currentMode != Mode.Calibration)
                {
                    currentMode = Mode.Calibration;
                    calibrationPoint1 = null;
                    calibrationPoint2 = null;
                    txtInstruction.Text = string.Format("Нажмите {0} на первой точке расстояния 100м в игре", calibrationHotkey);
                    txtResult.Text = "Калибровка: точка 1/2";
                }

                HandleCalibrationPoint(screenPoint);
            });
        }

        private void OverlayWindow_OnClearRequested()
        {
            this.Dispatcher.Invoke(() => ClearMarkers());
        }

        private void HandleCalibrationPoint(WpfPoint point)
        {
            if (calibrationPoint1 == null)
            {
                calibrationPoint1 = point;
                overlayWindow.AddMarker(point, Brushes.Yellow, "1");
                string template = (string)System.Windows.Application.Current.Resources["CalibrationPoint2"];
                txtInstruction.Text = string.Format(template, calibrationHotkey);
                txtInstructionState = TextState.CalibrationPoint;
                string labelTemplate = (string)System.Windows.Application.Current.Resources["CalibrationPointLabel"];
                txtResult.Text = labelTemplate;
                txtResultState = TextState.CalibrationPoint;
            }
            else if (calibrationPoint2 == null)
            {
                calibrationPoint2 = point;
                overlayWindow.AddMarker(point, Brushes.Yellow, "2");
                overlayWindow.AddLine(calibrationPoint1.Value, calibrationPoint2.Value, Brushes.Yellow, CALIBRATION_METERS);

                calibrationPixelDistance = CalculateDistance(calibrationPoint1.Value, calibrationPoint2.Value);
                txtCalibrationPx.Text = string.Format("{0:F1}", calibrationPixelDistance);

                UpdateCalibrationStatus();
                SaveSettings();

                string doneTemplate = (string)System.Windows.Application.Current.Resources["CalibrationDone"];
                txtResult.Text = string.Format(doneTemplate, calibrationPixelDistance);
                txtResultState = TextState.Result;
                string completedTemplate = (string)System.Windows.Application.Current.Resources["CalibrationCompleted"];
                txtInstruction.Text = string.Format(completedTemplate, measureHotkey);
                txtInstructionState = TextState.Default;

                currentMode = Mode.Idle;
            }
        }

        private void HandleMeasurementPoint(WpfPoint point)
        {
            if (measurePoint1 == null)
            {
                measurePoint1 = point;
                overlayWindow.AddMarker(point, Brushes.Lime, "A");
                string template = (string)System.Windows.Application.Current.Resources["MeasurementPoint2"];
                txtInstruction.Text = string.Format(template, measureHotkey);
                txtInstructionState = TextState.MeasurementPoint;
                string labelTemplate = (string)System.Windows.Application.Current.Resources["MeasurementPointLabel"];
                txtResult.Text = labelTemplate;
                txtResultState = TextState.MeasurementPoint;
            }
            else if (measurePoint2 == null)
            {
                measurePoint2 = point;
                overlayWindow.AddMarker(point, Brushes.Lime, "B");

                double pixelDistance = CalculateDistance(measurePoint1.Value, measurePoint2.Value);
                double meters = (pixelDistance / calibrationPixelDistance) * CALIBRATION_METERS;

                overlayWindow.AddLine(measurePoint1.Value, measurePoint2.Value, Brushes.Lime, meters);

                string valueTemplate = (string)System.Windows.Application.Current.Resources["DistanceValue"];
                txtResult.Text = string.Format(valueTemplate, meters);
                txtResultState = TextState.Result;
                string doneTemplate = (string)System.Windows.Application.Current.Resources["MeasurementDone"];
                txtInstruction.Text = string.Format(doneTemplate, measureHotkey, clearHotkey);
                txtInstructionState = TextState.Default;

                measurePoint1 = null;
                measurePoint2 = null;
            }
        }

        private void UpdateCalibrationStatus()
        {
            if (calibrationPixelDistance > 0)
            {
                string template = (string)System.Windows.Application.Current.Resources["CalibrationDone"];
                txtCalibrationStatus.Text = string.Format(template, calibrationPixelDistance);
                txtCalibrationStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0));
            }
            else
            {
                txtCalibrationStatus.Text = (string)System.Windows.Application.Current.Resources["CalibrationNotDone"];
                txtCalibrationStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 68, 68));
            }
        }

        private double CalculateDistance(WpfPoint p1, WpfPoint p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void BtnChangeMeasureKey_Click(object sender, RoutedEventArgs e)
        {
            isChangingMeasureKey = true;
            txtMeasureKey.Background = new SolidColorBrush(Color.FromRgb(0, 120, 215));
            txtMeasureKey.Text = "Нажмите клавишу...";
            txtMeasureKey.Focus();
        }

        private void BtnChangeClearKey_Click(object sender, RoutedEventArgs e)
        {
            isChangingClearKey = true;
            txtClearKey.Background = new SolidColorBrush(Color.FromRgb(0, 120, 215));
            txtClearKey.Text = "Нажмите клавишу...";
            txtClearKey.Focus();
        }

        private void BtnChangeCalibrationKey_Click(object sender, RoutedEventArgs e)
        {
            isChangingCalibrationKey = true;
            txtCalibrationKey.Background = new SolidColorBrush(Color.FromRgb(0, 120, 215));
            txtCalibrationKey.Text = "Нажмите клавишу...";
            txtCalibrationKey.Focus();
        }

        private void TxtMeasureKey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (isChangingMeasureKey && e.Key != Key.None)
            {
                measureHotkey = e.Key;
                txtMeasureKey.Text = e.Key.ToString();
                txtMeasureKey.Background = new SolidColorBrush(Color.FromRgb(26, 26, 26));
                isChangingMeasureKey = false;
                e.Handled = true;
            }
        }

        private void TxtClearKey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (isChangingClearKey && e.Key != Key.None)
            {
                clearHotkey = e.Key;
                txtClearKey.Text = e.Key.ToString();
                txtClearKey.Background = new SolidColorBrush(Color.FromRgb(26, 26, 26));
                isChangingClearKey = false;
                e.Handled = true;
            }
        }

        private void TxtCalibrationKey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (isChangingCalibrationKey && e.Key != Key.None)
            {
                calibrationHotkey = e.Key;
                txtCalibrationKey.Text = e.Key.ToString();
                txtCalibrationKey.Background = new SolidColorBrush(Color.FromRgb(26, 26, 26));
                isChangingCalibrationKey = false;
                e.Handled = true;
            }
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            RegisterHotkeys();
            MessageBox.Show(string.Format("Настройки сохранены!\n\nИзмерение: {0}\nОчистить: {1}\nКалибровка: {2}", measureHotkey, clearHotkey, calibrationHotkey),
                           "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.MeasureHotkey = measureHotkey.ToString();
            Properties.Settings.Default.ClearHotkey = clearHotkey.ToString();
            Properties.Settings.Default.CalibrationHotkey = calibrationHotkey.ToString();
            Properties.Settings.Default.CalibrationPx = calibrationPixelDistance;
            Properties.Settings.Default.Save();
        }

        private void LoadSettings()
        {
            string measureKeyStr = Properties.Settings.Default.MeasureHotkey;
            string clearKeyStr = Properties.Settings.Default.ClearHotkey;
            string calibrationKeyStr = Properties.Settings.Default.CalibrationHotkey;

            if (!string.IsNullOrEmpty(measureKeyStr))
            {
                Key parsedKey;
                if (Enum.TryParse(measureKeyStr, out parsedKey))
                {
                    measureHotkey = parsedKey;
                    txtMeasureKey.Text = measureHotkey.ToString();
                }
            }

            if (!string.IsNullOrEmpty(clearKeyStr))
            {
                Key parsedKey;
                if (Enum.TryParse(clearKeyStr, out parsedKey))
                {
                    clearHotkey = parsedKey;
                    txtClearKey.Text = clearHotkey.ToString();
                }
            }

            if (!string.IsNullOrEmpty(calibrationKeyStr))
            {
                Key parsedKey;
                if (Enum.TryParse(calibrationKeyStr, out parsedKey))
                {
                    calibrationHotkey = parsedKey;
                    txtCalibrationKey.Text = calibrationHotkey.ToString();
                }
            }

            double savedCalibration = Properties.Settings.Default.CalibrationPx;
            if (savedCalibration > 0)
            {
                calibrationPixelDistance = savedCalibration;
                txtCalibrationPx.Text = string.Format("{0:F1}", savedCalibration);
                UpdateCalibrationStatus();
                string template = (string)System.Windows.Application.Current.Resources["CalibrationLoaded"];
                txtInstruction.Text = string.Format(template, measureHotkey);
            }
        }

        private void txtCalibrationPx_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void BtnChangeLanguage_Click(object sender, RoutedEventArgs e)
        {
            isRussian = !isRussian;
            ChangeLanguage();
        }

        private void ChangeLanguage()
        {
            ResourceDictionary dictionary = new ResourceDictionary();
            if (isRussian)
            {
                dictionary.Source = new Uri("Resources/Dictionary.ru-RU.xaml", UriKind.Relative);
            }
            else
            {
                dictionary.Source = new Uri("Resources/Dictionary.en-US.xaml", UriKind.Relative);
            }

            ResourceDictionary oldDictionary = null;
            foreach (ResourceDictionary dict in System.Windows.Application.Current.Resources.MergedDictionaries)
            {
                if (dict.Source != null && (dict.Source.OriginalString.Contains("Dictionary.ru-RU") || 
                    dict.Source.OriginalString.Contains("Dictionary.en-US")))
                {
                    oldDictionary = dict;
                    break;
                }
            }

            if (oldDictionary != null)
            {
                System.Windows.Application.Current.Resources.MergedDictionaries.Remove(oldDictionary);
            }

            System.Windows.Application.Current.Resources.MergedDictionaries.Add(dictionary);

            // Обновляем динамические тексты
            UpdateDynamicTexts();
        }

        private void UpdateDynamicTexts()
        {
            // Восстанавливаем txtResult в зависимости от состояния
            if (txtResultState == TextState.CalibrationPoint)
            {
                string labelTemplate = (string)System.Windows.Application.Current.Resources["CalibrationPointLabel"];
                txtResult.Text = labelTemplate;
            }
            else if (txtResultState == TextState.MeasurementPoint)
            {
                string labelTemplate = (string)System.Windows.Application.Current.Resources["MeasurementPointLabel"];
                txtResult.Text = labelTemplate;
            }
            else if (txtResultState == TextState.Result)
            {
                // Если результат - переустанавливаем его значение
                // Это будет сделано в других методах при необходимости
            }
            else if (txtResultState == TextState.Cleared)
            {
                txtResult.Text = (string)System.Windows.Application.Current.Resources["Distance"];
            }
            else
            {
                txtResult.Text = (string)System.Windows.Application.Current.Resources["Distance"];
            }

            // Восстанавливаем txtInstruction
            if (txtInstructionState == TextState.CalibrationPoint)
            {
                string template = (string)System.Windows.Application.Current.Resources["CalibrationPoint2"];
                txtInstruction.Text = string.Format(template, calibrationHotkey);
            }
            else if (txtInstructionState == TextState.MeasurementPoint)
            {
                string template = (string)System.Windows.Application.Current.Resources["MeasurementPoint2"];
                txtInstruction.Text = string.Format(template, measureHotkey);
            }
            else
            {
                txtInstruction.Text = (string)System.Windows.Application.Current.Resources["Instructions"];
            }

            // Обновляем статус калибровки
            UpdateCalibrationStatus();
        }
    }
}