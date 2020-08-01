﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using ALOTInstallerCore.Helpers;
using ALOTInstallerCore.Objects;
using Serilog;

namespace ALOTInstallerCore.Steps.Installer
{
    /// <summary>
    /// Handles progress for the installation step
    /// </summary>
    public class ProgressHandler : INotifyPropertyChanged
    {
        /// <summary>
        /// The list of all default stages. This information is populated from the manifest and is copied into a local progress handler object
        /// </summary>
        public static List<Stage> DefaultStages = new List<Stage>();

        /// <summary>
        /// Stages that applicable for this handler
        /// </summary>
        public ObservableCollectionExtended<Stage> Stages { get; } = new ObservableCollectionExtended<Stage>();
        private double TOTAL_ACTIVE_WEIGHT => Stages.Sum(x => x.Weight);
        public int TOTAL_PROGRESS { get; private set; } = -1;
        public Stage CurrentStage { get; private set; }


        /// <summary>
        /// Adds a task to the progress tracker. These tasks must be submitted in the order that the program will execute them in. Tasks add to the weight pool and will allocate a progress slot.
        /// </summary>
        /// <param name="task">Name of stage.</param>
        public void AddTask(string stagename, Enums.MEGame game = Enums.MEGame.Unknown)
        {
            Stage pw = DefaultStages.FirstOrDefault(x => x.StageName == stagename);
            if (pw != null)
            {
                // Generate a local stage based on the global default ones.
                Stage localStage = new Stage(pw)
                {
                    StageUIIndex = Stages.Count + 1 //+1 cause these are only really used for the UI
                };
                pw.reweightStageForGame(game);
                Stages.Add(localStage);
            }
            else
            {
                Log.Error("Error adding stage for progress: " + stagename + ". Could not find stage in weighting system.");
            }
        }

        public void CompleteAndMoveToStage(string stageName)
        {
            if (CurrentStage != null)
            {
                CurrentStage.Progress = 100;
            }

            CurrentStage = Stages.FirstOrDefault(x => x.StageName == stageName);
        }

        /// <summary>
        /// Submits a new progress value for the current stage.
        /// </summary>
        /// <param name="newProgressValue">The new value of progress</param>
        /// <returns>Newly calculated overall progress integer (from 0-100, rounded down).</returns>
        public int SubmitProgress(int newProgressValue)
        {
            if (CurrentStage != null)
            {
                CurrentStage.Progress = newProgressValue;
            }

            return GetOverallProgress();
        }

        /// <summary>
        /// Call this method before your first progress submission. This will update the weights so they all add up to 1.
        /// </summary>
        public void ScaleWeights()
        {
            //recalculate total weight
            //foreach (MutableKeyValuePair<int, double> job in jobWeightList)
            //{
            //    TOTAL_ACTIVE_WEIGHT += job.Value;
            //}
            ////calculate each job's value
            //foreach (MutableKeyValuePair<int, double> job in jobWeightList)
            //{
            //    job.Value = job.Value / TOTAL_ACTIVE_WEIGHT;
            //}
        }

        public int GetOverallProgress()
        {
            double currentFinishedWeight = 0;
            //foreach (MutableKeyValuePair<int, double> job in jobWeightList)
            //{
            //    currentFinishedWeight += job.Key * job.Value; //progress * weight
            //}
            if (TOTAL_ACTIVE_WEIGHT > 0)
            {
                int progress = (int)currentFinishedWeight;
                //if (OVERALL_PROGRESS != progress)
                //{
                //    Log.Information("Overall Progress: " + progress + "%");
                //    OVERALL_PROGRESS = progress;
                //}
                return progress;
            }
            return 0;
        }

        internal void ScaleStageWeight(Stage stage, double scale)
        {
            stage.Weight *= scale;
            ScaleWeights();
        }

        internal void SetDefaultWeights()
        {
            Stages.Add(new Stage()
            {
                StageName = "STAGE_PRESCAN"
            });
            Stages.Add(new Stage()
            {
                StageName = "STAGE_UNPACKDLC",
                Weight = 0.11004021318
            });
            Stages.Add(new Stage()
            {
                StageName = "STAGE_SCAN",
                Weight = 0.12055272684
            });
            Stages.Add(new Stage()
            {
                StageName = "STAGE_REMOVE",
                Weight = 0.19155062326,
                ME1Scaling = 2.3
            });
            Stages.Add(new Stage()
            {
                StageName = "STAGE_INSTALL",
                Weight = 0.31997680866
            });
            Stages.Add(new Stage()
            {
                StageName = "STAGE_SAVE",
                Weight = 0.25787962804
            });
            Stages.Add(new Stage()
            {
                StageName = "STAGE_REPACK",
                Weight = 0.0800000
            });
            Stages.Add(new Stage()
            {
                StageName = "STAGE_INSTALLMARKERS",
                Weight = 0.0400000
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    //public class MutableKeyValuePair<TKey, TValue>
    //{
    //    public TKey Key { get; set; }
    //    public TValue Value { get; set; }

    //    public MutableKeyValuePair(TKey key, TValue value)
    //    {
    //        Key = key;
    //        Value = value;
    //    }
    //}
}
