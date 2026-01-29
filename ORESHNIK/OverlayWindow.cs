using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ORESHNIK
{
    public class ScreenOverlay : Window
    {
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        private const int HOTKEY_MEASURE_ID = 9000;
        private const int HOTKEY_CLEAR_ID = 9001;
        private const int HOTKEY_CALIBRATION_ID = 9002;

        private Canvas canvas;
        private IntPtr windowHandle;

        private Key measureHotkey;
        private Key clearHotkey;
        private Key calibrationHotkey;

        public delegate void MeasureRequestedHandler(Point screenPoint);
        public event MeasureRequestedHandler OnMeasureRequested;

        public delegate void ClearRequestedHandler();
        public event ClearRequestedHandler OnClearRequested;

        public delegate void CalibrationRequestedHandler(Point screenPoint);
        public event CalibrationRequestedHandler OnCalibrationRequested;

        public ScreenOverlay(Key measureKey, Key clearKey, Key calibrationKey)
        {
            measureHotkey = measureKey;
            clearHotkey = clearKey;
            calibrationHotkey = calibrationKey;
            InitializeWindow();
        }

        private void InitializeWindow()
        {
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            this.Topmost = true;
            this.ShowInTaskbar = false;
            this.ResizeMode = ResizeMode.NoResize;

            this.Left = 0;
            this.Top = 0;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;

            canvas = new Canvas();
            this.Content = canvas;

            this.Loaded += OverlayWindow_Loaded;
            this.Closed += OverlayWindow_Closed;
        }

        private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            windowHandle = new WindowInteropHelper(this).Handle;

            // Делаем окно прозрачным для мыши
            int extendedStyle = GetWindowLong(windowHandle, GWL_EXSTYLE);
            SetWindowLong(windowHandle, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);

            // Регистрируем горячие клавиши
            HwndSource source = HwndSource.FromHwnd(windowHandle);
            source.AddHook(HwndHook);
            RegisterHotkeys();
        }

        private void OverlayWindow_Closed(object sender, EventArgs e)
        {
            if (windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(windowHandle, HOTKEY_MEASURE_ID);
                UnregisterHotKey(windowHandle, HOTKEY_CLEAR_ID);
                UnregisterHotKey(windowHandle, HOTKEY_CALIBRATION_ID);
            }
        }

        public void UpdateHotkeys(Key measureKey, Key clearKey, Key calibrationKey)
        {
            measureHotkey = measureKey;
            clearHotkey = clearKey;
            calibrationHotkey = calibrationKey;

            if (windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(windowHandle, HOTKEY_MEASURE_ID);
                UnregisterHotKey(windowHandle, HOTKEY_CLEAR_ID);
                UnregisterHotKey(windowHandle, HOTKEY_CALIBRATION_ID);
                RegisterHotkeys();
            }
        }

        private void RegisterHotkeys()
        {
            uint measureVK = (uint)KeyInterop.VirtualKeyFromKey(measureHotkey);
            uint clearVK = (uint)KeyInterop.VirtualKeyFromKey(clearHotkey);
            uint calibrationVK = (uint)KeyInterop.VirtualKeyFromKey(calibrationHotkey);

            RegisterHotKey(windowHandle, HOTKEY_MEASURE_ID, 0, measureVK);
            RegisterHotKey(windowHandle, HOTKEY_CLEAR_ID, 0, clearVK);
            RegisterHotKey(windowHandle, HOTKEY_CALIBRATION_ID, 0, calibrationVK);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();

                if (hotkeyId == HOTKEY_MEASURE_ID)
                {
                    POINT cursorPos;
                    if (GetCursorPos(out cursorPos))
                    {
                        Point screenPoint = new Point(cursorPos.X, cursorPos.Y);
                        if (OnMeasureRequested != null)
                        {
                            OnMeasureRequested(screenPoint);
                        }
                    }
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_CLEAR_ID)
                {
                    if (OnClearRequested != null)
                    {
                        OnClearRequested();
                    }
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_CALIBRATION_ID)
                {
                    POINT cursorPos;
                    if (GetCursorPos(out cursorPos))
                    {
                        Point screenPoint = new Point(cursorPos.X, cursorPos.Y);
                        if (OnCalibrationRequested != null)
                        {
                            OnCalibrationRequested(screenPoint);
                        }
                    }
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void AddMarker(Point screenPoint, Brush color, string label)
        {
            // Точка
            Ellipse ellipse = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = color,
                Stroke = Brushes.White,
                StrokeThickness = 3
            };
            Canvas.SetLeft(ellipse, screenPoint.X - 8);
            Canvas.SetTop(ellipse, screenPoint.Y - 8);
            canvas.Children.Add(ellipse);

            // Метка
            TextBlock text = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 18,
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                Padding = new Thickness(6, 3, 6, 3)
            };
            Canvas.SetLeft(text, screenPoint.X + 15);
            Canvas.SetTop(text, screenPoint.Y - 25);
            canvas.Children.Add(text);
        }

        public void AddLine(Point p1, Point p2, Brush color, double distanceInMeters)
        {
            Line line = new Line
            {
                X1 = p1.X,
                Y1 = p1.Y,
                X2 = p2.X,
                Y2 = p2.Y,
                Stroke = color,
                StrokeThickness = 4
            };
            canvas.Children.Add(line);

            // Добавляем расстояние на линии
            Point midPoint = new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);

            TextBlock distanceText = new TextBlock
            {
                Text = string.Format("{0:F1} м", distanceInMeters),
                Foreground = color,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                Padding = new Thickness(5, 2, 5, 2)
            };
            Canvas.SetLeft(distanceText, midPoint.X + 10);
            Canvas.SetTop(distanceText, midPoint.Y - 10);
            canvas.Children.Add(distanceText);
        }

        public void ClearMarkers()
        {
            canvas.Children.Clear();
        }
    }
}