using System;
using System.Globalization;
using System.Windows.Data;

namespace Busca_BT.Infrastructure.Converters
{
    public sealed class StringNotEmptyToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
                return !string.IsNullOrWhiteSpace(s);
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
