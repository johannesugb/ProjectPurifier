using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace ProjectPurifier.Converter
{
	class BoolToCollapsedConverter : IValueConverter
	{
		public bool Invert { get; set; }

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if ((bool) value)
				return Invert ? Visibility.Collapsed : Visibility.Visible;
			else
				return Invert ? Visibility.Visible : Visibility.Collapsed;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
