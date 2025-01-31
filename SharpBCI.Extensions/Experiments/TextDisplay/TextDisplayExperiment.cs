﻿using System.Collections.Generic;
using System.Drawing;
using MarukoLib.Lang;
using MarukoLib.UI;
using Newtonsoft.Json;
using SharpBCI.Core.Experiment;
using SharpBCI.Core.Staging;
using SharpBCI.Extensions.StageProviders;

namespace SharpBCI.Extensions.Experiments.TextDisplay
{

    [Experiment(ExperimentName, "1.0", Description = "A text display paradigm.")]
    public class TextDisplayExperiment : StagedExperiment.Basic
    {

        public const string ExperimentName = "Text Display";

        public class Configuration
        {

            public class GuiConfig
            {

                public uint BackgroundColor;

                public uint ForegroundColor;

                public uint FontSize;

                public string Text;

            }

            public class TestConfig
            {

                public ushort TrialCount;

                public uint TrialDuration;

                public uint InterStimulusInterval;

            }

            public GuiConfig Gui;

            public TestConfig Test;

        }

        public class Factory : ExperimentFactory<TextDisplayExperiment>
        {

            // Test

            private static readonly Parameter<ushort> TrialCount = new Parameter<ushort>("Trial Count", null, null, Predicates.Positive, 5);

            private static readonly Parameter<uint> TrialDuration = new Parameter<uint>("Trial Duration", "ms", null, Predicates.Positive, 10000);

            private static readonly Parameter<uint> InterStimulusInterval = new Parameter<uint>("Inter-Stimulus Interval", "ms", null, Predicates.Positive, 2000);

            // GUI

            private static readonly Parameter<Color> BackgroundColor = new Parameter<Color>("Background Color", Color.Black);

            private static readonly Parameter<Color> ForegroundColor = new Parameter<Color>("Foreground Color", Color.Red);

            private static readonly Parameter<uint> FontSize = new Parameter<uint>("Font Size", 90);

            private static readonly Parameter<string> Text = new Parameter<string>("Display Content", defaultValue: "Text");

            public override IReadOnlyCollection<ParameterGroup> ParameterGroups => ScanGroups(typeof(Factory));

            public override IReadOnlyCollection<ISummary> Summaries => ScanSummaries(typeof(Factory));

            public override TextDisplayExperiment Create(IReadonlyContext context) => new TextDisplayExperiment(new Configuration
            {
                Gui = new Configuration.GuiConfig
                {
                    BackgroundColor = BackgroundColor.Get(context, ColorUtils.ToUIntArgb),
                    ForegroundColor = ForegroundColor.Get(context, ColorUtils.ToUIntArgb),
                    FontSize = FontSize.Get(context),
                    Text = Text.Get(context),
                },
                Test = new Configuration.TestConfig
                {
                    TrialCount = TrialCount.Get(context),
                    TrialDuration = TrialDuration.Get(context),
                    InterStimulusInterval = InterStimulusInterval.Get(context),
                }
            });

        }

        public readonly Configuration Config;

        public TextDisplayExperiment(Configuration configuration) : base(ExperimentName) => Config = configuration;

        public override void Run(Session session) => new TestWindow(session).ShowDialog();

        [JsonIgnore]
        protected override IStageProvider[] StageProviders => new IStageProvider[]
        {
            new PreparationStageProvider(),
            new MarkedStageProvider(MarkerDefinitions.ExperimentStartMarker),
            new RepeatingStageProvider.Static(new[]
            {
                new Stage {Cue = Config.Gui.Text, Marker = MarkerDefinitions.TrialStartMarker, Duration = Config.Test.TrialDuration},
                new Stage {Marker = MarkerDefinitions.TrialEndMarker, Duration = Config.Test.InterStimulusInterval},
            }, Config.Test.TrialCount),
            new MarkedStageProvider(MarkerDefinitions.ExperimentEndMarker),
            new DelayStageProvider(1000)
        };

    }

}
