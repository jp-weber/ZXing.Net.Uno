using Microsoft.UI;

namespace ZXing.Net.Uno.Controls
{
    [TemplatePart(Name = MainGridName, Type = typeof(Grid))]
    public sealed partial class BarcodeGeneratorControl : Control, IBarcodeGeneratorControl
    {
        private readonly ZXing.Net.Uno.Barcode.BarcodeWriter _barcodeWriter = new();
        private const string MainGridName = "MainGrid";
        Grid MainGrid;
        NativePlatformImageView _imageView;
        public BarcodeGeneratorControl()
        {
            this.DefaultStyleKey = typeof(BarcodeGeneratorControl);
        }

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            MainGrid = (Grid)GetTemplateChild(MainGridName);
            _imageView = CreatePlatformImage();
            CreateBarcode();
        }

        protected NativePlatformImageView CreatePlatformImage()
        {
#if IOS || MACCATALYST
			_imageView ??= new UIKit.UIImageView { BackgroundColor = UIKit.UIColor.Clear };
#elif ANDROID
            _imageView = new NativePlatformImageView(ContextHelper.Current);
            _imageView.SetBackgroundColor(Android.Graphics.Color.Transparent);
#elif WINDOWS
			_imageView = new NativePlatformImageView();
#endif
            return _imageView;
        }

        private void CreateBarcode()
        {
            if (MainGrid != null && _barcodeWriter != null)
            {
                _barcodeWriter.Format = Format.ToZXingList().FirstOrDefault();
                _barcodeWriter.Options.Width = (int)this.Width;
                _barcodeWriter.Options.Height = (int)this.Height;
                _barcodeWriter.Options.Margin = BarcodeMargin;
                if (Foreground is SolidColorBrush)
                {
                    _barcodeWriter.ForegroundColor = (Foreground as SolidColorBrush).Color;
                }
                else
                {
                    _barcodeWriter.ForegroundColor = Colors.Black;
                }
                if (Background is SolidColorBrush)
                {
                    _barcodeWriter.BackgroundColor = (Background as SolidColorBrush).Color;
                }
                else
                {
                    _barcodeWriter.BackgroundColor = Colors.White;
                }

                NativePlatformImage image = null;
                if (!string.IsNullOrWhiteSpace(Value))
                {
                    image = _barcodeWriter?.Write(Value);

                    if (MainGrid.Children.Count > 0)
                    {
                        MainGrid.Children.Clear();
                    }

#if IOS || MACCATALYST
			    _imageView.Image = image;
                MainGrid.Children.Add(new ContentControl
                {
                    Content = _imageView,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch
                });
#elif ANDROID
                _imageView?.SetImageBitmap(image);
                MainGrid.Children.Add(new ContentControl
                {
                    Content = _imageView,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch
                });
#elif WINDOWS
			    _imageView.Source = image;
                MainGrid.Children.Add(_imageView);
#endif

                }
            }
        }

        public static readonly DependencyProperty FormatProperty =
            DependencyProperty.Register(
                nameof(Format),
                typeof(BarcodeFormat),
                typeof(BarcodeGeneratorControl),
                new PropertyMetadata(BarcodeFormat.QR_CODE));

        public BarcodeFormat Format
        {
            get => (BarcodeFormat)GetValue(FormatProperty);
            set => SetValue(FormatProperty, value);
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value),
                typeof(string),
                typeof(BarcodeGeneratorControl),
                new PropertyMetadata(string.Empty, BarcodeValueChanged));

        private static void BarcodeValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace((string)args.NewValue))
            {
                (dependencyObject as BarcodeGeneratorControl).CreateBarcode();
            }
        }

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }


        //static readonly DependencyProperty ForegroundColorProperty =
        //    DependencyProperty.Register(
        //        nameof(ForegroundColor),
        //        typeof(Color),
        //        typeof(BarcodeGeneratorControl),
        //        new PropertyMetadata(Colors.Black));

        //public Color ForegroundColor
        //{
        //    get => (Color)GetValue(ForegroundColorProperty);
        //    private set => SetValue(ForegroundColorProperty, value);
        //}

        //static readonly DependencyProperty BackgroundColorProperty =
        //    DependencyProperty.Register(
        //        nameof(BackgroundColor),
        //        typeof(Color),
        //        typeof(BarcodeGeneratorControl),
        //        new PropertyMetadata(Colors.White));


        //public Color BackgroundColor
        //{
        //    get => (Color)GetValue(BackgroundColorProperty);
        //    private set => SetValue(BackgroundColorProperty, value);
        //}

        public static readonly DependencyProperty BarcodeMarginProperty =
            DependencyProperty.Register(
                nameof(BarcodeMargin),
                typeof(int),
                typeof(BarcodeGeneratorControl),
                new PropertyMetadata(1));


        public int BarcodeMargin
        {
            get => (int)GetValue(BarcodeMarginProperty);
            set => SetValue(BarcodeMarginProperty, value);
        }
    }
}
