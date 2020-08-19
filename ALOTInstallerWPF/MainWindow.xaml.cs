﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ALOTInstallerWPF.BuilderUI;
using ALOTInstallerWPF.Controllers;
using MahApps.Metro.Controls;

namespace ALOTInstallerWPF
{
    /// <summary>
    /// Main window for ALOT Installer
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindow_OnContentRendered(object? sender, EventArgs e)
        {
            StartupUIController.BeginFlow(this);
        }
    }
}
