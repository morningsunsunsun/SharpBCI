﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using SharpBCI.Core.Experiment;
using SharpBCI.Core.Staging;
using MarukoLib.Lang;
using MarukoLib.UI;
using Newtonsoft.Json;
using SharpBCI.Experiments.Speller.EyeTracking;
using SharpBCI.Experiments.Speller.P300;
using SharpBCI.Experiments.Speller.SSVEP;
using SharpBCI.Extensions;
using SharpBCI.Extensions.Data;
using SharpBCI.Extensions.Experiments;
using SharpBCI.Extensions.Patterns;
using SharpBCI.Extensions.StageProviders;

namespace SharpBCI.Experiments.Speller
{

    [ParameterizedObject(typeof(ObjectFactory))]
    public struct SubBandMixingParams : IParameterizedObject
    {

        public class ObjectFactory : ParameterizedObjectFactory<SubBandMixingParams>
        {

            private static readonly Parameter<double> A = new Parameter<double>("a", 1.25);

            private static readonly Parameter<double> B = new Parameter<double>("b", 0.25);

            public override SubBandMixingParams Create(IParameterDescriptor parameter, IReadonlyContext context) => new SubBandMixingParams(A.Get(context), B.Get(context));

            public override IReadonlyContext Parse(IParameterDescriptor parameter, SubBandMixingParams trialPreference) => new Context
            {
                [A] = trialPreference.A,
                [B] = trialPreference.B
            };

        }

        public readonly double A;

        public readonly double B;

        public SubBandMixingParams(double a, double b)
        {
            A = a;
            B = b;
        }

        public double GetSubBandWeight(int n) => Math.Pow(n, -A) + B;

        [SuppressMessage("ReSharper", "LoopCanBeConvertedToQuery")]
        public double MixSubBands(params double[] subBandComponents)
        {
            var result = 0.0;
            for (var i = 0; i < subBandComponents.Length; i++)
            {
                var component = subBandComponents[i];
                result += GetSubBandWeight(i + 1) * component * component;
            }
            return result;
        }

    }

    [Experiment(ExperimentName, "1.0")]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class SpellerExperiment : Experiment
    {

        public const string ExperimentName = "Speller";

        public class Configuration
        {

            public class GuiConfig
            {

                public ushort ColumnsOverridden;

                public uint BackgroundColor;

                public uint ForegroundColor;

                public uint CorrectTextColor;

                public uint WrongTextColor;

                public uint InputTextFontSize;

                public bool InputBarVisibility;

                public uint ButtonFontSize;

                public uint ButtonSize;

                public uint ButtonMargin;

                public Border ButtonBorder;

                public uint ButtonNormalColor;

                public uint ButtonFlashingColor;

                public uint ButtonHintColor;

                public Margins ButtonFlashingMargins;

                public Fixation ButtonFixationPoint;

            }

            public class TestConfig
            {

                public class BandpassFilter
                {

                    public readonly double LowCutOff;

                    public readonly double HighCutOff;

                    public BandpassFilter(double lowCutOff, double highCutOff)
                    {
                        LowCutOff = lowCutOff;
                        HighCutOff = highCutOff;
                    }

                    public static BandpassFilter[] Parse(MatrixQuery matrixQuery)
                    {
                        var matrix = matrixQuery?.GetMatrix();
                        if (matrix == null || matrix.Length == 0) return null;
                        var filterCount = matrix.GetRowCount();
                        var bandpassFilters = new BandpassFilter[filterCount];
                        for (var r = 0; r<filterCount; r++)
                            bandpassFilters[r] = new BandpassFilter(matrix[r, 0], matrix[r, 1]);
                        return bandpassFilters;
                    }

                    public override string ToString() => $"{LowCutOff:G1}~{HighCutOff:G1}Hz";

                }

                public bool Debug;

                public KeyboardLayout Layout;

                public SpellerParadigm Paradigm;

                public SpellerActivationMode ActivationMode;

                public bool DynamicInterval;

                public uint CursorMovementTolerance;

                public uint SelectionDelay;

                public OptionalText CopySpelling;
                
                public bool AlwaysCorrectFeedback;

                public ArrayQuery<int> Channels;

                #region P300

                public RandomTargetRate TargetRate;

                public uint SubTrialCount;

                #endregion

                #region SSVEP

                public uint ComputeParallelLevel;

                public CompositeTemporalPattern<SinusoidalPattern>[] StimulationPatterns;

                public BandpassFilter[] FilterBank;

                public SubBandMixingParams SubBandMixingParams;

                public uint HarmonicsCount;

                public uint SsvepDelay;

                public double CcaThreshold;

                #endregion

                public TrialPreference Trial;

                public bool TrialCancellable;

                public BaselinePreference Baseline;

                [JsonIgnore] public string HintText => Layout.FilterText(CopySpelling.Enabled ? CopySpelling.Text : null)?.Trim2Null();

                [JsonIgnore] public bool IsInstructed => HintText != null;

                [JsonIgnore] public ulong SubTrialDuration => Trial.Duration / SubTrialCount;

            }

            public GuiConfig Gui;

            public TestConfig Test;
        }

        public class Result : Core.Experiment.Result
        {

            public class Button
            {

                public string Label;

                public char? InputChar;

                public Button() { }

                public Button(KeyDescriptor keyDescriptor)
                {
                    Label = keyDescriptor.Name;
                    InputChar = keyDescriptor.InputChar;
                }

            }

            public class Trial
            {

                public ulong StartTime;

                public ulong EndTime;

                public bool? Correct;

                public bool Cancelled;

                public ICollection<Button> ActivedButtons;

                public Button SelectedButton;

            }

            public ulong ExperimentStartTime;

            public ulong ExperimentEndTime;

            public ICollection<Button> Buttons;

            public ICollection<Trial> Trials;

            public string HintText;

            public string InputText;

            [JsonIgnore]
            public IEnumerable<Trial> AvailableTrials => (Trials ?? EmptyArray<Trial>.Instance).Where(trial => !trial.Cancelled) ;

            [SuppressMessage("ReSharper", "InconsistentNaming")]
            public override IEnumerable<Item> Items
            {
                get
                {
                    var list = new LinkedList<Item>();
                    list.AddLast(new Item("Button Count", $"{Buttons?.Count ?? 0}"));
                    var duration = ExperimentEndTime <= ExperimentStartTime ? null : (TimeSpan?)TimeSpan.FromMilliseconds(ExperimentEndTime - ExperimentStartTime);
                    if (duration != null) list.AddLast(new Item("Spelling Duration", $"{duration.Value.TotalMinutes:G2} min"));
                    list.AddLast(new Item("Trial Count", AvailableTrials.Count().ToString()));
                    list.AddLast(new Item("  - Correct", AvailableTrials.Count(trial => trial.Correct ?? false).ToString()));
                    list.AddLast(new Item("  - Cancelled", (Trials ?? EmptyArray<Trial>.Instance).Count(trial => trial.Cancelled).ToString()));
                    if (HintText != null) list.AddLast(new Item("Hint Text", HintText));
                    list.AddLast(new Item("Input Text", InputText));
                    if (HintText != null && duration != null) // Compute BCI Utility and ITR
                    {
                        var S = AvailableTrials.Count(); // Total Count
                        var x = AvailableTrials.Count(trial => trial.Correct ?? false); // Correct Count
                        var P = x / (double) S;
                        var N = Buttons?.Count ?? 0;
                        var U = SpellerUtils.BCIUtility(N, P); // U in bits/segment
                        var ITR = SpellerUtils.ITR(N, P); // ITR in bits/segment
                        list.AddLast(new Item("Avg. Trial Duration", $"{duration.Value.TotalSeconds / S:F2} s"));
                        list.AddLast(new Item("Accuracy", $"{P * 100:F1} %"));
                        list.AddLast(new Item("BCI Utility", $"{SpellerUtils.ByTime(U, duration.Value.TotalMinutes / S):F2} bits/min"));
                        list.AddLast(new Item("ITR", $"{SpellerUtils.ByTime(ITR, duration.Value.TotalMinutes / S):F2} bits/min, {SpellerUtils.ByTime(ITR, duration.Value.TotalSeconds / S):F2} bps"));
                    }
                    return list;
                }
            }

        }

        public class Factory : ExperimentFactory<SpellerExperiment>
        {

            #region Definitions

            #region Parameters

            // Test Config

            private static readonly Parameter<bool> Debug = new Parameter<bool>("Debug", false);

            private static readonly Parameter<KeyboardLayout> Layout = Parameter<KeyboardLayout>.CreateBuilder("Layout")
                .SetSelectableValues(KeyboardLayout.Layouts.Values, true)
                .SetTypeConverters(KeyboardLayout.TypeConverter)
                .Build();

            private static readonly Parameter<SpellerParadigm> Paradigm = Parameter<SpellerParadigm>.CreateBuilder("Paradigm")
                .SetSelectableValuesForEnum(true)
                .SetTypeConverters(SpellerParadigmExt.TypeConverter)
                .Build();

            private static readonly Parameter<SpellerActivationMode> ActivationMode = Parameter<SpellerActivationMode>.OfEnum("Activation Mode", SpellerActivationMode.TwoByTwoBlock);

            private static readonly Parameter<bool> DynamicInterval = new Parameter<bool>("Dynamic Interval", true);

            private static readonly Parameter<uint> CursorMovementTolerance = new Parameter<uint>("Cursor Movement Tolerance", "dp", null, 300);

            private static readonly Parameter<uint> SelectionDelay = new Parameter<uint>("Selection Delay", "ms", null, 400);

            private static readonly Parameter<OptionalText> CopySpelling = new Parameter<OptionalText>("Copy-Spelling", new OptionalText(true, "HELLO WORLD! HYBRID-SPELLER@BCI-LAB601"));

            private static readonly Parameter<bool> AlwaysCorrectFeedback = new Parameter<bool>("Always Correct Feedback", false);

            private static readonly Parameter<ArrayQuery<int>> Channels = Parameter<ArrayQuery<int>>.CreateBuilder("Channels")
                .SetDescription("Channel indices in range of [1, channel num]")
                .SetDefaultQuery(":", TypeConverters.Double2Int)
                .Build();

            private static readonly Parameter<RandomTargetRate> TargetRate = new Parameter<RandomTargetRate>("Target Rate", new RandomTargetRate(true, 0.08F));

            private static readonly Parameter<uint> SubTrialCount = new Parameter<uint>("Sub-trial Count", 200);

            private static readonly Parameter<uint> ComputeParallelLevel = new Parameter<uint>("Compute Parallel Level", (uint)Math.Max(Environment.ProcessorCount - 1, 1));

            private static readonly Parameter<string> StimulationPatterns = Parameter<string>.CreateBuilder("Stimulation Schemes")
                .SetDescription("Available schemes is required for SSVEP, schemes are ordered by priority")
                .SetDefaultValue("14@0,20@0; 15@0,21@0; 16@0,22@0; 17@0,23@0")
                .Build();

            private static readonly Parameter<MatrixQuery> FilterBank = Parameter<MatrixQuery>.CreateBuilder("Filter Bank")
                .SetUnit("Hz").SetDescription("Nx2 Matrix to presents N-bandpass filters")
                .SetValidator(matrix =>
                {
                    var mat = matrix?.GetMatrix();
                    if (mat == null || mat.IsEmpty()) return true;
                    var col = mat.GetColCount();
                    if (col != 2) return false;
                    var row = mat.GetRowCount();
                    for (var r = 0; r < row; r++)
                        if (mat[r, 0] >= mat[r, 1])
                            return false;
                    return true;
                })
                .SetDefaultQuery("")
                .Build();

            private static readonly Parameter<SubBandMixingParams> SubBandMixingParams = new Parameter<SubBandMixingParams>("Sub-Band Mixing Params", new SubBandMixingParams(1.25F, 0.25));

            private static readonly Parameter<uint> HarmonicsCount = new Parameter<uint>("Harmonics Count", null, null, Predicates.Positive, 1);

            private static readonly Parameter<uint> SsvepDelay = new Parameter<uint>("SSVEP Delay", unit: "ms", null, 0);

            private static readonly Parameter<double> CcaThreshold = new Parameter<double>("CCA Threshold", null, null, 0.12);

            private static readonly Parameter<TrialPreference> Trial = new Parameter<TrialPreference>("Trial", null, null, new TrialPreference(4000, 2000));

            private static readonly Parameter<bool> TrialCancellable = new Parameter<bool>("Trial Cancellable", true);

            private static readonly Parameter<BaselinePreference> Baseline = Parameter<BaselinePreference>.CreateBuilder("Baseline")
                .SetDefaultValue(new BaselinePreference(false, 10000))
                .SetMetadata(BaselinePreference.Factory.TwoSidedProperty, false)
                .Build();

            // GUI Config

            private static readonly Parameter<ushort> ColumnsOverridden = new Parameter<ushort>("Override Columns", description: "Column number of buttons, 0 - default", 0);

            private static readonly Parameter<Color> BackgroundColor = new Parameter<Color>("Background Color", Color.Black);

            private static readonly Parameter<Color> ForegroundColor = new Parameter<Color>("Foreground Color", Color.White);

            private static readonly Parameter<Color> CorrectTextColor = new Parameter<Color>("Correct Text Color", Color.DarkGreen);

            private static readonly Parameter<Color> WrongTextColor = new Parameter<Color>("Wrong Text Color", Color.Red);

            private static readonly Parameter<uint> InputTextFontSize = new Parameter<uint>("Input Text Font Size", 40);

            private static readonly Parameter<bool> InputBarVisibility = new Parameter<bool>("Input Bar Visibility", true);

            private static readonly Parameter<uint> ButtonFontSize = new Parameter<uint>("Button Font Size", 40);

            private static readonly Parameter<uint> ButtonSize = new Parameter<uint>("Button Size", 300);

            private static readonly Parameter<uint> ButtonMargin = new Parameter<uint>("Button Margin", "Only applied while button size is not specified", 10);

            private static readonly Parameter<Border> ButtonBorder = new Parameter<Border>("Button Border", new Border(1, Color.White));

            private static readonly Parameter<Color> ButtonNormalColor = new Parameter<Color>("Button Normal Color", Color.Black);

            private static readonly Parameter<Color> ButtonFlashingColor = new Parameter<Color>("Button Flashing Color", Color.White);

            private static readonly Parameter<Color> ButtonHintColor = new Parameter<Color>("Button Hint Color", Color.Aqua);

            private static readonly Parameter<Margins> ButtonFlashingMargins = new Parameter<Margins>("Button Flashing Margins", new Margins(false, 0));

            private static readonly Parameter<Fixation> ButtonFixationPoint = new Parameter<Fixation>("Button Fixation Point", new Fixation(2, Color.Red));

            #endregion

            #region ParamGroups

            public static readonly ParameterGroup LayoutGroup = new ParameterGroup("Layout", Layout, ColumnsOverridden);

            public static readonly ParameterGroup ControlGroup = new ParameterGroup("Control", DynamicInterval, CursorMovementTolerance, SelectionDelay);

            public static readonly ParameterGroup InputGroup = new ParameterGroup("Input", Debug, InputTextFontSize, CopySpelling, InputBarVisibility, AlwaysCorrectFeedback);

            public static readonly ParameterGroup ParadigmGroup = new ParameterGroup("Paradigm", Paradigm, ActivationMode, Trial, TrialCancellable, Baseline);

            public static readonly ParameterGroup SignalGroup = new ParameterGroup("Signal", Channels);

            public static readonly ParameterGroup SsvepGroup = new ParameterGroup("SSVEP Parameters", ComputeParallelLevel, StimulationPatterns, FilterBank, SubBandMixingParams, SsvepDelay, HarmonicsCount, CcaThreshold);

            public static readonly ParameterGroup P300Group = new ParameterGroup("P300 Parameters", SubTrialCount, TargetRate);

            public static readonly ParameterGroup UiBasicGroup = new ParameterGroup("UI Basic", BackgroundColor, ForegroundColor, CorrectTextColor, WrongTextColor);

            public static readonly ParameterGroup UiButtonGroup = new ParameterGroup("UI Button", ButtonFontSize, ButtonSize, ButtonMargin, ButtonBorder, ButtonNormalColor, ButtonFlashingColor, ButtonHintColor, ButtonFlashingMargins, ButtonFixationPoint);

            #endregion

            #region Summary

            public static readonly ISummary ButtonCountSummary = ComputationalSummary.FromParams("Button Count", context => Layout.Get(context).KeyCount);

            public static readonly ISummary ButtonMatrixSummary = ComputationalSummary.FromParams("Button Matrix", context =>
            {
                var layoutSize = Layout.Get(context).GetLayoutSize(ColumnsOverridden.Get(context));
                return $"{layoutSize[0]}x{layoutSize[1]}";
            });

            public static readonly ISummary HintSummary = ComputationalSummary.FromParams("Hint", context =>
            {
                var copySpelling = CopySpelling.Get(context);
                if (copySpelling.IsEmpty) return "<NO HINT>";
                var filteredHint = Layout.Get(context).FilterText(copySpelling.Text);
                return filteredHint.IsEmpty() ? "<EMPTY>" : $"\"{filteredHint}\"";
            });

            public static readonly ISummary SsvepSchemesSummary = ComputationalSummary.FromExperiment<SpellerExperiment>("SSVEP Schemes", experiment =>
            {
                var activationMode = experiment.Config.Test.ActivationMode;
                var activationCount = activationMode == SpellerActivationMode.All ? experiment.Config.Test.Layout.KeyCount : (int)activationMode;
                var patterns = experiment.Config.Test.StimulationPatterns;
                var linkedList = new LinkedList<object>();
                for (var i = 0; i < activationCount; i++)
                    linkedList.AddLast(i >= patterns.Length ? "<MISSING>" : (object) patterns[i]);
                return linkedList.Join(", ");
            });

            public static readonly ISummary EstimatedFlashingCountSummary = ComputationalSummary.FromParams("Estimated Flashing Count", context => $"{SubTrialCount.Get(context) * TargetRate.Get(context).Probability:F1}");

            public static readonly ISummary SubTrialDurationSummary = ComputationalSummary.FromParams("Sub-Trial Duration", context => $"{Trial.Get(context).Duration / SubTrialCount.Get(context)} ms");

            #endregion

            #endregion

            private static CompositeTemporalPattern<SinusoidalPattern>[] ParseMultiple(string expression)
            {
                var strArray = expression.Contains(';') ? expression.Split(';') : new[] { expression };
                if (strArray.IsEmpty()) return EmptyArray<CompositeTemporalPattern<SinusoidalPattern>>.Instance;
                var schemes = new CompositeTemporalPattern<SinusoidalPattern>[strArray.Length];
                for (var i = 0; i < strArray.Length; i++)
                    schemes[i] = Parse(strArray[i]);
                return schemes;
            }

            private static CompositeTemporalPattern<SinusoidalPattern> Parse(string expression)
            {
                var strArray = expression.Contains(',') ? expression.Split(',') : new[] { expression };
                if (strArray.IsEmpty()) throw new ArgumentException(expression);
                return new CompositeTemporalPattern<SinusoidalPattern>(expression.Split(',').Select(SinusoidalPattern.Parse).ToArray());
            }

            public override IReadOnlyCollection<ParameterGroup> ParameterGroups => ScanGroups(typeof(Factory));

            public override IReadOnlyCollection<ISummary> Summaries => ScanSummaries(typeof(Factory));

            public override bool IsVisible(IReadonlyContext context, IDescriptor descriptor)
            {
                /* Groups */
                if (ReferenceEquals(descriptor, SignalGroup)) return SpellerParadigm.EyeTracking != Paradigm.Get(context);
                if (ReferenceEquals(descriptor, SsvepGroup)) return SpellerParadigm.SsvepWithEyeTracking == Paradigm.Get(context);
                if (ReferenceEquals(descriptor, P300Group)) return SpellerParadigm.P300WithEyeTracking == Paradigm.Get(context);

                /* Parameters */
                if (ReferenceEquals(Channels, descriptor)) return Paradigm.Get(context) != SpellerParadigm.EyeTracking;
                if (ReferenceEquals(ActivationMode, descriptor)) return Paradigm.Get(context) != SpellerParadigm.EyeTracking;
                if (ReferenceEquals(CursorMovementTolerance, descriptor)) return Paradigm.Get(context) != SpellerParadigm.EyeTracking && DynamicInterval.Get(context);
                if (ReferenceEquals(SelectionDelay, descriptor)) return DynamicInterval.Get(context);
                if (ReferenceEquals(InputBarVisibility, descriptor) || ReferenceEquals(ButtonHintColor, descriptor)) return CopySpelling.Get(context).Enabled;
                return true;
            }

            public override bool IsVisible(IReadonlyContext context, ISummary summary)
            {
                if (summary == HintSummary) return CopySpelling.Get(context).Enabled;
                if (summary == SsvepSchemesSummary) return Paradigm.Get(context) == SpellerParadigm.SsvepWithEyeTracking;
                if (summary == EstimatedFlashingCountSummary || summary == SubTrialDurationSummary) return Paradigm.Get(context) == SpellerParadigm.P300WithEyeTracking;
                return true;
            }

            public override ValidationResult IsValid(IReadonlyContext context, IParameterDescriptor parameter)
            {
                if (ReferenceEquals(Baseline, parameter))
                {
                    var baseline = Baseline.Get(context);
                    if (baseline.Duration != 0 && Paradigm.Get(context) == SpellerParadigm.SsvepWithEyeTracking && Trial.Get(context).Duration > baseline.Duration)
                        return ValidationResult.Failed("'baseline duration' must not shorter than 'trial duration' to take effects");
                }
                if (Paradigm.Get(context) == SpellerParadigm.SsvepWithEyeTracking && ReferenceEquals(StimulationPatterns, parameter))
                {
                    var activationMode = ActivationMode.Get(context);
                    var activationCount = activationMode == SpellerActivationMode.All ? Layout.Get(context).KeyCount : (int) activationMode;
                    if (ParseMultiple(StimulationPatterns.Get(context)).Length < activationCount)
                        return ValidationResult.Failed($"'available frequencies' must contains unless {activationCount} elements");
                }
                return base.IsValid(context, parameter);
            }

            public override SpellerExperiment Create(IReadonlyContext context)
            {
                var config = new Configuration
                {
                    Gui = new Configuration.GuiConfig
                    {
                        ColumnsOverridden = ColumnsOverridden.Get(context),
                        BackgroundColor = BackgroundColor.Get(context, ColorUtils.ToUIntArgb),
                        ForegroundColor = ForegroundColor.Get(context, ColorUtils.ToUIntArgb),
                        CorrectTextColor = CorrectTextColor.Get(context, ColorUtils.ToUIntArgb),
                        WrongTextColor = WrongTextColor.Get(context, ColorUtils.ToUIntArgb),
                        InputTextFontSize = InputTextFontSize.Get(context),
                        InputBarVisibility = InputBarVisibility.Get(context),
                        ButtonFontSize = ButtonFontSize.Get(context),
                        ButtonSize = ButtonSize.Get(context),
                        ButtonMargin = ButtonMargin.Get(context),
                        ButtonBorder = ButtonBorder.Get(context),
                        ButtonNormalColor = ButtonNormalColor.Get(context, ColorUtils.ToUIntArgb),
                        ButtonFlashingColor = ButtonFlashingColor.Get(context, ColorUtils.ToUIntArgb),
                        ButtonHintColor = ButtonHintColor.Get(context, ColorUtils.ToUIntArgb),
                        ButtonFlashingMargins = ButtonFlashingMargins.Get(context),
                        ButtonFixationPoint = ButtonFixationPoint.Get(context)
                    },
                    Test = new Configuration.TestConfig
                    {
                        Debug = Debug.Get(context),
                        Layout = Layout.Get(context),
                        Paradigm = Paradigm.Get(context),
                        ActivationMode = ActivationMode.Get(context),
                        DynamicInterval = DynamicInterval.Get(context),
                        CursorMovementTolerance = CursorMovementTolerance.Get(context),
                        SelectionDelay = SelectionDelay.Get(context),
                        CopySpelling = CopySpelling.Get(context),
                        AlwaysCorrectFeedback = AlwaysCorrectFeedback.Get(context),
                        Baseline = Baseline.Get(context),
                        Channels = Channels.Get(context),
                        TargetRate = TargetRate.Get(context),
                        SubTrialCount = SubTrialCount.Get(context),
                        ComputeParallelLevel = ComputeParallelLevel.Get(context),
                        StimulationPatterns = StimulationPatterns.Get(context, ParseMultiple),
                        FilterBank = FilterBank.Get(context, Configuration.TestConfig.BandpassFilter.Parse),
                        SubBandMixingParams= SubBandMixingParams.Get(context),
                        HarmonicsCount = HarmonicsCount.Get(context),
                        SsvepDelay = SsvepDelay.Get(context),
                        CcaThreshold = CcaThreshold.Get(context),
                        Trial = Trial.Get(context),
                        TrialCancellable = TrialCancellable.Get(context),
                    }
                };
                if (config.Test.Paradigm == SpellerParadigm.EyeTracking) config.Test.ActivationMode = SpellerActivationMode.Single;
                return new SpellerExperiment(config);
            }
            
        }

        public readonly Configuration Config;

        private SpellerExperiment(Configuration config) : base(ExperimentName) => Config = config;

        public override void Run(Session session)
        {
            var spellerController = new SpellerController();
            switch (Config.Test.Paradigm)
            {
                case SpellerParadigm.EyeTracking:
                    new EyeTrackingSpellerWindow(session, spellerController).Show();
                    break;
                case SpellerParadigm.P300WithEyeTracking:
                    new P300SpellerWindow(session, spellerController).Show();
                    break;
                case SpellerParadigm.SsvepWithEyeTracking:
                    new SsvepSpellerWindow(session, spellerController).Show();
                    break;
                default:
                    throw new NotSupportedException(Config.Test.Paradigm.GetName());
            }
        }

        internal StageProgram CreateStagedProgram(Session session, SpellerController spellerController) => new StageProgram(session.Clock, new[]
        {
            new PreparationStageProvider(),
            GetExperimentStageProvider(spellerController),
            new DelayStageProvider(3000)
        });

        private IStageProvider GetExperimentStageProvider(SpellerController spellerController)
        {
            switch (Config.Test.Paradigm)
            {
                case SpellerParadigm.EyeTracking:
                    return EyeTrackingSpellerStageProviderUtils.GetExperimentProvider(spellerController, Config.Test);
                case SpellerParadigm.P300WithEyeTracking:
                    return P300SpellerStageProviderUtils.GetExperimentProvider(spellerController, Config.Test);
                case SpellerParadigm.SsvepWithEyeTracking:
                    return SsvepSpellerStageProviderUtils.GetExperimentProvider(spellerController, Config.Test);
                default:
                    throw new NotSupportedException(Config.Test.Paradigm.GetName());
            }
        }

    }

}
