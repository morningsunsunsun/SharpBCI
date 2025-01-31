﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SharpBCI.Core.Experiment;
using SharpBCI.Core.Staging;
using MarukoLib.Lang;
using MarukoLib.UI;
using SharpBCI.Extensions.Experiments;
using SharpBCI.Extensions;
using SharpBCI.Extensions.StageProviders;

namespace SharpBCI.Experiments.CPT
{

    [Experiment(ExperimentName, "1.0")]
    public class CptExperiment : StagedExperiment.Basic
    {

        public const string ExperimentName = "Conners' Continuous Performance Test (CPT)";

        public class Configuration
        {

            public class GuiConfig
            {

                public uint BackgroundColor;

                public uint FontSize;

                public uint FontColor;

            }

            public class TestConfig
            {

                public bool Still;

                public bool PseudoRandom;

                public float TargetRate;

                public ulong LetterDuration;

                public ulong InterStimulusInterval;

                public ulong ExperimentDuration; // ms

            }

            public GuiConfig Gui;

            public TestConfig Test;

        }

        public class CptTrial
        {

            public bool Target;

            public ulong Timestamp;

            public bool Replied;

            public int ReactionTime;

            [JsonIgnore]
            public bool IsMissed => Target && !Replied;

            [JsonIgnore]
            public bool IsIncorrect => !Target && Replied;

            [JsonIgnore]
            public bool IsCorrect => Target == Replied;

            public void Reply(ulong timestamp)
            {
                Replied = true;
                ReactionTime = Math.Max((int)(timestamp - Timestamp), 0);
            }

            public override string ToString() => Timestamp + "," + Replied + "," + ReactionTime;

        }

        public class Result : Core.Experiment.Result
        {

            public ulong Duration; // milliseconds

            public ICollection<CptTrial> Trials;

            [JsonIgnore]
            public uint TotalCount => (uint)Trials.Count;

            [JsonIgnore]
            public uint TargetCount => (uint)Trials.Count(trial => trial.Target);

            [JsonIgnore]
            public uint NotTargetCount => (uint)Trials.Count(trial => !trial.Target);

            [JsonIgnore]
            public double Detectability
            {
                get
                {
                    var total = TotalCount;
                    var omissions = Omissions;
                    var commissions = Commissions;
                    return (total - omissions - commissions) / (double)(total - omissions);
                }
            }

            [JsonIgnore]
            public uint Omissions => (uint)Trials.Count(trial => trial.IsMissed);

            // incorrect response to non-targets
            [JsonIgnore]
            public uint Commissions => (uint)Trials.Count(trial => trial.IsIncorrect);

            // random or anticipatory responses (i.e. HRT < 100ms)
            [JsonIgnore]
            public uint Perserverations => (uint)Trials.Where(trial => trial.Replied).Count(trial => trial.ReactionTime >= 0 && trial.ReactionTime < 100);

            [JsonIgnore]
            public double AverageReactionTime => Trials.Where(trial => trial.Replied).Average(trail => (int?)trail.ReactionTime) ?? double.NaN;

            [JsonIgnore]
            public double ReactionTimeSd
            {
                get
                {
                    var avg = AverageReactionTime;
                    if (double.IsNaN(avg)) return double.NaN;
                    var squaredDeviation = Trials.Where(trial => trial.Replied).Average(trail => Math.Pow(trail.ReactionTime - avg, 2));
                    return Math.Sqrt(squaredDeviation);
                }
            }

            public override IEnumerable<Item> Items => new[]
                {
                    new Item("Duration", $"{TimeSpan.FromMilliseconds(Duration).TotalMinutes:G2} min"),
                    new Item("Trial Count", $"{TotalCount} (Target: {TargetCount})"),
                    Item.Separator, 
                    new Item("Detectability", $"{Detectability:P}"),
                    new Item("Omissions", $"{Omissions} ({Omissions / (double) TargetCount:P})"),
                    new Item("Commissions", $"{Commissions} ({Commissions / (double) NotTargetCount:P})"),
                    new Item("Perserverations", $"{Perserverations} ({Perserverations / (double) TotalCount:P})"),
                    new Item("Avg. Reaction Time", $"{AverageReactionTime:F2} ms"),
                    new Item("Reaction Time S.D.", $"{ReactionTimeSd:F2}")
                };
        }

        public class Factory : ExperimentFactory<CptExperiment>
        {

            // Test Config

            private static readonly Parameter<bool> Still = new Parameter<bool>("Still");

            private static readonly Parameter<bool> PseudoRandom = new Parameter<bool>("Pseudo Random", true);

            private static readonly Parameter<float> TargetRate = new Parameter<float>("Target Rate", "%", null, tr => tr >= 0 && tr <= 100, 70);

            private static readonly Parameter<ulong> LetterDuration = new Parameter<ulong>("Letter Duration", unit: "ms", null, 500);

            private static readonly Parameter<ulong> InterStimulusInterval = new Parameter<ulong>("Inter-Stimulus Interval", unit: "ms", null, 1300);

            private static readonly Parameter<ulong> ExperimentDuration = new Parameter<ulong>("Experiment Duration", unit: "min", null, 8);

            // GUI

            private static readonly Parameter<Color> BackgroundColor = new Parameter<Color>("Background Color", Color.Black);

            private static readonly Parameter<uint> FontSize = new Parameter<uint>("Font Size", 90);

            private static readonly Parameter<Color> FontColor = new Parameter<Color>("Font Color", Color.Red);

            public override IReadOnlyCollection<ParameterGroup> ParameterGroups => new[]
            {
                new ParameterGroup("Exp. Params", Still, PseudoRandom, TargetRate, LetterDuration, InterStimulusInterval, ExperimentDuration),
                new ParameterGroup("UI Window", BackgroundColor),
                new ParameterGroup("UI Font", FontSize, FontColor),
            };

            public override IReadOnlyCollection<ISummary> Summaries => new ISummary[]
            {
                ComputationalSummary.FromExperiment<CptExperiment>("Trial Count", exp => exp.StageProviders.GetStages().Count(stage => stage.Marker == IntervalMarker))
            };

            public override CptExperiment Create(IReadonlyContext context) => new CptExperiment(new Configuration
            {
                Gui = new Configuration.GuiConfig
                {
                    BackgroundColor = BackgroundColor.Get(context, ColorUtils.ToUIntArgb),
                    FontSize = FontSize.Get(context),
                    FontColor = FontColor.Get(context, ColorUtils.ToUIntArgb)
                },
                Test = new Configuration.TestConfig
                {
                    Still = Still.Get(context),
                    PseudoRandom = PseudoRandom.Get(context),
                    TargetRate = TargetRate.Get(context) / 100.0F,
                    LetterDuration = LetterDuration.Get(context),
                    InterStimulusInterval = InterStimulusInterval.Get(context),
                    ExperimentDuration = ExperimentDuration.Get(context) * 60 * 1000,
                }
            });

        }

        private const string CptGroupName = "cpt";

        [MarkerDefinition(CptGroupName)]
        public const int TargetDisplayMarker = MarkerDefinitions.CustomMarkerBase + 10;

        [MarkerDefinition(CptGroupName)]
        public const int NonTargetDisplayMarker = MarkerDefinitions.CustomMarkerBase + 11;

        [MarkerDefinition(CptGroupName)]
        public const int IntervalMarker = MarkerDefinitions.CustomMarkerBase + 20;

        [MarkerDefinition(CptGroupName)]
        public const int UserActionMarker = MarkerDefinitions.CustomMarkerBase + 30;

        public readonly Configuration Config;

        private CptExperiment(Configuration configuration) : base(ExperimentName) => Config = configuration;

        public override void Run(Session session) => new TestWindow(session).ShowDialog();

        [JsonIgnore]
        protected override IStageProvider[] StageProviders => new IStageProvider[]
        {
            new PreparationStageProvider(),
            new MarkedStageProvider(MarkerDefinitions.ExperimentStartMarker),
            new CptStageProvider(Config.Test),
            new MarkedStageProvider(MarkerDefinitions.ExperimentEndMarker),
            new DelayStageProvider(3000)
        };

    }

}
