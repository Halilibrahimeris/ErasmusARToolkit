using System;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// The HUD part of the room scan: the "Scan Room" / "Finish Scan" button and, while scanning, a line
    /// with the progress and a Reset button. Only offered in AR mode.
    /// </summary>
    internal sealed class ScanPanel
    {
        public const string ScanningClass = "scanning";

        readonly RoomScanner scanner;
        readonly Button scanButton;
        readonly VisualElement panel;
        readonly Label progress;
        readonly Action beforeScan;
        bool available;

        /// <param name="onScanStarting">Runs before a scan starts (the HUD shows the planes for it).</param>
        public ScanPanel(VisualElement hudRoot, RoomScanner roomScanner, Action onScanStarting)
        {
            scanner = roomScanner;
            beforeScan = onScanStarting;
            scanButton = Require<Button>(hudRoot, "scan-button");
            panel = Require<VisualElement>(hudRoot, "scan-panel");
            progress = Require<Label>(hudRoot, "scan-progress");

            scanButton.clicked += ToggleScan;
            Require<Button>(hudRoot, "scan-reset").clicked += () => scanner.ResetScan();
            scanner.ScanningChanged += OnScanningChanged;
            scanner.ProgressChanged += OnProgressChanged;
            OnScanningChanged(scanner.IsScanning);
        }

        public void Dispose()
        {
            scanner.ScanningChanged -= OnScanningChanged;
            scanner.ProgressChanged -= OnProgressChanged;
        }

        /// <summary>The scan button is shown in AR mode only.</summary>
        public void SetAvailable(bool value)
        {
            available = value;
            HudView.SetShown(scanButton, value);
            HudView.SetShown(panel, value && scanner.IsScanning);
        }

        void ToggleScan()
        {
            if (scanner.IsScanning)
            {
                scanner.FinishScan();
                return;
            }

            beforeScan?.Invoke();
            scanner.StartScan();
        }

        void OnScanningChanged(bool isScanning)
        {
            scanButton.text = RoomScanText.ButtonText(isScanning);
            scanButton.EnableInClassList(ScanningClass, isScanning);
            HudView.SetShown(panel, available && isScanning);
            OnProgressChanged(scanner.Progress);
        }

        void OnProgressChanged(RoomScanProgress value)
        {
            progress.text = RoomScanText.Progress(value);
        }

        static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            return root.Q<T>(name) ?? throw new InvalidOperationException(
                $"ARToolkitHUD.uxml is missing {typeof(T).Name} '{name}'. Re-import the toolkit package.");
        }
    }
}
