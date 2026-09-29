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
        readonly Button setFloor;
        readonly Label debug;
        readonly Action beforeScan;
        readonly bool showDiagnostics;
        bool available;
        string debugText = string.Empty;

        /// <param name="onScanStarting">Runs before a scan starts (the HUD shows the planes for it).</param>
        /// <param name="diagnostics">Show what the scan measures under the progress line.</param>
        public ScanPanel(VisualElement hudRoot, RoomScanner roomScanner, Action onScanStarting, bool diagnostics = false)
        {
            scanner = roomScanner;
            beforeScan = onScanStarting;
            showDiagnostics = diagnostics;
            scanButton = Require<Button>(hudRoot, "scan-button");
            panel = Require<VisualElement>(hudRoot, "scan-panel");
            progress = Require<Label>(hudRoot, "scan-progress");

            // Optional: HUD layouts made for older toolkit versions do not have them.
            debug = hudRoot.Q<Label>("scan-debug");
            setFloor = hudRoot.Q<Button>("scan-set-floor");
            if (setFloor != null)
            {
                setFloor.text = RoomScanText.SetFloorButton;
                setFloor.clicked += SetFloorAtScreenCenter;
            }

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
            ShowDiagnostics();
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
            ShowDiagnostics();
        }

        /// <summary>The ring in the middle of the view shows which surface is meant.</summary>
        void SetFloorAtScreenCenter()
        {
            scanner.SetFloorAt(new UnityEngine.Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.5f));
        }

        /// <summary>The scan redraws twice a second, so the line changes at most that often.</summary>
        void ShowDiagnostics()
        {
            if (debug == null)
            {
                return;
            }

            var shown = showDiagnostics && available && scanner.IsScanning;
            HudView.SetShown(debug, shown);
            if (!shown)
            {
                return;
            }

            var text = scanner.Diagnostics.ToLine();
            if (text != debugText)
            {
                debugText = text;
                debug.text = text;
            }
        }

        static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            return root.Q<T>(name) ?? throw new InvalidOperationException(
                $"ARToolkitHUD.uxml is missing {typeof(T).Name} '{name}'. Re-import the toolkit package.");
        }
    }
}
