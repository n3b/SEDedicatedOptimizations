﻿using System;
using Torch;

namespace n3bOptimizations
{
    public class PluginConfig : ViewModel
    {
        private bool gasTankEnabled = true;
        private int gasTankthreshold1 = 6;
        private int gasTankthreshold2 = 3;
        private int gasTankinterval = 200;
        private int gasTankbatches = 2;

        public bool GasTankEnabled
        {
            get => gasTankEnabled;
            set => SetValue(ref gasTankEnabled, value);
        }

        public int GasTankThreshold1
        {
            get => gasTankthreshold1;
            set => SetValue(ref gasTankthreshold1, Math.Max(Math.Min(value, 100), (int) GasTankThreshold2 + 1));
        }

        public int GasTankThreshold2
        {
            get => gasTankthreshold2;
            set => SetValue(ref gasTankthreshold2, Math.Max(Math.Min(value, GasTankThreshold1 - 1), 1));
        }

        public int GasTankInterval
        {
            get => gasTankinterval;
            set => SetValue(ref gasTankinterval, Math.Max(Math.Min(value, 5000), 20));
        }

        public int GasTankBatches
        {
            get => gasTankbatches;
            set => SetValue(ref gasTankbatches, Math.Max(Math.Min(value, 5), 1));
        }

        private bool productionBlockEnabled = true;

        public bool ProductionBlockEnabled
        {
            get => productionBlockEnabled;
            set => SetValue(ref productionBlockEnabled, value);
        }

        private bool inventoryEnabled = true;
        private int inventoryInterval = 500;

        public bool InventoryEnabled
        {
            get => inventoryEnabled;
            set => SetValue(ref inventoryEnabled, value);
        }

        public int InventoryInterval
        {
            get => inventoryInterval;
            set => SetValue(ref inventoryInterval, Math.Max(Math.Min(value, 5000), 20));
        }
    }
}