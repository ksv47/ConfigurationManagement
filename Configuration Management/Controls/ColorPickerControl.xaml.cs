#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Configuration_Management.Controls
{
    /// <summary>
    /// Переиспользуемый контрол выбора цвета (WPF) в HSV-модели:
    /// градиентная палитра «оттенок × насыщенность» с маркером, бегунок яркости,
    /// RGB-слайдеры, HEX-поле и расширенная палитра предустановленных цветов.
    /// Выбранный цвет доступен через зависимое свойство <see cref="SelectedColor"/>.
    /// Используется в <see cref="ColorPickerWindow"/> и вкладках окна настройки группы.
    /// </summary>
    public partial class ColorPickerControl : UserControl
    {
        // Расширенная палитра предустановленных цветов (73 шт., компактные плитки).
        private static readonly string[] PaletteColors =
        {
            "#000000", "#212121", "#3F3F3F", "#5C5C5C", "#7F7F7F", "#9E9E9E", "#BFBFBF", "#FFFFFF",
            "#7F1D1D", "#991B1B", "#DC2626", "#EF4444", "#F87171", "#FCA5A5", "#FFC2C2",
            "#7C2D12", "#C2410C", "#EA580C", "#F97316", "#FB923C", "#FDBA74", "#FED7AA",
            "#713F12", "#A16207", "#D97706", "#F59E0B", "#FBBF24", "#FDE047",
            "#14532D", "#166534", "#15803D", "#16A34A", "#22C55E", "#4ADE80", "#86EFAC", "#BBF7D0",
            "#134E4A", "#115E59", "#0F766E", "#0D9488", "#14B8A6", "#2DD4BF", "#5EEAD4",
            "#164E63", "#0E7490", "#0891B2", "#06B6D4", "#22D3EE", "#67E8F9",
            "#172554", "#1E3A8A", "#1D4ED8", "#2563EB", "#3B82F6", "#60A5FA", "#93C5FD",
            "#312E81", "#3730A3", "#4338CA", "#4F46E5", "#6366F1", "#A5B4FC",
            "#581C87", "#7E22CE", "#9333EA", "#A855F7", "#C084FC", "#D8B4FE",
            "#831843", "#BE185D", "#DB2777", "#EC4899", "#F472B6", "#F9A8D4"
        };

        private bool _isUpdating;

        private double _hue;
        private double _saturation = 100;
        private double _brightness = 100;

        /// <summary>
        /// Выбранный цвет в формате #RRGGBB. Обновляется при любом изменении пикера.
        /// </summary>
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(
                nameof(SelectedColor),
                typeof(string),
                typeof(ColorPickerControl),
                new FrameworkPropertyMetadata("#2D6CDF", OnSelectedColorChanged));

        public string SelectedColor
        {
            get => (string)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        /// <summary>Возникает при изменении выбранного цвета (при любом взаимодействии с пикером).</summary>
        public event EventHandler? SelectedColorChanged;

        public ColorPickerControl()
        {
            InitializeComponent();
            BuildPalette();
            RainbowLayer.Background = BuildRainbowBrush();
            SaturationLayer.Background = BuildSaturationOverlay();

            PaletteArea.MouseLeftButtonDown += OnPalette_MouseDown;
            PaletteArea.MouseMove += OnPalette_MouseMove;
            PaletteArea.MouseLeftButtonUp += OnPalette_MouseUp;

            SetColor(ParseColor(SelectedColor));
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ColorPickerControl c && !c._isUpdating && e.NewValue is string s)
                c.SetColor(ParseColor(s));
        }

        /// <summary>Строит плитки предустановленных цветов в сетку.</summary>
        private void BuildPalette()
        {
            PaletteGrid.Children.Clear();
            foreach (var hex in PaletteColors)
            {
                var button = new Button
                {
                    Style = (Style)FindResource("PaletteColorButton"),
                    Tag = hex,
                    Background = new SolidColorBrush(ParseColor(hex))
                };
                button.Click += (_, _) => SetColor(ParseColor(hex));
                PaletteGrid.Children.Add(button);
            }
        }

        private static LinearGradientBrush BuildRainbowBrush()
        {
            var brush = new LinearGradientBrush(new GradientStopCollection(), 0);
            brush.StartPoint = new Point(0, 0.5);
            brush.EndPoint = new Point(1, 0.5);
            for (var i = 0; i <= 6; i++)
                brush.GradientStops.Add(new GradientStop(FromHsv(i * 60.0, 1.0, 1.0), i / 6.0));
            return brush;
        }

        private static LinearGradientBrush BuildSaturationOverlay()
        {
            var brush = new LinearGradientBrush(new GradientStopCollection(), 90);
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(0, 1);
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 1));
            return brush;
        }

        private void SetColor(Color color)
        {
            var (h, s, v) = ToHsv(color);
            _hue = h;
            _saturation = s * 100.0;
            _brightness = v * 100.0;

            _isUpdating = true;
            try
            {
                BrightnessSlider.Value = _brightness;
            }
            finally
            {
                _isUpdating = false;
            }

            ApplyFromHsv();
        }

        private Color CurrentColor => FromHsv(_hue, _saturation / 100.0, _brightness / 100.0);

        private void ApplyFromHsv()
        {
            _isUpdating = true;
            try
            {
                var color = CurrentColor;

                RedSlider.Value = color.R;
                GreenSlider.Value = color.G;
                BlueSlider.Value = color.B;
                RedValue.Text = color.R.ToString();
                GreenValue.Text = color.G.ToString();
                BlueValue.Text = color.B.ToString();
                BrightnessValue.Text = ((int)Math.Round(_brightness)).ToString() + "%";
                HexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                ColorPreview.Background = new SolidColorBrush(color);

                // Затемнение всей палитры при снижении яркости.
                BrightnessOverlay.Opacity = 1.0 - _brightness / 100.0;

                // Публикуем текущий цвет (внутри _isUpdating изменение не вызовет повторного SetColor).
                SetValue(SelectedColorProperty, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
                SelectedColorChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _isUpdating = false;
            }

            UpdateMarker();
        }

        private void UpdateMarker()
        {
            var w = MarkerLayer.ActualWidth;
            var h = MarkerLayer.ActualHeight;
            if (w <= 0 || h <= 0)
                return;

            Canvas.SetLeft(ColorMarker, _hue / 360.0 * w - ColorMarker.Width / 2);
            Canvas.SetTop(ColorMarker, (1.0 - _saturation / 100.0) * h - ColorMarker.Height / 2);
        }

        private void OnPalette_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ApplyPointer(e.GetPosition(PaletteArea));
            PaletteArea.CaptureMouse();
        }

        private void OnPalette_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                ApplyPointer(e.GetPosition(PaletteArea));
        }

        private void OnPalette_MouseUp(object sender, MouseButtonEventArgs e)
        {
            PaletteArea.ReleaseMouseCapture();
        }

        private void ApplyPointer(Point position)
        {
            var w = PaletteArea.ActualWidth;
            var h = PaletteArea.ActualHeight;
            if (w <= 0 || h <= 0)
                return;

            _hue = Math.Clamp(position.X / w * 360.0, 0, 359.99);
            _saturation = Math.Clamp((1.0 - position.Y / h) * 100.0, 0, 100);
            ApplyFromHsv();
        }

        private void OnBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating)
                return;

            _brightness = BrightnessSlider.Value;
            ApplyFromHsv();
        }

        private void OnRgb_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating)
                return;

            var color = Color.FromRgb(
                (byte)RedSlider.Value,
                (byte)GreenSlider.Value,
                (byte)BlueSlider.Value);
            SetColor(color);
        }

        private void OnHex_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating)
                return;

            var text = HexBox.Text?.Trim() ?? string.Empty;
            if (text.Length != 7 || !text.StartsWith("#"))
                return;

            try
            {
                SetColor(ParseColor(text));
            }
            catch
            {
                // Игнорируем некорректный ввод HEX.
            }
        }

        private static Color ParseColor(string? hex)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex ?? "#2D6CDF");
            }
            catch
            {
                return (Color)ColorConverter.ConvertFromString("#2D6CDF");
            }
        }

        private static Color FromHsv(double h, double s, double v)
        {
            var hi = (int)Math.Floor(h / 60.0) % 6;
            var f = h / 60.0 - Math.Floor(h / 60.0);
            var p = v * (1 - s);
            var q = v * (1 - f * s);
            var t = v * (1 - (1 - f) * s);

            return hi switch
            {
                0 => Rgb(v, t, p),
                1 => Rgb(q, v, p),
                2 => Rgb(p, v, t),
                3 => Rgb(p, q, v),
                4 => Rgb(t, p, v),
                _ => Rgb(v, p, q)
            };
        }

        private static Color Rgb(double r, double g, double b) =>
            Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));

        private static (double H, double S, double V) ToHsv(Color c)
        {
            var r = c.R / 255.0;
            var g = c.G / 255.0;
            var b = c.B / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var d = max - min;

            double h;
            if (d == 0)
                h = 0;
            else if (max == r)
                h = 60 * (((g - b) / d) % 6);
            else if (max == g)
                h = 60 * ((b - r) / d + 2);
            else
                h = 60 * ((r - g) / d + 4);
            if (h < 0)
                h += 360;

            var s = max == 0 ? 0 : d / max;
            return (h, s, max);
        }
    }
}
#endif