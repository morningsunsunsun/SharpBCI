﻿using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MarukoLib.Lang;
using SharpBCI.Extensions.Data;
using SharpBCI.Extensions.Windows;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace SharpBCI.Extensions.Presenters
{

    public class PositionPresenter : IPresenter
    {

        public static readonly NamedProperty<uint> CheckboxSizeProperty = new NamedProperty<uint>("CheckboxSize", 15);

        public static readonly NamedProperty<bool> Position1DLabelVisibilityProperty = new NamedProperty<bool>("Position1DLabelVisibility", true);

        public static readonly NamedProperty<PositionH1D> Position2DHorizontalAlignmentProperty = new NamedProperty<PositionH1D>("Position2DHorizontalAlignment", PositionH1D.Center);

        public static readonly PositionPresenter Instance = new PositionPresenter();

        public PresentedParameter Present(IParameterDescriptor param, Action updateCallback)
        {
            if (param.ValueType == typeof(Position1D) || param.ValueType == typeof(PositionH1D) || param.ValueType == typeof(PositionV1D)) return Present1D(param, updateCallback);
            if (param.ValueType == typeof(Position2D)) return Present2D(param, updateCallback);
            throw new NotSupportedException($"Unsupported value type: {param.ValueType}");
        }

        public PresentedParameter Present1D(IParameterDescriptor param, Action updateCallback)
        {
            var checkboxSize = CheckboxSizeProperty.Get(param.Metadata);
            var labelVisible = Position1DLabelVisibilityProperty.Get(param.Metadata);
            var enumNames = param.ValueType.GetEnumNames();
            var enumValues = param.ValueType.GetEnumValues();
            var checkboxes = new Rectangle[enumNames.Length];
            var selectedIndex = -1;

            void Select(int index)
            {
                if (selectedIndex == index) return;
                for (var i = 0; i < checkboxes.Length; i++)
                    checkboxes[i].Fill = i == index ? Brushes.DimGray : Brushes.White;
                selectedIndex = index;
            }

            var grid = new Grid();
            if (labelVisible)
                grid.RowDefinitions.Add(new RowDefinition {Height = GridLength.Auto}); /* Name row */
            grid.RowDefinitions.Add(new RowDefinition {Height = GridLength.Auto}); /* Checkbox row */
            for (var i = 0; i < enumNames.Length; i++)
            {
                var index = i; /* Used in closure */
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                if (labelVisible)
                {
                    var nameTextBlock = new TextBlock { Text = enumNames[index], HorizontalAlignment = HorizontalAlignment.Center, FontSize = 9 };
                    grid.Children.Add(nameTextBlock);
                    Grid.SetRow(nameTextBlock, 0);
                    Grid.SetColumn(nameTextBlock, index);
                }

                var checkbox = checkboxes[index] = new Rectangle
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(2),
                    Width = checkboxSize, Height = checkboxSize,
                    Stroke = Brushes.Black,
                    Fill = Brushes.White
                };
                checkbox.MouseLeftButtonUp += (sender, e) => Select(index);
                grid.Children.Add(checkbox);
                if (labelVisible) Grid.SetRow(checkbox, 1);
                Grid.SetColumn(checkbox, index);
            }
            void Setter(object val) => Select((int)val);
            object Getter() => enumValues.GetValue(selectedIndex);
            void Updater(ParameterStateType state, bool value)
            {
                switch (state)
                {
                    case ParameterStateType.Enabled:
                        foreach (var checkbox in checkboxes)
                            checkbox.IsEnabled = value;
                        break;
                    case ParameterStateType.Valid:
                        for (var i = 0; i < checkboxes.Length; i++)
                            checkboxes[i].Fill = value ? (i == selectedIndex ? Brushes.DimGray : Brushes.White) : new SolidColorBrush(ViewConstants.InvalidColor);
                        break;
                }
            }
            return new PresentedParameter(param, grid, new PresentedParameter.ParamDelegates(Getter, Setter, param.IsValid, Updater));
        }

        public PresentedParameter Present2D(IParameterDescriptor param, Action updateCallback)
        {
            var checkboxSize = CheckboxSizeProperty.Get(param.Metadata);
            var enumNames = param.ValueType.GetEnumNames();
            var enumValues = param.ValueType.GetEnumValues();
            var checkboxes = new Rectangle[enumNames.Length];
            var selectedIndex = -1;

            void Select(int index)
            {
                if (selectedIndex == index) return;
                for (var i = 0; i < checkboxes.Length; i++)
                    checkboxes[i].Fill = i == index ? Brushes.DimGray : Brushes.White;
                selectedIndex = index;
            }

            var grid = new Grid {VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 0, 3)};
            switch (Position2DHorizontalAlignmentProperty.Get(param.Metadata))
            {
                case PositionH1D.Left:
                    grid.HorizontalAlignment = HorizontalAlignment.Left;
                    break;
                case PositionH1D.Right:
                    grid.HorizontalAlignment = HorizontalAlignment.Right;
                    break;
                default:
                    grid.HorizontalAlignment = HorizontalAlignment.Center;
                    break;
            }

            for (var i = 0; i < 3; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition {Height = GridLength.Auto});
                grid.ColumnDefinitions.Add(new ColumnDefinition {Width = GridLength.Auto});
            }

            for (var i = 0; i < enumValues.Length; i++)
            {
                var index = i; /* Used in closure */
                var rowIndex = index / 3;
                var colIndex = index % 3;
                var checkbox = checkboxes[index] = new Rectangle
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4),
                    Width = checkboxSize, Height = checkboxSize,
                    Stroke = Brushes.Black, Fill = Brushes.White,
                    ToolTip = enumNames[index]
                };
                checkbox.MouseLeftButtonUp += (sender, e) => Select(index);
                grid.Children.Add(checkbox);
                Grid.SetRow(checkbox, rowIndex);
                Grid.SetColumn(checkbox, colIndex);
            }
            void Setter(object val) => Select((int)val);
            object Getter() => enumValues.GetValue(selectedIndex);
            void Updater(ParameterStateType state, bool value)
            {
                switch (state)
                {
                    case ParameterStateType.Enabled:
                        foreach (var checkbox in checkboxes)
                            checkbox.IsEnabled = value;
                        break;
                    case ParameterStateType.Valid:
                        for (var i = 0; i < checkboxes.Length; i++)
                            checkboxes[i].Fill = value ? (i == selectedIndex ? Brushes.DimGray : Brushes.White) : new SolidColorBrush(ViewConstants.InvalidColor);
                        break;
                }
            }
            return new PresentedParameter(param, grid, new PresentedParameter.ParamDelegates(Getter, Setter, param.IsValid, Updater));
        }

    }

}