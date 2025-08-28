using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace CustomWindow.Converters;

public class BoolToSuccessColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            // 성공(true)이면 녹색, 실패(false)면 빨간색
            var color = boolValue ? Color.FromArgb(255, 16, 124, 16) : Color.FromArgb(255, 196, 43, 28);
            return new SolidColorBrush(color);
        }
        
        // 기본값은 회색
        return new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}