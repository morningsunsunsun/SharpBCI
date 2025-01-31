﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Data;
using JetBrains.Annotations;
using MarukoLib.IO;
using MarukoLib.Lang;
using MarukoLib.Lang.Exceptions;
using MarukoLib.Persistence;
using Microsoft.Win32;
using SharpBCI.Core.Experiment;

namespace SharpBCI.Windows
{

    public class SessionNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as SessionConfig.Experiment?)?.GetFormattedSessionDescriptor();

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ExperimentIdConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as SessionConfig.Experiment?)?.Params.Id;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    /// <inheritdoc cref="Window" />
    /// <summary>
    /// Interaction logic for MultiSessionConfigWindow.xaml
    /// </summary>
    [SuppressMessage("ReSharper", "NotAccessedField.Local")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    partial class MultiSessionConfigWindow : Bootstrap.ISessionListener
    {

        public class FileItem<T>
        {

            public readonly string FilePath;

            public readonly T Value;

            public FileItem(string filePath, T value)
            {
                FilePath = filePath;
                Value = value;
            }

        }

        public class ExperimentItem : FileItem<SessionConfig.Experiment>
        {

            public ExperimentItem(string filePath, SessionConfig.Experiment value) : base(filePath, value) { }

            public string SessionName => Value.GetFormattedSessionDescriptor();

            public string ExperimentId => Value.Params.Id;

        }

        public class DeviceItem : FileItem<IDictionary<string, DeviceParams>>
        {

            public DeviceItem(string filePath, IDictionary<string, DeviceParams> value) : base(filePath, value) { }

        }

        private readonly ObservableCollection<ExperimentItem> _experiments = new ObservableCollection<ExperimentItem>();

        private DeviceItem _device = null;
        
        private string _multiSessionConfigFile;

        public MultiSessionConfigWindow([CanBeNull] string multiSessionConfigFile = null)
        {
            InitializeComponent();

            ExperimentListView.ItemsSource = _experiments;
            _experiments.CollectionChanged += ExperimentsCollectionOnChanged;

            _multiSessionConfigFile = multiSessionConfigFile;
        }

        public bool IsKillOnFinish { get; set; } = false;

        private static ExperimentItem ReadExperimentConfig(string path, string alternativeDirectory)
        {
            if (!App.FindFile(alternativeDirectory, path, out var experimentFilePath))
                throw new UserException($"Experiment config file not found: {experimentFilePath}");
            if (path.EndsWith(SessionConfig.FileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                if (!JsonUtils.TryDeserializeFromFile<SessionConfig>(experimentFilePath, out var config))
                    throw new UserException($"Malformed session config file: {path}");
                return new ExperimentItem(path, config.ExperimentPart);
            }
            if (path.EndsWith(SessionConfig.Experiment.FileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                if (!JsonUtils.TryDeserializeFromFile<SessionConfig.Experiment>(experimentFilePath, out var config))
                    throw new UserException($"Malformed experiment config file: {path}");
                return new ExperimentItem(path, config);
            }
            throw new UserException($"Unsupported experiment config file type: {path}");
        }

        private static DeviceItem ReadDeviceConfig(string path, string alternativeDirectory)
        {
            if (!App.FindFile(alternativeDirectory, path, out var deviceFilePath))
                throw new UserException($"Device config file not found: {deviceFilePath}");
            if (path.EndsWith(SessionConfig.FileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                if (!JsonUtils.TryDeserializeFromFile<SessionConfig>(deviceFilePath, out var config))
                    throw new UserException($"Malformed session config file: {path}");
                return new DeviceItem(alternativeDirectory, config.DevicePart);
            }
            if (path.EndsWith(SessionConfig.DeviceFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                if (!JsonUtils.TryDeserializeFromFile<IDictionary<string, DeviceParams>>(deviceFilePath, out var config))
                    throw new UserException($"Malformed device config file: {path}");
                return new DeviceItem(alternativeDirectory, config);
            }
            throw new UserException($"Unsupported device config file type: {path}");
        }

        private static DeviceItem WriteDeviceConfig(IDictionary<string, DeviceParams> deviceConfig, string path)
        {
            deviceConfig.JsonSerializeToFile(path, JsonUtils.PrettyFormat, JsonUtils.DefaultEncoding);
            return new DeviceItem(path, deviceConfig);
        }

        public void Start()
        {
            if (_experiments.IsEmpty())
            {
                MessageBox.Show("Cannot start with no sessions.");
                return;
            }
            Close();
            Bootstrap.StartSession(_experiments.Select(item => item.Value).ToArray(), DeviceConfigPanel.DeviceConfig, false, this);
        }

        private void UpdateTitle() => Title = string.IsNullOrWhiteSpace(_multiSessionConfigFile) 
            ? "Multi-Session Configuration" 
            : $"Multi-Session Configuration: {_multiSessionConfigFile}";

        private void LoadMultiSessionConfig(string path)
        {
            if (path == null)
            {
                Clear();
                return;
            }

            var dir = Path.GetDirectoryName(Path.GetFullPath(path));

            if (!JsonUtils.TryDeserializeFromFile<MultiSessionConfig>(path, out var msCfg))
                throw new UserException($"Malformed multi-session config file: {path}");
            _experiments.Clear();

            SubjectTextBox.Text = msCfg.Subject ?? "";

            foreach (var expPath in msCfg.ExperimentConfigs ?? EmptyArray<string>.Instance) 
                _experiments.Add(ReadExperimentConfig(expPath, dir));

            _device = string.IsNullOrWhiteSpace(msCfg.DeviceConfig) ? null : ReadDeviceConfig(msCfg.DeviceConfig, dir);
            DeviceConfigPanel.DeviceConfig = _device?.Value ?? new Dictionary<string, DeviceParams>();

            _multiSessionConfigFile = path;
            UpdateTitle();
        }

        private void SaveMultiSessionConfig(string path)
        {
            new MultiSessionConfig
            {
                Subject = SubjectTextBox.Text,
                ExperimentConfigs = _experiments.Select(item => item.FilePath).ToArray(),
                DeviceConfig = _device.FilePath
            }.JsonSerializeToFile(path, JsonUtils.PrettyFormat, JsonUtils.DefaultEncoding);
            _multiSessionConfigFile = path;
            UpdateTitle();
        }

        private void Clear()
        {
            SubjectTextBox.Text = "";
            _experiments.Clear();
            _device = null;
            DeviceConfigPanel.DeviceConfig = new Dictionary<string, DeviceParams>();
        }

        private void RunExperiments_OnClick(object sender, RoutedEventArgs e) => Start();

        private void Window_OnLoaded(object sender, RoutedEventArgs e)
        {
            DeviceConfigPanel.UpdateDevices();
            LoadMultiSessionConfig(_multiSessionConfigFile);
        }

        private void ExperimentsCollectionOnChanged(object sender, NotifyCollectionChangedEventArgs e) => RunExperimentsBtn.IsEnabled = _experiments.Any();

        private void ExperimentListView_OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            // Note that you can have more than one file.
            var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? EmptyArray<string>.Instance;
            foreach (var file in files)
            {
                if (!file.EndsWith(SessionConfig.FileSuffix)) continue;
                _experiments.Add(ReadExperimentConfig(file, null));
            }
        }

        private void ExperimentListView_OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void NewMultiSessionConfigMenuItem_OnClick(object sender, RoutedEventArgs e) => Clear();

        private void OpenMultiSessionConfigMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open Multi-Session Config",
                Multiselect = false,
                CheckFileExists = true,
                DefaultExt = MultiSessionConfig.FileSuffix,
                Filter = FileUtils.GetFileFilter("Multi-Session Config File", MultiSessionConfig.FileSuffix),
            };
            if (!string.IsNullOrWhiteSpace(_multiSessionConfigFile)) dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(_multiSessionConfigFile)) ?? "";
            if (!dialog.ShowDialog(this).Value) return;
            LoadMultiSessionConfig(dialog.FileName);
        }

        private void SaveMultiSessionConfigMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_multiSessionConfigFile))
                SaveMultiSessionConfigAsMenuItem_OnClick(sender, e);
            else
                SaveMultiSessionConfig(_multiSessionConfigFile);
        }

        private void SaveMultiSessionConfigAsMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            var defaultFileName = string.IsNullOrWhiteSpace(_multiSessionConfigFile) ? "" : Path.GetFileName(_multiSessionConfigFile);
            var dialog = new SaveFileDialog
            {
                Title = "Save Multi-Session Config",
                OverwritePrompt = true,
                AddExtension = true,
                FileName = defaultFileName,
                DefaultExt = MultiSessionConfig.FileSuffix,
                Filter = FileUtils.GetFileFilter("Multi-Session Config File", MultiSessionConfig.FileSuffix),
            };
            if (!string.IsNullOrWhiteSpace(_multiSessionConfigFile)) dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(_multiSessionConfigFile)) ?? "";
            if (!dialog.ShowDialog(this).Value) return;
            SaveMultiSessionConfig(dialog.FileName);
        }

        private void AddExperimentConfigMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open Experiment Config",
                Multiselect = true,
                CheckFileExists = true,
                DefaultExt = SessionConfig.Experiment.FileSuffix,
                Filter = FileUtils.GetFileFilter("Experiment Config File", SessionConfig.Experiment.FileSuffix),
            };
            if (!string.IsNullOrWhiteSpace(_multiSessionConfigFile)) dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(_multiSessionConfigFile)) ?? "";
            if (!dialog.ShowDialog(this).Value || dialog.FileNames.IsEmpty()) return;
            foreach (var fileName in dialog.FileNames)
                _experiments.Add(ReadExperimentConfig(fileName, null));
        }

        private void LoadDeviceConfigMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open Device Config",
                Multiselect = false,
                CheckFileExists = true,
                DefaultExt = SessionConfig.DeviceFileSuffix,
                Filter = FileUtils.GetFileFilter("Device Config File", SessionConfig.DeviceFileSuffix),
            };
            if (!string.IsNullOrWhiteSpace(_device?.FilePath)) dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(_device.FilePath)) ?? "";
            else if (!string.IsNullOrWhiteSpace(_multiSessionConfigFile)) dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(_multiSessionConfigFile)) ?? "";
            if (!dialog.ShowDialog(this).Value) return;
            _device = ReadDeviceConfig(dialog.FileName, null);
        }

        private void SaveDeviceConfigMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_device?.FilePath))
                SaveDeviceConfigAsMenuItem_OnClick(sender, e);
            else
                _device = WriteDeviceConfig(DeviceConfigPanel.DeviceConfig, _device.FilePath);
        }

        private void SaveDeviceConfigAsMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            var defaultFileName = string.IsNullOrWhiteSpace(_device?.FilePath) ? $"device{SessionConfig.DeviceFileSuffix}" : Path.GetFileName(_device.FilePath);
            var dialog = new SaveFileDialog
            {
                Title = "Save Device Config",
                OverwritePrompt = true,
                AddExtension = true,
                FileName = defaultFileName,
                DefaultExt = SessionConfig.DeviceFileSuffix,
                Filter = FileUtils.GetFileFilter("Device Config File", SessionConfig.DeviceFileSuffix),
            };
            if (!string.IsNullOrWhiteSpace(_device?.FilePath)) dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(_device.FilePath)) ?? "";
            if (!dialog.ShowDialog(this).Value) return;
            _device = WriteDeviceConfig(DeviceConfigPanel.DeviceConfig, dialog.FileName);
        }

        private void SystemVariablesMenuItem_OnClick(object sender, RoutedEventArgs e) => App.ConfigSystemVariables();

        private void RemoveExperimentMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (ExperimentListView.SelectedItem is SessionConfig.Experiment)
                _experiments.RemoveAt(ExperimentListView.SelectedIndex);
        }

        private void MoveExperimentUpMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (ExperimentListView.SelectedIndex > 0 && ExperimentListView.SelectedIndex < _experiments.Count)
                _experiments.Move(ExperimentListView.SelectedIndex, ExperimentListView.SelectedIndex - 1);
        }

        private void MoveExperimentDownMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (ExperimentListView.SelectedIndex >= 0 && ExperimentListView.SelectedIndex < _experiments.Count - 1)
                _experiments.Move(ExperimentListView.SelectedIndex, ExperimentListView.SelectedIndex + 1);
        }

        void Bootstrap.ISessionListener.BeforeStart(int index, Session session) { }

        void Bootstrap.ISessionListener.AfterCompleted(int index, Session session) { }

        void Bootstrap.ISessionListener.AfterAllCompleted(Session[] sessions)
        {
            if (IsKillOnFinish) App.Kill();
        }

    }

}
